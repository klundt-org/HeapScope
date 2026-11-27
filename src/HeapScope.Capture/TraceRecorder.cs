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

using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using HeapScope.Core;

namespace HeapScope.Capture;

/// <summary>What a trace records.</summary>
public enum TraceProfile
{
    /// <summary>CPU samples with call stacks and the runtime's default events (dotnet-trace's cpu-sampling).</summary>
    Cpu = 0,

    /// <summary>Garbage collections, their pauses and allocation ticks (sampled allocations).</summary>
    Gc = 1,

    /// <summary>
    /// Sampled allocations with call stacks: allocation ticks, one about every 100 KB allocated, plus the
    /// loader and JIT events and the rundown that name the methods on the stacks.
    /// </summary>
    Allocations = 2,
}

/// <summary>A trace that was written.</summary>
/// <param name="StoppedEarly">True when it ended before the duration: canceled, or the process exited.</param>
/// <param name="Problem">Why a live recording ended before it was asked to, when that was an error: what was recorded is kept.</param>
public sealed record TraceRecording(string Path, long Bytes, TimeSpan Duration, bool StoppedEarly, string? Problem = null);

/// <summary>
/// Records an EventPipe trace of a .NET process through its diagnostics port: the same session dotnet-trace
/// starts, so nobody installs dotnet-trace. The file is a <c>.nettrace</c> that PerfView, Visual Studio or TraceEvent
/// read. It is written to a partial file of its own (<see cref="PartialName"/>) and renamed when complete; an existing file
/// is never overwritten. A live recording (<see cref="RecordLiveAsync"/>) is written under its final name, for a reader beside it.
/// Canceling stops the recording early and keeps what was recorded; a failure deletes the partial file.
/// </summary>
public static class TraceRecorder
{
    // dotnet-trace's defaults without the type keyword (0x80000): while a session has it, the runtime never describes a type again, so
    // every gcdump but the first had types without a name (see CHANGES.md of HeapScope.ThirdParty.GcDump).
    private const long RuntimeDefaultKeywords = 0x14C14FCCBD & ~TypeKeyword;
    private const long TypeKeyword = 0x80000;
    private const long GcKeyword = 0x1;
    private const long LoaderKeyword = 0x8;
    private const long JitKeyword = 0x10;
    private const long SampleProfilerKeywords = unchecked((long)0xF00000000000);

    /// <summary>The EventPipe providers of a profile.</summary>
    public static IReadOnlyList<EventPipeProvider> Providers(TraceProfile profile) => profile switch
    {
        TraceProfile.Gc =>
        [
            new("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, GcKeyword),
        ],
        TraceProfile.Allocations =>
        [
            // Allocation ticks are verbose GC events; EventPipe records a stack with each one. Loader and JIT events name
            // the modules and the methods compiled while recording; the rundown on stop names the rest.
            new("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, GcKeyword | LoaderKeyword | JitKeyword),
        ],
        _ =>
        [
            new("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational, SampleProfilerKeywords),
            new("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, RuntimeDefaultKeywords),
        ],
    };

    /// <exception cref="TargetUnavailableException">The process is gone or does not answer on its diagnostics port.</exception>
    /// <exception cref="ElevationRequiredException">The process belongs to another user or is elevated.</exception>
    /// <exception cref="DumpFileExistsException">The trace file exists already.</exception>
    public static async Task<TraceRecording> RecordAsync(int processId, string path, TraceProfile profile, TimeSpan duration, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            throw new DumpFileExistsException($"{fullPath} already exists; HeapScope never overwrites a file. Choose another file name.");
        }

        PrivateFiles.CreateUserDirectory(Path.GetDirectoryName(fullPath)!);
        // Created new under a name of its own before the session starts: a file made
        // beforehand by someone else is never written into or deleted, and a recording into the same folder never meets it.
        var partial = PartialName(fullPath);
        var stopwatch = Stopwatch.StartNew();
        // Unbuffered, as a live recording's: the session's small chunks went into a 64 KB buffer, so an idle target's trace
        // stayed 0 bytes on disk for minutes. That was the "start that took over 60 s" of CI and the gate since 2026-11-20: the session
        // had started; its bytes had not been written.
        var file = PrivateFiles.Open(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous);
        var stoppedEarly = false;
        try
        {
            await using (file.ConfigureAwait(false))
            {
                using var session = await StartAsync(processId, Providers(profile), cancellationToken).ConfigureAwait(false);
                stoppedEarly = await CopyAsync(session, file, duration, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            TryDelete(partial);
            throw;
        }

        File.Move(partial, fullPath, overwrite: false);
        return new TraceRecording(fullPath, new FileInfo(fullPath).Length, stopwatch.Elapsed, stoppedEarly);
    }

    /// <summary>
    /// Records the allocations profile for a reader beside it (ADR-037): into <paramref name="path"/> itself, each chunk
    /// of the stream written as it arrives, the file shared for reading and deleting. A System.Runtime counter every
    /// <paramref name="counterInterval"/> makes the runtime send a thread's last samples when it goes idle (the process has one timer
    /// for all who ask, and the shortest interval wins: it is that of the run's own counters). Before it, a session with the rundown alone names the methods
    /// compiled so far, into a partial file that the recording's end deletes; without it names come later. What was recorded before an
    /// error is kept.
    /// </summary>
    /// <param name="noNames">Why no names are to be recorded (a capture of the process runs), or null to record them.</param>
    /// <param name="started">Called once the session records, off the caller's thread.</param>
    /// <inheritdoc cref="RecordAsync" path="/exception"/>
    public static async Task<TraceRecording> RecordLiveAsync(int processId, string path, TimeSpan duration, TimeSpan counterInterval, string? noNames, Action<LiveTraceStart>? started, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            throw new DumpFileExistsException($"{fullPath} already exists; HeapScope never overwrites a file. Choose another file name.");
        }

        PrivateFiles.CreateUserDirectory(Path.GetDirectoryName(fullPath)!);
        var stopwatch = Stopwatch.StartNew();
        var names = PartialName(fullPath);
        var stoppedEarly = false;
        var created = false;
        var recording = false;
        string? failed = null;
        try
        {
            var problem = noNames ?? await RecordNamesAsync(processId, names, cancellationToken).ConfigureAwait(false);
            var start = new LiveTraceStart(fullPath, problem is null ? names : null, problem, stopwatch.Elapsed);
            stopwatch.Restart();
            var file = PrivateFiles.Open(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete, 1, FileOptions.Asynchronous);
            created = true;
            await using (file.ConfigureAwait(false))
            {
                using var session = await StartAsync(processId, [.. Providers(TraceProfile.Allocations), Counter(counterInterval)], cancellationToken).ConfigureAwait(false);
                recording = true;
                started?.Invoke(start);
                stoppedEarly = await CopyAsync(session, file, duration, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (recording && ex is IOException or UnauthorizedAccessException && new FileInfo(fullPath) is { Exists: true, Length: > 0 })
        {
            // The file states what it holds object by object: what was written is read as a recording cut short.
            (stoppedEarly, failed) = (true, ex.Message);
        }
        catch
        {
            if (created && !recording)
            {
                TryDelete(fullPath);
            }

            throw;
        }
        finally
        {
            TryDelete(names);
        }

        return new TraceRecording(fullPath, new FileInfo(fullPath).Length, stopwatch.Elapsed, stoppedEarly, failed);
    }

    private static EventPipeProvider Counter(TimeSpan interval) => new(
        "System.Runtime",
        EventLevel.Informational,
        0,
        new Dictionary<string, string> { ["EventCounterIntervalSec"] = Math.Max(1, (int)interval.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture) });

    /// <summary>How long the session of the names may take: a process that does not answer must not hold the recording up.</summary>
    private static readonly TimeSpan s_namesTimeout = TimeSpan.FromSeconds(15);

    /// <summary>A session that is stopped at once, for its rundown: why it wrote none, or null.</summary>
    private static async Task<string?> RecordNamesAsync(int processId, string path, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(s_namesTimeout);
        try
        {
            var file = PrivateFiles.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous);
            await using (file.ConfigureAwait(false))
            {
                using var session = await new DiagnosticsClient(processId)
                    .StartEventPipeSessionAsync([new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, LoaderKeyword)], requestRundown: true, circularBufferMB: 256, limit.Token)
                    .ConfigureAwait(false);
                var copy = session.EventStream.CopyToAsync(file, CancellationToken.None);

                // One that is not waited for (the limit passed) ends with the session: its error is nobody's.
                _ = copy.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                await SessionStop.StopAsync(session, s_namesTimeout).ConfigureAwait(false);
                await copy.WaitAsync(limit.Token).ConfigureAwait(false);
            }

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"not done in {s_namesTimeout.TotalSeconds:N0} s");
        }
        catch (Exception ex) when (ex is ServerNotAvailableException or ServerErrorException or UnauthorizedAccessException or IOException or ObjectDisposedException)
        {
            // The recording itself says what is wrong, if it fails too; a names session let go ends its copy this way.
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private static async Task<EventPipeSession> StartAsync(int processId, IReadOnlyList<EventPipeProvider> providers, CancellationToken cancellationToken)
    {
        // Bounded: a process that does not answer the start (frozen) ends the recording with a reason, not a wait.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(s_startWait);
        try
        {
            return await new DiagnosticsClient(processId)
                .StartEventPipeSessionAsync(providers, requestRundown: true, circularBufferMB: 256, limit.Token)
                .WaitAsync(limit.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TargetUnavailableException(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Process {processId} did not answer the start of a recording within {s_startWait.TotalSeconds:N0} s. If it is stopped in a debugger, let it run, then record again."));
        }
        catch (Exception ex) when (ex is ServerNotAvailableException or ServerErrorException or UnauthorizedAccessException)
        {
            throw Translate(processId, ex);
        }
    }

    /// <summary>Copies the session's stream until its end, the duration's or the cancel; whether it stopped early.</summary>
    private static async Task<bool> CopyAsync(EventPipeSession session, Stream file, TimeSpan duration, CancellationToken cancellationToken)
    {
        // The stream ends when the session stops (with the rundown that names methods) or the process exits.
        var copy = session.EventStream.CopyToAsync(file, CancellationToken.None);
        var elapsed = Task.Delay(duration, cancellationToken);
        var first = await Task.WhenAny(copy, elapsed).ConfigureAwait(false);
        var stoppedEarly = first == copy || cancellationToken.IsCancellationRequested;
        // A target that does not answer the stop (frozen in a debugger) has its session let go, which ends the copy.
        // The stop writes the rundown that names methods, which takes seconds in a large process: the trace waits longer than a counter.
        var letGo = first != copy && !await SessionStop.StopAsync(session, s_stopWait).ConfigureAwait(false);
        stoppedEarly |= letGo;

        // A stopped session ends its stream after the rundown; one that never ends (the target hung) must not
        // hang the caller: give up after a while and keep what arrived.
        if (await Task.WhenAny(copy, Task.Delay(s_streamEndTimeout, CancellationToken.None)).ConfigureAwait(false) == copy)
        {
            try
            {
                await copy.ConfigureAwait(false);
            }
            catch (Exception ex) when (letGo && ex is ObjectDisposedException or IOException or OperationCanceledException)
            {
                // The session let go ended the copy; the trace so far is in the file.
            }
        }
        else
        {
            stoppedEarly = true;
            session.Dispose();
            try
            {
                await copy.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or IOException or OperationCanceledException)
            {
                // Disposing the session ended the copy; the trace so far is in the file.
            }
        }

        return stoppedEarly;
    }

    /// <summary>
    /// The partial file a recording into <paramref name="fullPath"/> writes: the final name, a random part (so no file made
    /// beforehand is ever written into) and <c>.partial</c>. Anything that looks for a recording in progress (the size
    /// watch, the sweep, tests) finds it with <see cref="PartialPattern"/>.
    /// </summary>
    public static string PartialName(string fullPath) => $"{fullPath}.{Guid.NewGuid():N}.partial";

    /// <summary>The search pattern, in <paramref name="fullPath"/>'s folder, of the partial files a recording into it writes.</summary>
    public static string PartialPattern(string fullPath) => System.IO.Path.GetFileName(fullPath) + ".*.partial";

    /// <summary>How long a stopped session may take to end its stream (the rundown) before the recording stops waiting.</summary>
    private static readonly TimeSpan s_streamEndTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How long a recording's start waits for the target.</summary>
    private static readonly TimeSpan s_startWait = TimeSpan.FromSeconds(30);

    /// <summary>How long a recording's stop waits for the target, its rundown included.</summary>
    private static readonly TimeSpan s_stopWait = TimeSpan.FromSeconds(30);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the original failure is what the caller needs to see.
        }
    }

    internal static Exception Translate(int processId, Exception ex)
    {
        string name;
        try
        {
            using var process = Process.GetProcessById(processId);
            name = process.ProcessName;
        }
        catch (Exception lookup) when (lookup is ArgumentException or InvalidOperationException)
        {
            return new TargetUnavailableException($"No running process has id {processId}.", ex);
        }

        // The same reasons as a capture's (another user's, diagnostics off, another /tmp).
        return ex is UnauthorizedAccessException
            ? ElevationRequiredException.ForTrace(processId, name, ex)
            : CapturePlatform.Rules().NotAnswering(processId, $"{name} ({processId}) did not answer on its diagnostics port: {ex.Message} Is it a .NET 6 or later process?", ex);
    }
}
