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

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeapScope.Application.Allocations;
using HeapScope.Application.Monitoring;
using HeapScope.Application.Presentation;
using HeapScope.Application.Sessions;
using HeapScope.Application.Snapshots;
using HeapScope.Contracts;
using HeapScope.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// The Live page (sprint 1.7, ADR-021): monitor a process's GC heap per heap kind every interval, take snapshots by
/// rules, see the run as a stacked chart with snapshot markers and cards, and a badge when GC pressure is high. Samples
/// arrive on a background thread and are marshaled here. Since sprint 4.3 this is monitoring alone: what the page shows is in
/// <see cref="Shown"/>, the run's files in <see cref="Files"/> and the past runs in <see cref="PastRuns"/>.
/// </summary>
public sealed partial class LiveViewModel : ObservableObject, IAsyncDisposable
{
    private readonly MonitorService _monitor;
    private readonly ICaptureService _capture;
    private readonly IUiDispatcher _dispatcher;
    private readonly IUserInteraction? _interaction;
    private readonly ILogger _logger;
    private MonitorRun? _run;

    /// <summary>Records an allocation trace for the whole run (sprint 3.4).</summary>
    [ObservableProperty]
    private bool _recordAllocations;

    /// <summary>A Get snapshot or Force GC in progress.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GetSnapshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForceGcCommand))]
    private bool _acting;

    [ObservableProperty]
    private ProcessRow? _selectedProcess;

    [ObservableProperty]
    private int _intervalSeconds = 1;

    [ObservableProperty]
    private string? _every;

    [ObservableProperty]
    private string? _above;

    [ObservableProperty]
    private string? _growth;

    [ObservableProperty]
    private string _rearm = "below";

    [ObservableProperty]
    private string? _maxSnapshots;

    /// <summary>Rules capture gcdumps instead of full dumps (sprint 1.8). Each forces a full GC, which the chart shows.</summary>
    [ObservableProperty]
    private bool _captureGcDumps;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(GetSnapshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForceGcCommand))]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private bool _isRunning;

    [ObservableProperty]
    private string? _latest;

    [ObservableProperty]
    private bool _highGcPressure;

    private const string WaitingText = "Waiting for the first sample.";

    private static readonly TimeSpan s_noSampleWait = TimeSpan.FromSeconds(10);

    private bool _warnedNoSamples;

    public LiveViewModel(MonitorService monitor, ICaptureService capture, IUiDispatcher dispatcher, ILogger<LiveViewModel>? logger = null, IUserInteraction? interaction = null, AllocationsViewModel? allocations = null, AllocationSession? live = null)
    {
        _interaction = interaction;
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? NullLogger<LiveViewModel>.Instance;
        Files = new LiveRunFilesViewModel(Shown, interaction, allocations, _logger);
        PastRuns = new LivePastRunsViewModel(capture, Shown, Files, _logger);
        Allocations = new LiveAllocationsViewModel(live, _logger);
        Shown.PropertyChanged += (_, e) =>
        {
            // A past run's chart has no live allocations beside it: Open allocations opens its trace.
            if (e.PropertyName == nameof(LiveRunState.PastRunShown) && Shown.PastRunShown is not null)
            {
                Allocations.Close();
            }
        };
    }

    /// <summary>What the page shows of the run being watched, or of a past one.</summary>
    public LiveRunState Shown { get; } = new();

    /// <summary>The shown run's timeline, trace and snapshots.</summary>
    public LiveRunFilesViewModel Files { get; }

    /// <summary>The current session's past runs.</summary>
    public LivePastRunsViewModel PastRuns { get; }

    /// <summary>What the process being watched allocates, when the run records it.</summary>
    public LiveAllocationsViewModel Allocations { get; }

    public ObservableCollection<ProcessRow> Processes { get; } = [];

    // Sprint 4.1.1: the same rule as the Processes page for the processes HeapScope cannot watch as it runs now.
    private readonly ProcessListFilter _filter = new();

    /// <summary>The check box: list the processes hidden because they need more rights (off at each start).</summary>
    [ObservableProperty]
    private bool _showHiddenProcesses;

    /// <summary>Whether a process needs more rights than HeapScope has, so the check box is offered.</summary>
    [ObservableProperty]
    private bool _canShowHiddenProcesses;

    /// <summary>How many processes are hidden and why, while they are; null otherwise.</summary>
    [ObservableProperty]
    private string? _hiddenNote;

    public IReadOnlyList<string> RearmModes { get; } = ["below", "once", "step"];

    /// <summary>What the empty chart says: once Start is pressed, "choose a process" is wrong.</summary>
    public string EmptyText => IsRunning ? WaitingText : PlatformHints.LiveEmpty;

    /// <summary>How a run waits before asking whether any sample came: tests hold it to decide when that happens.</summary>
    public Func<TimeSpan, Task> NoSampleDelay { get; set; } = static wait => Task.Delay(wait);

    /// <summary>The run in progress, if any (tests and smoke runs await its completion).</summary>
    public MonitorRun? Run => _run;

    /// <summary>Whether runs write their timeline into the session folder.</summary>
    public bool WriteTimeline { get; set; } = true;

    /// <summary>
    /// Whether runs use the session library at all: their record, timeline and trace (off for smoke runs, which leave
    /// nothing behind in the user's sessions).
    /// </summary>
    public bool RecordRuns { get; set; } = true;

    /// <summary>Why monitoring a process is not available on this system (sprint 2.3.6), or null.</summary>
    public string? Unavailable => _capture.UnavailableReason;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Unavailable is { } reason)
        {
            Shown.Status = reason;
            return;
        }

        try
        {
            var processes = await _capture.ListProcessesAsync(CancellationToken.None);
            _filter.Replace(processes.Select(ProcessRow.From));
            ShowRows();
            _filter.Log(_logger, "Live", toggled: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            // What the user was shown, for the log (sprint 4.8, M13).
            _logger.LogInformation("{Section} shown as: {Message}", "Live process list", ex.Message);
            Shown.Status = "The process list could not be read: " + ex.Message;
        }
    }

    partial void OnShowHiddenProcessesChanged(bool value)
    {
        _filter.ShowHidden = value;
        ShowRows();
        _filter.Log(_logger, "Live", toggled: true);
    }

    /// <summary>Shows the rows the filter lets through and keeps the choice while its row is shown (sprint 4.1.1).</summary>
    private void ShowRows()
    {
        var selected = SelectedProcess?.ProcessId;
        Processes.Clear();
        foreach (var row in _filter.Visible)
        {
            Processes.Add(row);
        }

        SelectedProcess = Processes.FirstOrDefault(p => p.ProcessId == selected);
        CanShowHiddenProcesses = _filter.AnyNeedMoreRights;
        HiddenNote = _filter.Hidden.Count > 0 ? HeapScope.Application.Sessions.RightsTexts.HiddenNote(_filter.Hidden.Count) : null;
    }

    private bool CanStart() => !IsRunning;

    /// <summary>
    /// Starts monitoring. The run's start reads and records its session on disk under the session's lock, which can wait,
    /// so it runs off the UI thread (review H3 of 3.4).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        // Whatever this press leads to, the last run's warning is over.
        Shown.RunWarning = null;
        _warnedNoSamples = false;
        if (SelectedProcess is not { } process)
        {
            Shown.Status = "Pick a process to monitor.";
            return;
        }

        if (ParseRules() is not { } rules)
        {
            return;
        }

        Shown.Clear();
        Files.Show(timeline: null, trace: null);
        Allocations.Close();
        HighGcPressure = false;
        Latest = null;
        var options = new MonitorOptions(
            TimeSpan.FromSeconds(Math.Max(1, IntervalSeconds)),
            rules,
            WriteTimeline,
            CaptureGcDumps ? SnapshotKindDto.GcDump : SnapshotKindDto.FullDump,
            RecordAllocations,
            process.Name,
            RecordRun: RecordRuns);
        IsRunning = true;
        _stopAsked = false;
        Shown.Status = string.Create(CultureInfo.CurrentCulture, $"Starting to monitor {process.Name} (pid {process.ProcessId})...");
        var replaced = _run;
        _run = null;
        MonitorRun run;
        try
        {
            run = await Task.Run(async () =>
            {
                if (replaced is not null)
                {
                    await DisposeReplacedAsync(replaced);
                }

                return _monitor.Start(process.ProcessId, options, attached =>
                {
                    // Before the first sample can arrive: every event of the run finds it current.
                    _run = attached;
                    attached.SampleReceived += (_, sample) => _dispatcher.Post(() => OnSample(attached, sample));
                    attached.Captured += (_, capture) => _dispatcher.Post(() => OnCaptured(attached, capture));
                    attached.GcForced += (_, forced) => _dispatcher.Post(() => OnGcForced(attached, forced));
                    attached.TraceStopped += (_, message) => _dispatcher.Post(() => OnTraceStopped(attached, message));
                    attached.TraceStarted += (_, trace) => _dispatcher.Post(() => OnTraceStarted(attached, trace));
                    attached.TraceEnded += (_, _) => _dispatcher.Post(() => OnTraceEnded(attached));
                });
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Monitoring pid {Pid} could not start", process.ProcessId);
            Shown.Status = null;
            Shown.RunWarning = "Monitoring could not start: " + ex.Message;
            IsRunning = false;
            return;
        }

        if (_stopAsked)
        {
            run.Stop();
        }

        if (ReferenceEquals(run, _run) && IsRunning && run.TraceError is null)
        {
            Shown.Status = string.Create(CultureInfo.CurrentCulture, $"Monitoring {process.Name} (pid {process.ProcessId}){(rules.Rules.Count > 0 ? "; rules: " + string.Join(", ", rules.Rules.Select(r => r.Label)) : string.Empty)}.");
        }

        _ = WatchAsync(run);
        _ = WarnIfNoSamplesAsync(run, process.Name, options.Interval);
    }

    private async Task WarnIfNoSamplesAsync(MonitorRun run, string processName, TimeSpan interval)
    {
        // A process can turn off the events the counters come from, and then nothing ever arrives. Three intervals, for a long interval.
        var wait = TimeSpan.FromTicks(Math.Max(s_noSampleWait.Ticks, 3 * interval.Ticks));
        await NoSampleDelay(wait).ConfigureAwait(false);
        _dispatcher.Post(() =>
        {
            if (ReferenceEquals(run, _run) && IsRunning && Shown.Samples.Count == 0)
            {
                _logger.LogWarning("Monitoring run {RunId} (pid {Pid}): no samples after {Seconds:N0} s", run.Id, run.ProcessId, wait.TotalSeconds);
                _warnedNoSamples = true;
                Shown.RunWarning = string.Create(CultureInfo.CurrentCulture, $"No samples from {processName} after {wait.TotalSeconds:N0} s, so there is nothing to chart yet. A process can turn off the runtime events Live reads (EventSource support), as some trimmed apps do. Get snapshot still works.");
            }
        });
    }

    /// <summary>What a forced collection does to the target, asked before each one (review of 3.4; the ask-first rule).</summary>
    public const string ForceGcQuestion =
        "Force a full, blocking garbage collection in {0}? Every managed thread in it pauses while the collection runs, for "
        + "seconds on a large heap. Finalizers do not run, so objects waiting for finalization, and what they hold, stay "
        + "until a later collection. An app inside a no-GC region leaves it, and its EndNoGCRegion call then fails. Objects "
        + "the collection moves count as dead and new in a comparison by address.";

    private bool CanAct() => IsRunning && !Acting;

    /// <summary>Takes a snapshot of the monitored process now (sprint 3.4), as the run's card.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task GetSnapshotAsync()
    {
        if (_run is not { } run)
        {
            return;
        }

        Acting = true;
        try
        {
            Shown.Status = "Taking a snapshot...";
            var taken = await run.CaptureNowAsync();
            Shown.Status = taken.Result is not null ? "Snapshot taken." : "The snapshot failed: " + taken.Error;
        }
        finally
        {
            Acting = false;
        }
    }

    /// <summary>Forces a full garbage collection in the monitored process (sprint 3.4), marked on the chart.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ForceGcAsync()
    {
        if (_run is not { } run)
        {
            return;
        }

        var target = run.Options.ProcessName ?? string.Create(CultureInfo.InvariantCulture, $"pid {run.ProcessId}");
        if (_interaction is not null)
        {
            var yes = await _interaction.ConfirmAsync("Force a garbage collection", string.Format(CultureInfo.CurrentCulture, ForceGcQuestion, target));
            _logger.LogInformation("Force GC of pid {Pid} (run {RunId}) asked; the user answered {Answer}", run.ProcessId, run.Id, yes ? "yes" : "no");
            if (!yes)
            {
                return;
            }
        }

        Acting = true;
        try
        {
            Shown.Status = "Forcing a garbage collection...";
            var forced = await run.ForceGcAsync(CancellationToken.None);
            Shown.Status = forced.Result is { Completed: true } ? "A full garbage collection ran in the process. Objects it moved count as dead and new in a comparison by address."
                : forced.Result is { } incomplete ? "The collection is not confirmed: " + incomplete.Outcome + ". It is not marked on the chart."
                : forced.Error;
        }
        finally
        {
            Acting = false;
        }
    }

    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        // Pressed while the run is still starting: it stops as soon as it has started (3.4 re-review).
        _stopAsked = true;
        _run?.Stop();
    }

    private bool _stopAsked;

    partial void OnIsRunningChanged(bool value) => PastRuns.IsRunning = value;

    /// <summary>
    /// Disposes a run a new one replaces (peer review P11): it has ended (Start waits for that), but holds its stop and its
    /// trace's recording until then.
    /// </summary>
    private async Task DisposeReplacedAsync(MonitorRun run)
    {
        try
        {
            await run.DisposeAsync();
        }
        catch (Exception ex)
        {
            // How the run ended was logged and shown when it did; this is about what it still held.
            _logger.LogWarning(ex, "Monitoring run {RunId}: disposing it failed", run.Id);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Allocations.Close();
        if (_run is { } run)
        {
            await run.DisposeAsync();
        }
    }

    /// <summary>The rules from the form, or null after saying what is wrong.</summary>
    private MonitorRules? ParseRules()
    {
        // The form's empty fields set no rule; minutes in the current culture (the app's grammar).
        var parsed = MonitorRulesParser.Parse(
            new MonitorRulesInput(Filled(Every), Filled(Above), Filled(Growth), Filled(MaxSnapshots), Rearm),
            MonitorRuleGrammar.Minutes(CultureInfo.CurrentCulture));
        if (parsed.FirstError is { } error)
        {
            Shown.Status = error;
        }

        return parsed.Rules;
    }

    private static string? Filled(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private void OnSample(MonitorRun run, MonitorSample sample)
    {
        if (!ReferenceEquals(run, _run))
        {
            return;
        }

        if (_warnedNoSamples)
        {
            _warnedNoSamples = false;
            Shown.RunWarning = null;
        }

        var s = sample.Counters;
        HighGcPressure = sample.HighGcPressure;
        Latest = string.Create(CultureInfo.CurrentCulture,
            $"GC heap {Format.Bytes(s.GcHeapBytes)}, after the last GC {Format.Bytes(s.HeapAfterGcBytes)} (gen1 {Format.Bytes(s.Gen1Bytes)}, gen2 {Format.Bytes(s.Gen2Bytes)}, LOH {Format.Bytes(s.LargeObjectHeapBytes)}, POH {Format.Bytes(s.PinnedObjectHeapBytes)}); allocated {Format.Bytes(s.AllocatedBytes)}; GCs {s.Gen0Collections}/{s.Gen1Collections}/{s.Gen2Collections}; last GC {s.TimeInGcPercent:0.#}% of the time");
        Shown.Add(s);
    }

    private void OnGcForced(MonitorRun run, MonitorForcedGc forced)
    {
        // Only a collection whose end was seen is marked (review of 3.4).
        if (!ReferenceEquals(run, _run) || forced.Result is not { Completed: true })
        {
            return;
        }

        Shown.AddForcedGc(forced.At);
    }

    private void OnTraceStopped(MonitorRun run, string message)
    {
        if (ReferenceEquals(run, _run))
        {
            Shown.Status = message;
        }
    }

    private void OnTraceStarted(MonitorRun run, LiveTraceFile trace)
    {
        if (ReferenceEquals(run, _run))
        {
            Allocations.Follow(trace.Path, trace.NamesPath);
        }
    }

    private void OnTraceEnded(MonitorRun run)
    {
        if (ReferenceEquals(run, _run))
        {
            Allocations.End();
        }
    }

    private void OnCaptured(MonitorRun run, MonitorCapture capture)
    {
        if (!ReferenceEquals(run, _run))
        {
            return;
        }

        Shown.Add(new SnapshotCard(capture.At, capture.Rule, capture.Result?.Path, capture.Result?.SizeBytes ?? 0, capture.Error));
    }

    private async Task WatchAsync(MonitorRun run)
    {
        string? ended = null;
        try
        {
            await run.Completion;
        }
        catch (CaptureFailedException ex)
        {
            _logger.LogWarning("Monitoring run {RunId} (pid {Pid}) refused: {Reason}", run.Id, run.ProcessId, ex.Message);
            ended = ex.Message;
        }
        catch (OperationCanceledException)
        {
            // Stopped before the session connected: a normal stop.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Monitoring pid {Pid} failed", run.ProcessId);
            ended = "Monitoring failed. Details are in the log.";
        }

        _dispatcher.Post(() =>
        {
            if (!ReferenceEquals(run, _run))
            {
                return;
            }

            // The final status first: whoever sees IsRunning turn false (a test on another thread) then reads it.
            Shown.Status = ended is not null ? null : string.Create(CultureInfo.CurrentCulture,
                $"Stopped after {Shown.Samples.Count:N0} samples and {run.Captures.Count(c => c.Result is not null)} snapshots.");

            // "Nothing to chart yet" and "Get snapshot still works" are about a run that is still going.
            Shown.RunWarning = ended ?? (_warnedNoSamples ? null : Shown.RunWarning);
            _warnedNoSamples = false;
            Files.Show(run.TimelinePath, run.TracePath);

            // Not for a refused run: the warning says why, and "the run goes on without it" would be false.
            if (ended is null && run.TraceError is { } traceError)
            {
                Shown.Status = string.IsNullOrEmpty(Shown.Status) ? traceError : Shown.Status + " " + traceError;
            }

            IsRunning = false;
            _ = PastRuns.RefreshRunsCommand.ExecuteAsync(null);
        });
    }
}
