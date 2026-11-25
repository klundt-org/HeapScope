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

using System.Globalization;
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

    /// <summary>A view is used again for this long while samples arrive, and addresses are named at most this often.</summary>
    private static readonly TimeSpan s_viewFor = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_renameEvery = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly LiveMethodNames _names = new();
    private readonly LiveAllocations _allocations;
    private readonly LiveTraceStream _stream;
    private readonly Thread _thread;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly EventStackReader? _stacks;
    private readonly string? _stackProblem;
    private readonly string? _namesPath;
    private readonly string _path;
    private string? _namesProblem;
    private int _namedBefore;
    private int _methods;
    private long _unnamed;
    private readonly Action<string>? _notice;
    private volatile bool _ended;
    private int _processId;
    private string? _processName;
    private DateTime _startUtc;
    private double _durationMs;
    private long _total;
    private int _lost;
    private double? _delayMs;
    private double _longestDelayMs;
    private bool _hasRundown;
    private bool _completed;
    private string? _problem;
    private string? _problemDetail;
    private Exception? _failure;
    private LiveTraceView? _view;
    private long _viewAt;
    private long _renamedAt;

    private LiveTraceReader(string path, string? namesPath, LiveAllocationLimits limits, Action<string>? notice)
    {
        _path = path;
        _namesPath = namesPath;
        _notice = notice;
        _namesProblem = namesPath is null ? "none recorded" : null;
        _allocations = new LiveAllocations(_names, limits, notice);
        (_stacks, _stackProblem) = EventStackReader.Create();
        _stream = new LiveTraceStream(path, () => _ended, _stop.Token);
        _thread = new Thread(Run) { IsBackground = true, Name = "HeapScope live trace" };
    }

    /// <summary>Starts reading <paramref name="path"/>, which may hold nothing yet.</summary>
    /// <param name="namesPath">A trace with the rundown of the methods compiled before (deleted or damaged is no failure).</param>
    /// <param name="notice">Told, under the reader's lock, what changes the meaning of figures already given: for the log.</param>
    /// <exception cref="SnapshotFormatException">The file cannot be opened.</exception>
    public static LiveTraceReader Start(string path, string? namesPath, Action<string>? notice = null) => Start(path, namesPath, LiveAllocationLimits.Default, notice);

    internal static LiveTraceReader Start(string path, string? namesPath, LiveAllocationLimits limits, Action<string>? notice = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var fullPath = Path.GetFullPath(path);
        try
        {
            var reader = new LiveTraceReader(fullPath, namesPath, limits, notice);
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

    /// <summary>Whether the recording was said to have ended.</summary>
    public bool Ended => _ended;

    /// <summary>Whether the file was read to its own end: the trace is whole.</summary>
    public bool ReadToEnd => _stream.Checked?.End == TraceCheckResult.Whole;

    /// <summary>The recording ended and its file is closed: the reader finishes what it holds.</summary>
    public void End() => _ended = true;

    /// <summary>Where the trace stands; the delay is of the samples since the last time this was asked.</summary>
    public LiveTraceStatus Status()
    {
        lock (_gate)
        {
            var status = Peek();
            _delayMs = null;
            return status;
        }
    }

    /// <summary>As <see cref="Peek"/>, the longest delay counted anew from here: for the log.</summary>
    public LiveTraceStatus Report()
    {
        lock (_gate)
        {
            var status = Peek();
            _longestDelayMs = 0;
            return status;
        }
    }

    /// <summary>As <see cref="Status"/>, the delay left for the next one to ask.</summary>
    public LiveTraceStatus Peek()
    {
        lock (_gate)
        {
            Rename();
            return new LiveTraceStatus(
                _allocations.Version,
                _durationMs,
                _allocations.BucketMs,
                _allocations.Samples,
                _allocations.Bytes,
                _allocations.Types,
                _allocations.Entries,
                _allocations.StackNodes,
                _completed ? _methods : _names.Count,
                _names.LeftOut,
                _lost,
                _allocations.WithoutStack,
                _unnamed,
                _allocations.PastLimits,
                _delayMs,
                _longestDelayMs,
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

    /// <summary>
    /// What was read so far, as the queries read a trace: entries added up by stretch of time (<see cref="AllocationTrace.SampleCounts"/>).
    /// While samples arrive, the same view answers for a second, so that questions asked together are answered from one; once reading
    /// is over it is everything.
    /// </summary>
    public LiveTraceView View()
    {
        lock (_gate)
        {
            if (_view is { } known
                && ((known.Version == _allocations.Version && known.Completed == _completed) || (!_completed && Environment.TickCount64 - _viewAt < s_viewFor.TotalMilliseconds)))
            {
                return known;
            }

            Rename();
            var info = new AllocationTraceInfo(
                _processId,
                _processName,
                _startUtc,
                _durationMs,
                _lost,
                _allocations.WithoutStack,
                _unnamed,
                _hasRundown);
            _view = new LiveTraceView(_allocations.View(info), _allocations.BucketMs, _allocations.Version, _completed);
            _viewAt = Environment.TickCount64;
            return _view;
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

    /// <summary>Folding the stacks again takes as long as there are entries: not for every method that is compiled.</summary>
    private void Rename()
    {
        if (_completed || _renamedAt == 0 || Environment.TickCount64 - _renamedAt >= s_renameEvery.TotalMilliseconds)
        {
            if (_allocations.Rename())
            {
                _view = null;
            }

            // A walk over everything kept: with the folding, not at every question.
            _unnamed = _allocations.Unnamed();
            _renamedAt = Environment.TickCount64;
        }
    }

    private void Run()
    {
        var clean = false;
        try
        {
            if (_namesPath is { } namesPath)
            {
                var problem = ReadNames(namesPath);
                lock (_gate)
                {
                    (_namesProblem, _namedBefore) = (problem, _names.Count);
                }
            }

            _notice?.Invoke(string.Create(CultureInfo.InvariantCulture, $"stacks {_stackProblem ?? "are read"}; {_namedBefore} methods compiled before are named ({_namesProblem ?? "all that were recorded"})"));

            using var source = TraceReads.OpenLive(_stream);
            TraceReads.Subscribe(source, OnTick, OnMethod, OnUnload, OnEvent);
            TraceReads.Process(source);
            lock (_gate)
            {
                // Some are only seen at the end.
                _lost = TraceReads.Lost(source);
                clean = true;
            }

            if (_stream.Refusal is { } refusal)
            {
                Stopped(refusal.Message, _stream.RefusalDetail);
            }
        }
        catch (Exception ex) when (_stop.IsCancellationRequested)
        {
            // Stopped: whatever the source made of its stream ending is not the file's fault, nor anyone's to know.
            _ = ex;
        }
        catch (Exception) when (_stream.Refusal is { } refusal)
        {
            Stopped(refusal.Message, _stream.RefusalDetail);
        }
        catch (AmountException ex)
        {
            Stopped($"{_path} {ex.Message}: the file may be damaged.", ex.Detail);
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
            Complete(clean);
        }
    }

    /// <summary>
    /// Reading is over, however it ended. The last names are taken only after a clean end: after an error, such as too little memory,
    /// it could fail again, and an error that leaves this thread would end the worker.
    /// </summary>
    private void Complete(bool clean)
    {
        try
        {
            lock (_gate)
            {
                if (clean)
                {
                    _allocations.Rename();
                    _unnamed = _allocations.Unnamed();
                }
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _failure, ex);
            Stopped("Following the recording failed. The trace itself is still recorded; details are in the log.", TraceReads.Origin(ex));
        }
        finally
        {
            lock (_gate)
            {
                // What names the addresses is over with: an unnamed frame stays one.
                _methods = _names.Count;
                _names.Clear();
                _view = null;
                _completed = true;
            }

            _completion.TrySetResult();
        }
    }

    private void Stopped(string problem, string? detail)
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
                throw new AmountException(amount, string.Create(CultureInfo.InvariantCulture, $"amount of sample {_allocations.Samples + 1} at {sample.TimeMs:F1} ms"));
            }

            if (_processId == 0)
            {
                (_processId, _processName) = TraceReads.ProcessOf(e);
            }

            var delay = Math.Max(0, (DateTime.UtcNow - sample.Utc).TotalMilliseconds);
            _delayMs = Math.Max(_delayMs ?? 0, delay);
            if (_stream.HasCaughtUp)
            {
                // Until then it meets what the file held before, as old as that is.
                _longestDelayMs = Math.Max(_longestDelayMs, delay);
            }

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

    private void OnUnload(ulong start)
    {
        lock (_gate)
        {
            _names.Remove(start);
        }
    }

    private void OnEvent(LiveEvent e)
    {
        lock (_gate)
        {
            _startUtc = e.SessionStartUtc;
            _durationMs = Math.Max(_durationMs, e.TimeMs);
            _hasRundown |= e.IsRundown;
            _lost = e.Lost;
        }
    }

    /// <summary>The names of the methods compiled before the recording, from a short trace of its own: why there are none, or not all, or null.</summary>
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
            var methods = new List<(ulong, uint, string)>();
            var (text, leftOut) = (0L, 0L);
            TraceReads.Subscribe(
                source,
                null,
                (start, size, name) =>
                {
                    _stop.Token.ThrowIfCancellationRequested();
                    text += name.Length;
                    if (methods.Count < LiveMethodNames.MaxMethods && text * 2 <= LiveMethodNames.MaxTextBytes)
                    {
                        methods.Add((start, size, name));
                    }
                    else
                    {
                        leftOut++;
                    }
                },
                null,
                null);
            TraceReads.Process(source);
            lock (_gate)
            {
                _names.AddAll(methods);
                _names.LeaveOut(leftOut);
            }

            return TraceReads.Lost(source) is > 0 and var lost
                ? string.Create(CultureInfo.InvariantCulture, $"{lost} events of them were lost")
                : methods.Count == 0 ? "it names none" : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SnapshotFormatException || TraceReads.IsFileError(ex))
        {
            return TraceReads.Origin(ex);
        }
    }

    private sealed class AmountException(string message, string detail) : Exception(message)
    {
        public string Detail { get; } = detail;
    }
}
