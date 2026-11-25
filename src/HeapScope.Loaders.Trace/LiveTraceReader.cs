// HeapScope
// Copyright (C) 2026 Ray Klundt
// https://github.com/klundt-org/HeapScope
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, version 3 only, with additional terms
// under Section 7 of that license.
//
// Additional terms, including attribution requirements, apply.
// See the ADDITIONAL-TERMS.md file distributed with this program.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program (see the LICENSE file). If not, see
// <https://www.gnu.org/licenses/>.
//
// Commercial licenses are available. See COMMERCIAL.md.

using HeapScope.Core;
using HeapScope.Core.Allocations;
using Microsoft.Diagnostics.Tracing;

namespace HeapScope.Loaders.Trace;

/// <summary>
/// Reads the sampled allocations of a trace while it is recorded (sprint 4.3, ADR-037): a thread of its own follows the growing
/// file through <see cref="LiveTraceStream"/> and adds the samples up in <see cref="LiveAllocations"/>. Nothing of the file is
/// kept but that, and the finished file stays the record (<see cref="AllocationTraceReader"/> reads it).
/// </summary>
public sealed class LiveTraceReader : IDisposable
{
    /// <summary>Changes when how a live trace is read or added up changes (the worker's versions line).</summary>
    public const int Version = 1;

    /// <summary>The memory a live trace reserves while it is read.</summary>
    public const long ReservedBytes = LiveAllocationLimits.ReservedBytes;

    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly LiveMethodNames _names = new();
    private readonly LiveAllocations _allocations;
    private readonly LiveTraceStream _stream;
    private readonly Thread _thread;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly EventStackReader? _stacks;
    private readonly string? _stackProblem;
    private readonly string? _namesProblem;
    private readonly int _namedBefore;
    private readonly string _path;
    private volatile bool _ended;
    private int _processId;
    private string? _processName;
    private DateTime _startUtc;
    private double _durationMs;
    private long _total;
    private int _lost;
    private double? _delayMs;
    private bool _hasRundown;
    private bool _completed;
    private string? _problem;
    private string? _problemDetail;
    private Exception? _failure;

    private LiveTraceReader(string path, string? namesPath, LiveAllocationLimits limits)
    {
        _path = path;
        _allocations = new LiveAllocations(_names, limits);
        (_stacks, _stackProblem) = EventStackReader.Create();
        if (namesPath is not null)
        {
            _namesProblem = ReadNames(namesPath);
            _namedBefore = _names.Count;
        }

        _stream = new LiveTraceStream(path, () => _ended, _stop.Token);
        _thread = new Thread(Run) { IsBackground = true, Name = "HeapScope live trace" };
    }

    /// <summary>Starts reading <paramref name="path"/>, which may hold nothing yet.</summary>
    /// <param name="namesPath">A trace with the rundown of the methods compiled before (deleted or damaged is no failure).</param>
    /// <exception cref="SnapshotFormatException">The file cannot be opened.</exception>
    public static LiveTraceReader Start(string path, string? namesPath) => Start(path, namesPath, LiveAllocationLimits.Default);

    internal static LiveTraceReader Start(string path, string? namesPath, LiveAllocationLimits limits)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var fullPath = Path.GetFullPath(path);
        try
        {
            var reader = new LiveTraceReader(fullPath, namesPath, limits);
            reader._thread.Start();
            return reader;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SnapshotFormatException($"{fullPath} could not be read: {ex.Message}", ex);
        }
    }

    /// <summary>Ends when reading does: the recording ended and everything was read, reading failed, or it was stopped.</summary>
    public Task Completion => _completion.Task;

    /// <summary>An error that is HeapScope's own, if one ended the reading (for the log).</summary>
    public Exception? Failure => Volatile.Read(ref _failure);

    /// <summary>The recording ended and its file is closed: the reader finishes what it holds.</summary>
    public void End() => _ended = true;

    /// <summary>Where the trace stands; the delay is of the samples since the last time this was asked.</summary>
    public LiveTraceStatus Status()
    {
        lock (_gate)
        {
            var delay = _delayMs;
            _delayMs = null;
            return new LiveTraceStatus(
                _allocations.Version,
                _durationMs,
                _allocations.BucketMs,
                _allocations.Samples,
                _allocations.Bytes,
                _allocations.Types,
                _allocations.Entries,
                _allocations.StackNodes,
                _names.Count,
                _lost,
                _allocations.WithoutStack,
                _allocations.Unnamed(),
                _allocations.PastLimits,
                delay,
                _stream.CaughtUp,
                _completed,
                _stream.BytesRead,
                _stream.Checked?.ToString(),
                _problem,
                _problemDetail,
                _stackProblem,
                _namesProblem,
                _namedBefore);
        }
    }

    /// <summary>The process, as the first samples name it, and when the recording started.</summary>
    public (int ProcessId, string? ProcessName, DateTime StartUtc) Source()
    {
        lock (_gate)
        {
            return (_processId, _processName, _startUtc);
        }
    }

    /// <summary>What was read so far, as the queries read a trace: entries added up by stretch of time (<see cref="AllocationTrace.SampleCounts"/>).</summary>
    public AllocationTrace View()
    {
        lock (_gate)
        {
            return _allocations.View(new AllocationTraceInfo(
                _processId,
                _processName,
                _startUtc,
                _durationMs,
                _lost,
                _allocations.WithoutStack,
                _allocations.Unnamed(),
                _hasRundown));
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (_thread.IsAlive && Thread.CurrentThread != _thread)
        {
            _thread.Join();
        }

        _stream.Dispose();
        _stop.Dispose();
    }

    private void Run()
    {
        try
        {
            using var source = TraceReads.OpenLive(_stream);
            TraceReads.Subscribe(source, OnTick, OnMethod, OnEvent);
            TraceReads.Process(source);
            lock (_gate)
            {
                _lost = TraceReads.Lost(source);
            }

            if (_stream.Refusal is { } refusal)
            {
                Stopped(refusal.Message, "the check refused it");
            }
        }
        catch (Exception ex) when (_stop.IsCancellationRequested)
        {
            // Stopped: whatever the source made of its stream ending is not the file's fault, nor anyone's to know.
            _ = ex;
        }
        catch (Exception) when (_stream.Refusal is { } refusal)
        {
            Stopped(refusal.Message, "the check refused it");
        }
        catch (AmountException ex)
        {
            Stopped($"{_path} {ex.Message}: the file may be damaged.", "amount of a sample");
        }
        catch (Exception ex) when (TraceReads.IsFileError(ex))
        {
            // A file that ended before its first event is no error of the recording's.
            if (_stream.BytesRead > 0)
            {
                Stopped($"{_path} could not be read as it was recorded ({ex.GetType().Name}).", TraceReads.Origin(ex));
            }
        }
        catch (OutOfMemoryException ex)
        {
            Stopped("There is too little memory to follow the recording. The trace itself is still recorded.", TraceReads.Origin(ex));
        }
        catch (Exception ex)
        {
            // A boundary: the thread's error is logged by whoever reads Failure, and the view keeps what it has.
            Volatile.Write(ref _failure, ex);
            Stopped("Following the recording failed. The trace itself is still recorded; details are in the log.", TraceReads.Origin(ex));
        }
        finally
        {
            lock (_gate)
            {
                _completed = true;
            }

            _completion.TrySetResult();
        }
    }

    private void Stopped(string problem, string detail)
    {
        lock (_gate)
        {
            (_problem, _problemDetail) = (problem, detail);
        }
    }

    private void OnTick(TraceEvent e, LiveSample sample)
    {
        var stack = _stacks is { } stacks ? stacks.Stack(e) : [];
        lock (_gate)
        {
            if (AllocationTraceReader.RefusedAmount(sample.Bytes, ref _total) is { } amount)
            {
                throw new AmountException(amount);
            }

            if (_processId == 0 && sample.ProcessId > 0)
            {
                (_processId, _processName) = (sample.ProcessId, string.IsNullOrEmpty(sample.ProcessName) ? null : sample.ProcessName);
            }

            var delay = (DateTime.UtcNow - sample.Utc).TotalMilliseconds;
            _delayMs = Math.Max(_delayMs ?? 0, Math.Max(0, delay));
            _allocations.Add(sample.TimeMs, sample.TypeName, sample.Bytes, sample.Kind, stack);
        }
    }

    private void OnMethod(ulong start, uint size, string name)
    {
        lock (_gate)
        {
            _names.Add(start, size, name);
        }
    }

    private void OnEvent(LiveEvent e)
    {
        lock (_gate)
        {
            _startUtc = e.SessionStartUtc;
            _durationMs = Math.Max(_durationMs, e.TimeMs);
            _hasRundown |= e.IsRundown;
        }
    }

    /// <summary>The names of the methods compiled before the recording, from a short trace of its own: why there are none, or null.</summary>
    private string? ReadNames(string path)
    {
        try
        {
            using (var check = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                TraceFileCheck.Check(check, path, _stop.Token);
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var source = TraceReads.OpenLive(stream);
            TraceReads.Subscribe(source, null, OnMethod, null);
            TraceReads.Process(source);
            return _names.Count == 0 ? "it names none" : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SnapshotFormatException || TraceReads.IsFileError(ex))
        {
            return TraceReads.Origin(ex);
        }
    }

    private sealed class AmountException(string message) : Exception(message);
}
