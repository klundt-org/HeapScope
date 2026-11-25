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
using HeapScope.Application.Sessions;
using HeapScope.Capture;
using Microsoft.Extensions.Logging;

namespace HeapScope.Application.Monitoring;

/// <summary>
/// A monitoring run's allocation trace (sprint 3.4, split from the run in 3.8, peer review P11): recorded into the run's
/// session folder, watched so it stops at its size limit or when the disk runs low, and stopped after the run's
/// captures. Its watch is awaited before anything it uses is disposed. A refusal or failure is reported through
/// <c>problem</c> once and the run goes on.
/// </summary>
internal sealed class AllocationRecording : IAsyncDisposable
{
    private static readonly TimeSpan s_checkInterval = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _stop = new();
    private readonly ILogger _logger;
    private readonly Guid _runId;
    private readonly int _processId;
    private readonly long _limitBytes;
    private readonly TimeProvider _time;
    private readonly Func<Task[]> _pendingCaptures;
    private readonly Action<string, Exception?> _problem;
    private readonly long _startedAt;
    private Action<LiveTraceStart>? _started;
    private Action<string>? _ended;
    private Task<TraceResult>? _trace;
    private Task _watch = Task.CompletedTask;
    private Task _told = Task.CompletedTask;
    private long _endedAt;
    private bool _recording;
    private bool _asked;

    private AllocationRecording(Guid runId, int processId, long limitBytes, TimeProvider time, ILogger logger, Func<Task[]> pendingCaptures, Action<string, Exception?> problem)
    {
        _runId = runId;
        _processId = processId;
        _limitBytes = limitBytes;
        _time = time;
        _logger = logger;
        _pendingCaptures = pendingCaptures;
        _problem = problem;
        _startedAt = time.GetTimestamp();
    }

    /// <summary>The trace file being written, once started.</summary>
    public string? Path { get; private set; }

    /// <summary>
    /// Starts recording into <paramref name="folder"/>; a refusal (no folder, the recorder failing to start) is reported
    /// at once and the recording does nothing more.
    /// </summary>
    public static AllocationRecording Start(
        string? folder,
        Guid runId,
        int processId,
        long limitBytes,
        AllocationRecorder recorder,
        TimeProvider time,
        ILogger logger,
        Func<Task[]> pendingCaptures,
        Action<string, Exception?> problem,
        TimeSpan counterInterval,
        Func<bool>? busy = null,
        Action<LiveTraceStart>? started = null,
        Action<string>? ended = null)
    {
        var recording = new AllocationRecording(runId, processId, limitBytes, time, logger, pendingCaptures, problem) { _started = started, _ended = ended };
        try
        {
            var target = folder ?? throw new IOException("The run has no session folder to record the allocation trace into.");
            recording.Path = System.IO.Path.Combine(target, string.Create(CultureInfo.InvariantCulture, $"allocations-{processId}-{time.GetLocalNow():yyyyMMdd-HHmmss}.nettrace"));
            logger.LogInformation("Monitoring run {RunId} (pid {Pid}): starting an allocation trace to {Path}, limit {Limit} bytes", runId, processId, recording.Path, limitBytes);
            recording._trace = recorder(new AllocationRecordingRequest(processId, recording.Path, counterInterval, busy?.Invoke() == true ? "a capture of the process runs" : null, recording.OnStarted), recording._stop.Token);
            recording._told = recording._trace.ContinueWith(recording.OnEnded, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            recording._watch = recording.WatchAsync(recording.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CaptureFailedException)
        {
            problem($"The allocation trace could not start: {ex.Message}", ex);
        }

        return recording;
    }

    /// <summary>
    /// Stops the trace (after the run's captures, which the caller awaited: stopping a runtime session while a gcdump's
    /// is open can induce another collection) and returns what it kept, or null with the failure reported.
    /// </summary>
    public async Task<TraceResult?> StopAsync()
    {
        if (_trace is not { } trace)
        {
            return null;
        }

        _asked = !trace.IsCompleted;
        await _stop.CancelAsync().ConfigureAwait(false);
        await _watch.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await _told.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        try
        {
            var written = await trace.ConfigureAwait(false);
            _logger.LogInformation("Monitoring run {RunId} (pid {Pid}): allocation trace {Path}, {Bytes} bytes over {Seconds:N0} s{Early}", _runId, _processId, written.Path, written.Bytes, written.Duration.TotalSeconds, written.StoppedEarly && !_asked ? ", ended before it was asked to" : string.Empty);
            return written;
        }
        catch (OperationCanceledException ex) when (_stop.IsCancellationRequested)
        {
            // Stopped while the session was still starting: nothing was recorded. The user asked for a trace, so they hear why there
            // is none, and the time tells a quick Stop from a session that never started. Passing the exception adds this to an early
            // stop's "what it recorded is kept", which it corrects.
            var endedAt = Volatile.Read(ref _endedAt);
            var seconds = _time.GetElapsedTime(_startedAt, endedAt != 0 ? endedAt : _time.GetTimestamp()).TotalSeconds;
            _problem(string.Create(CultureInfo.InvariantCulture, $"No allocation trace: it stopped {seconds:N0} s in, before the trace session had started."), ex);
            return null;
        }
        catch (Exception ex)
        {
            // A boundary: whatever the recorder threw, the run still closes its timeline and records its end.
            _problem($"The allocation trace failed: {ex.Message}", ex);
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await _watch.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _stop.Dispose();
    }

    private void OnStarted(LiveTraceStart start)
    {
        Volatile.Write(ref _recording, true);
        _logger.LogInformation(
            "Monitoring run {RunId} (pid {Pid}): the allocation trace {Path} is being written; names of the methods compiled so far {Names} in {NamesMs:N0} ms",
            _runId,
            _processId,
            start.Path,
            start.NamesProblem ?? "recorded",
            start.NamesTime.TotalMilliseconds);
        _started?.Invoke(start);
    }

    /// <summary>
    /// Nothing more is written to the file, whatever ended it: whoever reads it beside the recording is told (ADR-037), and only if
    /// they were told it started. A trace that ends before the run (refused, failed) is reported at once.
    /// </summary>
    private void OnEnded(Task<TraceResult> trace)
    {
        Volatile.Write(ref _endedAt, _time.GetTimestamp());
        if (Volatile.Read(ref _recording))
        {
            _logger.LogInformation(
                "Monitoring run {RunId} (pid {Pid}): the allocation trace {Path} is written, {Seconds:N1} s in: {How}",
                _runId,
                _processId,
                Path,
                _time.GetElapsedTime(_startedAt, _endedAt).TotalSeconds,
                trace.IsFaulted ? "it failed" : _stop.IsCancellationRequested ? "it was stopped" : "it ended by itself");
            _ended?.Invoke(Path!);
        }

        if (trace is { IsCompletedSuccessfully: true, Result.Problem: { } failed })
        {
            _problem($"The allocation trace stopped early because {failed.TrimEnd('.')}; what it recorded is kept and the run goes on.", null);
        }

        if (trace.IsFaulted && !_stop.IsCancellationRequested)
        {
            var error = trace.Exception!.InnerException ?? trace.Exception;
            _problem($"The allocation trace stopped: {error.Message} The run goes on without it.", error);
        }
    }

    /// <summary>Stops the trace at its size limit or when the disk has less than <see cref="MonitorRun.TraceMinimumFreeBytes"/> free.</summary>
    private async Task WatchAsync(string path)
    {
        try
        {
            while (!_stop.IsCancellationRequested && _trace is { IsCompleted: false })
            {
                await Task.Delay(s_checkInterval, _time, _stop.Token).ConfigureAwait(false);
                // The recorder writes the file under its final name (4.3), once its session starts.
                var file = new FileInfo(path);
                var size = file.Exists ? file.Length : 0;
                var free = new DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(path))!).AvailableFreeSpace;
                string? why = size >= _limitBytes
                    ? string.Create(CultureInfo.InvariantCulture, $"it reached its limit of {_limitBytes / (1024 * 1024)} MB")
                    : free < MonitorRun.TraceMinimumFreeBytes ? string.Create(CultureInfo.InvariantCulture, $"the disk has {free / (1024 * 1024)} MB free") : null;
                if (why is not null)
                {
                    _problem($"The allocation trace stopped early because {why}; what it recorded is kept and the run goes on.", null);

                    // Not while a capture of the process runs: stopping a runtime session while a gcdump's is open can
                    // induce another collection.
                    await Task.WhenAll(_pendingCaptures()).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                    await _stop.CancelAsync().ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(ex, "Monitoring run {RunId}: the allocation trace's size could not be checked", _runId);
        }
    }
}
