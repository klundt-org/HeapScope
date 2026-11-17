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

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using HeapScope.Application.Workers;
using HeapScope.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeapScope.Application.Tutorial;

/// <summary>
/// The tutorial's sample (sprint 3.13, ADR-042): reads the shipped sample's scenario catalog, starts a sample with the chosen
/// scenarios as the user, marks its process as a sample and stops it. Refused while this process runs elevated (the sample would
/// be elevated too) and above <see cref="MaxRunning"/> at once in this process; every sample exits with the process that
/// started it (it watches this process's pid), and the ones still running are stopped when this service is disposed.
/// Each running sample has a record in the data folder (<c>samples/&lt;pid&gt;.json</c>, with its start time), so
/// <see cref="StopRecorded"/> can stop one that another HeapScope process started, on any system (review H4).
/// A sample that cannot be started or read is refused with a next step, never an unexpected failure (review H2), and every
/// start, refusal, stop and exit is logged with what support needs to back it up (review H5).
/// </summary>
public sealed class SampleService : IDisposable
{
    /// <summary>The most samples that run at once in one HeapScope process.</summary>
    public const int MaxRunning = 3;

    /// <summary>The interval of a growing sample's step.</summary>
    public static readonly TimeSpan GrowEvery = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions s_json = new() { PropertyNameCaseInsensitive = true };

    private readonly ISampleProcessStarter _starter;
    private readonly Func<bool> _isElevated;
    private readonly Func<SampleLaunch?> _resolve;
    private readonly ILogger _logger;
    private readonly HeapScopeFolders _folders;
    private readonly TimeSpan _readyTimeout;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private readonly List<Entry> _running = [];
    private int _pending;
    private SampleCatalog? _catalog;

    /// <param name="folders">The data folder for the samples' records; null for <see cref="HeapScopeFolders.Default"/>.</param>
    /// <param name="readyTimeout">How long a sample may take to list its catalog or plant its scenarios (default 60 s).</param>
    public SampleService(
        ISampleProcessStarter starter,
        Func<bool> isElevated,
        Func<SampleLaunch?>? resolve = null,
        ILogger<SampleService>? logger = null,
        HeapScopeFolders? folders = null,
        TimeSpan? readyTimeout = null)
    {
        _starter = starter ?? throw new ArgumentNullException(nameof(starter));
        _isElevated = isElevated ?? throw new ArgumentNullException(nameof(isElevated));
        _resolve = resolve ?? (() => SampleLaunch.Resolve(AppContext.BaseDirectory, WorkerHostOptions.RunningDotnetHost(), File.Exists));
        _logger = logger ?? NullLogger<SampleService>.Instance;
        _folders = folders ?? HeapScopeFolders.Default;
        _readyTimeout = readyTimeout ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>Raised when the samples running change (one started, stopped or exited), on any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// What to do when the sample is missing or broken: update the command line's dotnet tool, or reinstall the release
    /// (review M8: one sentence was wrong for the tool).
    /// </summary>
    public static string RepairStep => WorkerHostOptions.RunningDotnetHost() is not null
        ? "Update HeapScope's command line: dotnet tool update -g HeapScope.Cli"
        : "Reinstall HeapScope from its release archive";

    /// <summary>Why no sample can start here, or null: it is not in this installation, or this process runs elevated.</summary>
    public string? UnavailableReason => _resolve() is null
        ? $"The sample is not in this installation. {RepairStep}."
        : _isElevated()
            ? "HeapScope runs as administrator, so the sample would too. Start HeapScope without administrator rights to run it."
            : null;

    /// <summary>The version of the catalog read, or null before it is read.</summary>
    public int? CatalogVersion => _catalog?.Version;

    /// <summary>The samples running now.</summary>
    public IReadOnlyList<RunningSample> Running
    {
        get
        {
            lock (_gate)
            {
                return [.. _running.Select(r => r.Sample)];
            }
        }
    }

    /// <summary>Whether <paramref name="processId"/> is a sample this process started and that still runs.</summary>
    public bool IsSample(int processId) => Running.Any(s => s.ProcessId == processId);

    /// <summary>The sample's scenarios, read from the sample itself; a read that fails is not kept, so the next one tries again.</summary>
    /// <exception cref="SampleRefusedException">The sample is not here, could not run or could not list its scenarios.</exception>
    public async Task<IReadOnlyList<SampleScenario>> ListScenariosAsync(CancellationToken cancellationToken)
    {
        if (_catalog is { } known)
        {
            return known.Scenarios;
        }

        await _catalogGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_catalog is { } read)
            {
                return read.Scenarios;
            }

            if (UnavailableReason is { } unavailable)
            {
                throw Refused(unavailable, LogLevel.Information);
            }

            var launch = _resolve()!;
            _logger.LogInformation("Reading the sample's catalog from {File} ({Form})", launch.SampleFile, launch.Prefix.Count == 0 ? "executable" : "dotnet host " + launch.FileName);
            using var process = StartProcess(launch, [.. launch.Prefix, "--list"]);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_readyTimeout);
            await using var killOnTimeout = timeout.Token.Register(process.Kill).ConfigureAwait(false);
            string? json = null;
            try
            {
                json = await process.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Reported below: the timeout, whether the read was canceled or ended when the timeout killed the sample.
            }
            catch (IOException ex)
            {
                throw Refused($"The sample's output could not be read ({ex.Message}). Try again; if it fails again, HeapScope's log has the details.", LogLevel.Warning);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (timeout.IsCancellationRequested)
            {
                throw Refused($"The sample did not list its scenarios within {_readyTimeout.TotalSeconds:N0} seconds and was stopped. The computer may be busy; try again.", LogLevel.Warning);
            }

            SampleCatalog? catalog = null;
            try
            {
                catalog = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<SampleCatalog>(json, s_json);
            }
            catch (JsonException)
            {
                // Refused below with what the sample said.
            }

            if (catalog is not { Scenarios.Count: > 0 })
            {
                var (code, error) = await ExitOfAsync(process).ConfigureAwait(false);
                _logger.LogWarning("The sample at {File} listed no scenarios: exit code {Code}, its message: {Error}", launch.SampleFile, code, error);
                throw new SampleRefusedException($"The sample could not list its scenarios{(error.Length > 0 ? ": " + error : string.Empty)}. {RepairStep}.");
            }

            _catalog = catalog;
            _logger.LogInformation(
                "The sample at {File} lists {Count} scenarios (catalog version {Version}): {Names}",
                launch.SampleFile,
                catalog.Scenarios.Count,
                catalog.Version,
                string.Join(", ", catalog.Scenarios.Select(s => $"{s.Name} plants {s.AnswerType}")));
            return catalog.Scenarios;
        }
        finally
        {
            _catalogGate.Release();
        }
    }

    /// <summary>
    /// Starts a sample with <paramref name="scenarios"/>, growing when <paramref name="grow"/>, and returns once it is ready.
    /// </summary>
    /// <exception cref="SampleRefusedException">Why it cannot start, with what to do.</exception>
    public async Task<RunningSample> StartAsync(IReadOnlyList<string> scenarios, bool grow, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scenarios);
        var asked = string.Join(", ", scenarios);
        if (UnavailableReason is { } unavailable)
        {
            throw Refused(unavailable, LogLevel.Information, asked);
        }

        if (scenarios.Count == 0)
        {
            throw Refused("Choose at least one scenario.", LogLevel.Information, asked);
        }

        var catalog = await ListScenariosAsync(cancellationToken).ConfigureAwait(false);
        var chosen = new List<SampleScenario>();
        foreach (var name in scenarios)
        {
            var scenario = catalog.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw Refused($"There is no scenario {name} in this HeapScope's sample. The Processes page and 'heapscope-cli sample list' list them.", LogLevel.Information, asked);
            chosen.Add(scenario);
        }

        foreach (var scenario in chosen)
        {
            if (chosen.FirstOrDefault(other => scenario.CannotRunWith.Contains(other.Name, StringComparer.OrdinalIgnoreCase)) is { } other)
            {
                throw Refused($"{scenario.Name} and {other.Name} cannot run together: {scenario.ConflictReason ?? "they conflict"}. Choose one.", LogLevel.Information, asked);
            }
        }

        // Review M10: a slot is taken before the start and given back when it fails, so two starts at once cannot pass the limit.
        lock (_gate)
        {
            if (_running.Count + _pending >= MaxRunning)
            {
                var pids = string.Join(", ", _running.Select(r => r.Sample.ProcessId));
                _logger.LogInformation("Sample refused for {Scenarios}: {Max} running or starting (pids {Pids})", asked, MaxRunning, pids);
                throw new SampleRefusedException($"Three samples are running, the most at once. Stop one first: on the Processes page (rows marked Sample) or with Stop lesson.");
            }

            _pending++;
        }

        try
        {
            return await StartReservedAsync(chosen, grow, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _pending--;
            }
        }
    }

    /// <summary>
    /// Stops the sample <paramref name="processId"/> this process started; false when it is not one (or exited already).
    /// <paramref name="reason"/> says who stopped it, for the log: the Processes page, a lesson, the command line, shutdown.
    /// </summary>
    public bool Stop(int processId, string reason)
    {
        Entry? entry;
        lock (_gate)
        {
            entry = _running.FirstOrDefault(r => r.Sample.ProcessId == processId);
            if (entry is null)
            {
                _logger.LogInformation("Sample {Pid} not stopped ({Reason}): it is not running", processId, reason);
                return false;
            }

            entry.StopReason = reason;
            _running.Remove(entry);
        }

        DeleteRecord(processId);
        try
        {
            entry.Process.Kill();
        }
        catch (Win32Exception ex)
        {
            _logger.LogWarning("Sample {Pid} could not be stopped ({Reason}): {Error}", processId, reason, ex.Message);
            return false;
        }

        _logger.LogInformation("Sample {Pid} stopped ({Reason}): {Scenarios}, after {Seconds:N0} s", processId, reason, string.Join(", ", entry.Sample.Scenarios), (DateTimeOffset.UtcNow - entry.Sample.StartedUtc).TotalSeconds);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Stops the sample <paramref name="processId"/> whichever HeapScope process started it (<c>heapscope-cli sample stop</c>):
    /// only a process with a sample's record whose start time matches, so a reused pid is never stopped.
    /// </summary>
    /// <exception cref="SampleRefusedException">It could not be stopped (another user's, access denied), with what to do.</exception>
    public SampleStopOutcome StopRecorded(int processId)
    {
        var path = RecordPath(processId);
        SampleRecord? record = null;
        try
        {
            record = File.Exists(path) ? JsonSerializer.Deserialize<SampleRecord>(File.ReadAllText(path), s_json) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning("The sample record {Path} could not be read: {Error}", path, ex.Message);
        }

        var started = _starter.StartTimeUtcOf(processId);
        if (record is null || started is null || Math.Abs((started.Value - record.StartTimeUtc).TotalSeconds) > 2)
        {
            _logger.LogInformation(
                "Sample stop refused for {Pid}: {Reason}",
                processId,
                record is null ? "no sample record" : started is null ? "its sample has ended" : $"pid reused (started {started:O}, the sample {record.StartTimeUtc:O})");
            if (record is not null)
            {
                DeleteRecord(processId);
            }

            return SampleStopOutcome.NotASample;
        }

        // The record goes first, so its owner reads the exit as a stop from elsewhere; it is written back if the kill fails.
        DeleteRecord(processId);
        try
        {
            _starter.Kill(processId);
        }
        catch (Win32Exception ex)
        {
            WriteRecord(record);
            _logger.LogWarning("Sample {Pid} could not be stopped: {Error}", processId, ex.Message);
            throw new SampleRefusedException($"Sample {processId} could not be stopped: {ex.Message}. Stop it from the HeapScope that started it, or from Task Manager.", ex);
        }

        _logger.LogInformation("Sample {Pid} stopped from another HeapScope process (started by pid {Owner} at {Started:O}): {Scenarios}", processId, record.OwnerProcessId, record.StartTimeUtc, string.Join(", ", record.Scenarios));
        return SampleStopOutcome.Stopped;
    }

    /// <summary>How the sample <paramref name="processId"/> ends; null when it is not one this process runs.</summary>
    public Task<SampleExit>? ExitAsync(int processId)
    {
        lock (_gate)
        {
            return _running.FirstOrDefault(r => r.Sample.ProcessId == processId)?.Exit.Task;
        }
    }

    public void Dispose()
    {
        foreach (var sample in Running)
        {
            Stop(sample.ProcessId, "HeapScope is closing");
        }

        _catalogGate.Dispose();
    }

    private async Task<RunningSample> StartReservedAsync(List<SampleScenario> chosen, bool grow, CancellationToken cancellationToken)
    {
        var launch = _resolve()!;
        var names = chosen.Select(s => s.Name).ToList();
        List<string> arguments = [.. launch.Prefix, "--scenarios", string.Join(',', names), "--parent-pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture)];
        if (grow)
        {
            arguments.AddRange(["--grow-every", ((int)GrowEvery.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)]);
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var process = StartProcess(launch, arguments);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_readyTimeout);

            // Review M11: the timeout kills the sample, which ends the read whether or not the pipe honors cancellation.
            await using var killOnTimeout = timeout.Token.Register(process.Kill).ConfigureAwait(false);
            while (await process.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line)
            {
                if (line.StartsWith("READY pid=", StringComparison.Ordinal))
                {
                    return Track(process, names, grow, launch, arguments, System.Diagnostics.Stopwatch.GetElapsedTime(started));
                }
            }

            // The read ended: the sample exited, or the timeout (or the caller) killed it and the pipe ended the read.
            cancellationToken.ThrowIfCancellationRequested();
            if (timeout.IsCancellationRequested)
            {
                throw new OperationCanceledException(timeout.Token);
            }

            var (code, error) = await ExitOfAsync(process).ConfigureAwait(false);
            _logger.LogWarning("Sample for {Scenarios} exited before it was ready: exit code {Code}, its message: {Error}", string.Join(", ", names), code, error);
            throw new SampleRefusedException(error.Length > 0
                ? $"The sample did not start: {error}"
                : $"The sample did not start: it exited with code {code?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} and no message. Try again; if it fails again, HeapScope's log has the details.");
        }
        catch (IOException ex)
        {
            process.Kill();
            process.Dispose();
            throw Refused($"The sample's output could not be read ({ex.Message}). Try again; if it fails again, HeapScope's log has the details.", LogLevel.Warning);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill();
            process.Dispose();
            _logger.LogWarning("Sample for {Scenarios} was not ready within {Seconds:N0} s and was stopped", string.Join(", ", names), _readyTimeout.TotalSeconds);
            throw new SampleRefusedException($"The sample did not get ready within {_readyTimeout.TotalSeconds:N0} seconds and was stopped. The computer may be busy; try again.");
        }
        catch
        {
            process.Kill();
            process.Dispose();
            throw;
        }
    }

    private RunningSample Track(ISampleProcess process, List<string> names, bool grow, SampleLaunch launch, List<string> arguments, TimeSpan toReady)
    {
        var sample = new RunningSample(process.Id, names, grow, DateTimeOffset.UtcNow);
        var entry = new Entry(sample, process);
        lock (_gate)
        {
            _running.Add(entry);
        }

        WriteRecord(new SampleRecord(process.Id, process.StartTimeUtc, names, Environment.ProcessId));
        process.Exited += (_, _) => OnExited(entry);
        if (process.HasExited)
        {
            OnExited(entry);
        }

        _logger.LogInformation(
            "Sample {Pid} started: {Scenarios}, growing {Grows} (every {Every} ms), ready in {Seconds:N1} s; {File} {Arguments}",
            process.Id,
            string.Join(", ", names),
            grow,
            grow ? (int)GrowEvery.TotalMilliseconds : 0,
            toReady.TotalSeconds,
            launch.FileName,
            string.Join(' ', arguments));
        Changed?.Invoke(this, EventArgs.Empty);
        return sample;
    }

    // Review H3 and L17: an exit is reported when it happens, the process released, and the reason told apart.
    private void OnExited(Entry entry)
    {
        if (!entry.TryReport())
        {
            return;
        }

        bool wasRunning;
        lock (_gate)
        {
            wasRunning = _running.Remove(entry);
        }

        var reason = entry.StopReason is not null
            ? SampleExitReason.Stopped
            : File.Exists(RecordPath(entry.Sample.ProcessId)) ? SampleExitReason.OnItsOwn : SampleExitReason.StoppedElsewhere;
        DeleteRecord(entry.Sample.ProcessId);
        if (wasRunning)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        _ = ReportExitAsync(entry, reason);
    }

    // The exit event can come before the last lines of its standard error are read (a crash's message is those lines), so
    // the report waits for them, briefly (review H5).
    private async Task ReportExitAsync(Entry entry, SampleExitReason reason)
    {
        try
        {
            await entry.Process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
        {
            // Reported with what was read.
        }

        var exit = new SampleExit(entry.Sample.ProcessId, reason, entry.Process.ExitCode, entry.Process.Error);
        if (reason != SampleExitReason.Stopped)
        {
            _logger.LogInformation(
                "Sample {Pid} exited ({Reason}): {Scenarios}, exit code {Code}, after {Seconds:N0} s; its last message: {Error}",
                exit.ProcessId,
                reason,
                string.Join(", ", entry.Sample.Scenarios),
                exit.ExitCode,
                (DateTimeOffset.UtcNow - entry.Sample.StartedUtc).TotalSeconds,
                exit.Error.Length > 2048 ? exit.Error[^2048..] : exit.Error);
        }

        entry.Process.Dispose();
        entry.Exit.TrySetResult(exit);
    }

    // Review H2: a sample that cannot be started (blocked, missing, not executable) is a refusal with a next step.
    private ISampleProcess StartProcess(SampleLaunch launch, IReadOnlyList<string> arguments)
    {
        try
        {
            return _starter.Start(launch.FileName, arguments);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The sample at {File} could not be started", launch.SampleFile);
            throw new SampleRefusedException($"The sample at {launch.SampleFile} could not be started ({ex.Message}). If an antivirus or a company policy blocks it, allow it; otherwise: {RepairStep}.", ex);
        }
    }

    // Review M11: its standard error is read to the end before it is reported.
    private static async Task<(int? Code, string Error)> ExitOfAsync(ISampleProcess process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            process.Kill();
        }

        return (process.ExitCode, process.Error);
    }

    private SampleRefusedException Refused(string reason, LogLevel level, string? scenarios = null)
    {
        _logger.Log(level, "Sample refused{For}: {Reason}", scenarios is null ? string.Empty : " for " + scenarios, reason);
        return new SampleRefusedException(reason);
    }

    private string RecordPath(int processId) =>
        Path.Combine(_folders.Root, "samples", processId.ToString(CultureInfo.InvariantCulture) + ".json");

    private void WriteRecord(SampleRecord record)
    {
        try
        {
            var path = RecordPath(record.ProcessId);
            PrivateFiles.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = PrivateFiles.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(stream, record);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without its record only 'sample stop' from another process cannot find it; this process still stops it.
            _logger.LogWarning("The record of sample {Pid} could not be written: {Error}", record.ProcessId, ex.Message);
        }
    }

    private void DeleteRecord(int processId)
    {
        try
        {
            File.Delete(RecordPath(processId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogInformation("The record of sample {Pid} could not be deleted: {Error}", processId, ex.Message);
        }
    }

    private sealed class Entry(RunningSample sample, ISampleProcess process)
    {
        public RunningSample Sample { get; } = sample;

        public ISampleProcess Process { get; } = process;

        public TaskCompletionSource<SampleExit> Exit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? StopReason { get; set; }

        private int _reported;

        /// <summary>True the first time only: an exit is reported once, from the event or from the check after subscribing.</summary>
        public bool TryReport() => Interlocked.Exchange(ref _reported, 1) == 0;
    }

    /// <summary>A running sample's record in the data folder: its pid with its start time, so a reused pid is never stopped.</summary>
    private sealed record SampleRecord(int ProcessId, DateTime StartTimeUtc, IReadOnlyList<string> Scenarios, int OwnerProcessId);
}
