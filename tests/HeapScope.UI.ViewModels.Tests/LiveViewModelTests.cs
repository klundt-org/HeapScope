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

using System.IO;
using System.Threading.Channels;
using HeapScope.Application.Jobs;
using HeapScope.Application.Monitoring;
using HeapScope.Application.Sessions;
using HeapScope.Contracts;
using HeapScope.UI.Services;
using HeapScope.UI.ViewModels;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>The Live page (sprint 1.7): form validation, samples to the chart, rule snapshots as cards, stopping.</summary>
public sealed class LiveViewModelTests : IAsyncDisposable
{
    private const long Mb = 1024 * 1024;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));
    private readonly FeedSource _feed = new();
    private readonly FakeCaptureService _capture;
    private readonly FakeUserInteraction _interaction = new();
    private readonly LiveViewModel _live;
    private readonly List<(TimeSpan Wait, TaskCompletionSource Done)> _delays = [];

    public LiveViewModelTests()
    {
        _capture = new FakeCaptureService(_root, new JobTracker(TimeProvider.System));
        _capture.Processes.Add(new ProcessEntry(42, "app", null, "10.0.0", ProcessElevation.NotElevated, false, null, null));
        _live = new LiveViewModel(new MonitorService(_capture, _feed), _capture, new ImmediateUiDispatcher(), interaction: _interaction)
        {
            NoSampleDelay = wait =>
            {
                var done = new TaskCompletionSource();
                _delays.Add((wait, done));
                return done.Task;
            },
        };
    }

    [Fact]
    public async Task The_chooser_hides_processes_HeapScope_cannot_watch_until_the_box_is_checked()
    {
        // Sprint 4.1.1: the same rule as the Processes page.
        _capture.Processes.Add(new ProcessEntry(77, "service", null, "10.0.0", ProcessElevation.Elevated, false, null, null));

        await _live.RefreshCommand.ExecuteAsync(null);

        _live.Processes.Select(p => p.ProcessId).ShouldBe([42]);
        _live.CanShowHiddenProcesses.ShouldBeTrue();
        _live.HiddenNote.ShouldBe(HeapScope.Application.Sessions.RightsTexts.HiddenNote(1));

        _live.ShowHiddenProcesses = true;
        _live.Processes.Select(p => p.ProcessId).ShouldBe([42, 77]);
        _live.Processes[1].ElevationSuffix.ShouldBe(", " + HeapScope.Application.Sessions.RightsTexts.NeedsMore, "a shown one says what it needs");
        _live.Processes[0].ElevationSuffix.ShouldBeEmpty();
        _live.HiddenNote.ShouldBeNull();

        _live.SelectedProcess = _live.Processes[1];
        _live.ShowHiddenProcesses = false;
        _live.SelectedProcess.ShouldBeNull("a hidden process is not chosen");
    }

    [Fact]
    public async Task Run_elevated_the_chooser_lists_every_process_with_no_box()
    {
        _capture.IsHeapScopeElevated = true;
        _capture.Processes.Add(new ProcessEntry(77, "service", null, "10.0.0", ProcessElevation.Elevated, false, null, null));

        await _live.RefreshCommand.ExecuteAsync(null);

        _live.Processes.Select(p => p.ProcessId).ShouldBe([42, 77]);
        _live.CanShowHiddenProcesses.ShouldBeFalse();
        _live.HiddenNote.ShouldBeNull();
    }

    private async Task StartLiveAsync()
    {
        if (_live.SelectedProcess is null)
        {
            await _live.RefreshCommand.ExecuteAsync(null);
            _live.SelectedProcess = _live.Processes.Single();
        }

        await _live.StartCommand.ExecuteAsync(null);
        _live.IsRunning.ShouldBeTrue();
    }

    private async Task StopLiveAsync()
    {
        _live.StopCommand.Execute(null);
        await _live.Run!.Completion;
        await WaitUntilAsync(() => !_live.IsRunning);
    }

    [Fact]
    public async Task Start_needs_a_process_and_valid_rules()
    {
        await _live.StartCommand.ExecuteAsync(null);
        _live.Shown.Status.ShouldBe("Pick a process to monitor.");

        await _live.RefreshCommand.ExecuteAsync(null);
        _live.SelectedProcess = _live.Processes.Single();
        foreach (var (field, text, message) in new (string Field, string Text, string Message)[]
        {
            ("above", "lots", "Above: enter a size above zero, such as 500MB or 2GB."),
            ("above", "50%", "Above: enter a size above zero, such as 500MB or 2GB."),
            ("above", "0MB", "Above: enter a size above zero, such as 500MB or 2GB."),
            ("growth", "much", "Growth: enter a size or a percent above zero, such as 100MB or 50%."),
            ("growth", "0%", "Growth: enter a size or a percent above zero, such as 100MB or 50%."),
            ("every", "0", "Every: enter minutes, such as 5 or 0.5."),
            ("max", "0", "Max snapshots: enter a whole number of 1 or more."),
        })
        {
            (_live.Above, _live.Growth, _live.Every, _live.MaxSnapshots) = (null, null, null, null);
            switch (field)
            {
                case "above": _live.Above = text; break;
                case "growth": _live.Growth = text; break;
                case "every": _live.Every = text; break;
                default: _live.MaxSnapshots = text; break;
            }

            await _live.StartCommand.ExecuteAsync(null);
            (_live.Shown.Status, _live.IsRunning).ShouldBe((message, false), $"{field} = {text}");
        }
    }

    [Fact]
    public async Task Get_snapshot_and_force_gc_mark_the_run_and_a_past_run_reopens_read_only()
    {
        // Sprint 3.4 (decisions 2, 1 and 5).
        await _live.RefreshCommand.ExecuteAsync(null);
        _live.SelectedProcess = _live.Processes.Single();
        _live.GetSnapshotCommand.CanExecute(null).ShouldBeFalse("only while monitoring");

        await _live.StartCommand.ExecuteAsync(null);
        await _feed.SendAsync(100 * Mb, timeInGc: 2);
        await _live.GetSnapshotCommand.ExecuteAsync(null);
        _live.Shown.Status.ShouldBe("Snapshot taken.");
        await _live.ForceGcCommand.ExecuteAsync(null);
        _live.Shown.Status!.ShouldStartWith("A full garbage collection ran");
        await _feed.SendAsync(120 * Mb, timeInGc: 2);
        _capture.Busy.Add(42);
        await _live.ForceGcCommand.ExecuteAsync(null);
        _live.Shown.Status!.ShouldContain("Wait for it to finish");
        _capture.Busy.Clear();
        _interaction.Answer = false;
        await _live.ForceGcCommand.ExecuteAsync(null);
        _interaction.Answer = true;
        _interaction.Questions.ShouldContain(q => q.Contains("Finalizers do not run", StringComparison.Ordinal), "asked first, with what it does to the target");

        (_live.Shown.Snapshots.ShouldHaveSingleItem().Rule, _live.Shown.Markers.Count, _live.Shown.GcMarkers.Count, _capture.ForcedGcs.Count).ShouldBe(("manual", 1, 1, 1));
        _live.StopCommand.Execute(null);
        await _live.Run!.Completion;
        await WaitUntilAsync(() => !_live.IsRunning && _live.PastRuns.Runs.Count == 1);

        var past = _live.PastRuns.Runs.ShouldHaveSingleItem();
        past.Title.ShouldContain("app 42, 1 snapshots");
        await _live.StartCommand.ExecuteAsync(null);
        _live.PastRuns.OpenPastRunCommand.CanExecute(past).ShouldBeFalse("not while monitoring");
        _live.StopCommand.Execute(null);
        await _live.Run!.Completion;
        await WaitUntilAsync(() => !_live.IsRunning);

        await _live.PastRuns.OpenPastRunCommand.ExecuteAsync(past);

        _live.Shown.Samples.Count.ShouldBe(2);
        (_live.Shown.Markers.Count, _live.Shown.GcMarkers.Count, _live.Shown.Snapshots.Count).ShouldBe((1, 1, 1));
        _live.Shown.PastRunShown.ShouldBe(past.Title);
        _live.Shown.Status!.ShouldStartWith("Showing a past run");
    }

    [Fact]
    public async Task Rules_capture_gcdumps_when_asked()
    {
        await _live.RefreshCommand.ExecuteAsync(null);
        _live.SelectedProcess = _live.Processes.Single();
        _live.Above = "500MB";
        _live.CaptureGcDumps = true;

        await _live.StartCommand.ExecuteAsync(null);
        await _feed.SendAsync(600 * Mb, timeInGc: 2);
        await WaitUntilAsync(() => _live.Shown.Snapshots.Count == 1);
        _live.StopCommand.Execute(null);

        _capture.Kinds.ShouldBe([SnapshotKindDto.GcDump]);
    }

    /// <summary>A refused run lasts milliseconds: what stays is a warning that says why, and no "choose a process" under it.</summary>
    [Fact]
    public async Task A_refused_run_leaves_a_warning_that_says_why_and_no_prompt()
    {
        await using var live = new LiveViewModel(new MonitorService(_capture, new RefusingSource()), _capture, new ImmediateUiDispatcher(), interaction: _interaction);
        await live.RefreshCommand.ExecuteAsync(null);
        live.SelectedProcess = live.Processes.Single();

        await live.StartCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => !live.IsRunning);

        live.Shown.RunWarning.ShouldNotBeNull().ShouldContain("cannot monitor it");
        live.Shown.RunWarning!.ShouldContain(OperatingSystem.IsMacOS() ? "Run HeapScope as that user and" : OperatingSystem.IsLinux() ? "Run HeapScope as that user, or as root (sudo)" : "Run HeapScope as administrator");
        live.Shown.RunWarning!.ShouldNotContain("dump");
        live.Shown.Status.ShouldBeNull("the warning says it; a second line would repeat it");
        live.Shown.ShowEmptyState.ShouldBeFalse();
    }

    /// <summary>The trace is refused too then, and "the run goes on without it" would be false under a run that was refused.</summary>
    [Fact]
    public async Task A_refused_run_that_also_asked_for_a_trace_shows_only_the_refusal()
    {
        static Task<TraceResult> Refused(AllocationRecordingRequest request, CancellationToken ct) =>
            Task.FromException<TraceResult>(new CaptureFailedException(CaptureFailure.NeedsAdministrator, "cannot record its allocations", null));

        await using var live = new LiveViewModel(new MonitorService(_capture, new RefusingSource(), allocations: Refused), _capture, new ImmediateUiDispatcher(), interaction: _interaction);
        await live.RefreshCommand.ExecuteAsync(null);
        live.SelectedProcess = live.Processes.Single();
        live.RecordAllocations = true;

        await live.StartCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => !live.IsRunning);

        live.Shown.RunWarning.ShouldNotBeNull().ShouldContain("cannot monitor it");
        live.Shown.Status.ShouldBeNull();
    }

    /// <summary>A process can turn off the events the counters come from: the page says so instead of waiting in silence.</summary>
    [Fact]
    public async Task A_run_with_no_samples_says_so_and_the_first_sample_clears_it()
    {
        _live.EmptyText.ShouldBe(PlatformHints.LiveEmpty);
        var raised = new List<string?>();
        _live.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        _live.Shown.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await StartLiveAsync();
        _live.EmptyText.ShouldBe("Waiting for the first sample.");
        _live.Shown.ShowEmptyState.ShouldBeTrue();
        raised.ShouldContain(nameof(LiveViewModel.EmptyText), "or the page would keep saying choose a process");
        raised.Clear();
        _delays.ShouldHaveSingleItem().Wait.ShouldBe(TimeSpan.FromSeconds(10));
        _live.Shown.RunWarning.ShouldBeNull();
        _delays[0].Done.SetResult();

        _live.Shown.RunWarning!.ShouldStartWith("No samples from app after 10 s");
        _live.Shown.RunWarning!.ShouldContain("Get snapshot still works");
        raised.ShouldContain(nameof(LiveRunState.ShowEmptyState));
        _live.Shown.ShowEmptyState.ShouldBeFalse();
        _live.IsRunning.ShouldBeTrue();

        await _feed.SendAsync(100 * Mb, timeInGc: 2);
        await WaitUntilAsync(() => _live.Shown.RunWarning is null);
        _live.Shown.HasSamples.ShouldBeTrue();

        await StopLiveAsync();
        _live.EmptyText.ShouldBe(PlatformHints.LiveEmpty);
    }

    [Fact]
    public async Task A_long_interval_waits_three_intervals_before_it_warns()
    {
        _live.IntervalSeconds = 20;

        await StartLiveAsync();

        _delays.ShouldHaveSingleItem().Wait.ShouldBe(TimeSpan.FromSeconds(60));
        await StopLiveAsync();
    }

    /// <summary>"Nothing to chart yet" and "Get snapshot still works" are about a run still going.</summary>
    [Fact]
    public async Task Stopping_ends_the_no_sample_warning()
    {
        await StartLiveAsync();
        _delays[0].Done.SetResult();
        _live.Shown.RunWarning.ShouldNotBeNull();

        await StopLiveAsync();

        _live.Shown.RunWarning.ShouldBeNull();
        _live.Shown.Status!.ShouldStartWith("Stopped after 0 samples");
        _live.Shown.ShowEmptyState.ShouldBeTrue();
    }

    [Fact]
    public async Task The_wait_of_a_run_that_ended_warns_about_nothing()
    {
        await StartLiveAsync();
        await StopLiveAsync();
        _delays[0].Done.SetResult();
        _live.Shown.RunWarning.ShouldBeNull("the run stopped");

        await StartLiveAsync();
        _delays.Count.ShouldBe(2);
        await StopLiveAsync();
        await StartLiveAsync();
        _delays[1].Done.SetResult();
        _live.Shown.RunWarning.ShouldBeNull("that wait was another run's");

        _delays[2].Done.SetResult();
        _live.Shown.RunWarning.ShouldNotBeNull("this run's own wait");
        await StopLiveAsync();
    }

    [Fact]
    public async Task A_press_that_cannot_start_clears_the_last_warning()
    {
        await StartLiveAsync();
        _delays[0].Done.SetResult();
        _live.Shown.RunWarning.ShouldNotBeNull();
        await StopLiveAsync();
        _live.Shown.RunWarning = "From the last run.";
        _live.Above = "lots";

        await _live.StartCommand.ExecuteAsync(null);

        _live.Shown.RunWarning.ShouldBeNull();
        _live.Shown.Status!.ShouldStartWith("Above:");
    }

    [Fact]
    public async Task Samples_reach_the_chart_rules_become_cards_and_markers_and_stop_ends_the_run()
    {
        await _live.RefreshCommand.ExecuteAsync(null);
        _live.SelectedProcess = _live.Processes.Single();
        _live.Above = "500MB";
        _live.Rearm = "once";

        await _live.StartCommand.ExecuteAsync(null);
        _live.IsRunning.ShouldBeTrue();
        _live.Shown.Status!.ShouldContain("rules: above 500 MB");
        await _feed.SendAsync(100 * Mb, timeInGc: 2);
        await _feed.SendAsync(600 * Mb, timeInGc: 30);
        await WaitUntilAsync(() => _live.Shown.Snapshots.Count == 1);
        await _feed.SendAsync(700 * Mb, timeInGc: 30);

        _live.Shown.Samples.Select(s => s.HeapAfterGcBytes).ShouldBe([100 * Mb, 600 * Mb, 700 * Mb]);
        _live.Shown.ChartVersion.ShouldBeGreaterThanOrEqualTo(4, "a redraw per sample and per snapshot");
        _live.Latest!.ShouldContain("after the last GC 700");
        _live.HighGcPressure.ShouldBeTrue("(2 + 30 + 30) / 3 is over 10 percent");
        var card = _live.Shown.Snapshots.ShouldHaveSingleItem("once: one snapshot");
        (card.Rule, card.Error).ShouldBe(("above 500 MB", null));
        _live.Shown.Markers.ShouldHaveSingleItem().ShouldBe(_live.Shown.Samples[1].At);

        _live.StopCommand.Execute(null);
        await _live.Run!.Completion;
        await WaitUntilAsync(() => !_live.IsRunning);
        _live.Shown.Status!.ShouldStartWith("Stopped after 3 samples and 1 snapshots.");
        _live.StartCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task The_stopped_runs_timeline_opens_shows_in_its_folder_and_deletes_after_asking()
    {
        await _live.RefreshCommand.ExecuteAsync(null);
        _live.SelectedProcess = _live.Processes.Single();
        await _live.StartCommand.ExecuteAsync(null);
        _live.Files.HasTimeline.ShouldBeFalse("not while running");
        await _feed.SendAsync(100 * Mb, timeInGc: 2);
        await WaitUntilAsync(() => _live.Shown.Samples.Count == 1);
        _live.StopCommand.Execute(null);
        await _live.Run!.Completion;
        await WaitUntilAsync(() => !_live.IsRunning);

        var path = _live.Files.TimelinePath.ShouldNotBeNull();
        File.Exists(path).ShouldBeTrue();
        _live.Shown.Status!.ShouldNotContain(path, Case.Insensitive, "the path has its own row");
        await _live.Files.OpenTimelineCommand.ExecuteAsync(null);
        await _live.Files.ShowTimelineCommand.ExecuteAsync(null);
        (_interaction.Opened.Single(), _interaction.Shown.Single()).ShouldBe((path, path));

        _interaction.Answer = false;
        await _live.Files.DeleteTimelineCommand.ExecuteAsync(null);
        File.Exists(path).ShouldBeTrue("declined");
        _interaction.Questions.Single().ShouldContain(Path.GetFileName(path));

        _interaction.Answer = true;
        await _live.Files.DeleteTimelineCommand.ExecuteAsync(null);
        File.Exists(path).ShouldBeFalse();
        (_live.Files.TimelinePath, _live.Files.HasTimeline, _live.Shown.Status).ShouldBe((null, false, "Timeline deleted."));
        _live.Files.OpenTimelineCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task A_snapshot_card_shows_its_file_in_its_folder()
    {
        await _live.RefreshCommand.ExecuteAsync(null);
        _live.SelectedProcess = _live.Processes.Single();
        _live.Above = "500MB";
        await _live.StartCommand.ExecuteAsync(null);
        await _feed.SendAsync(600 * Mb, timeInGc: 2);
        await WaitUntilAsync(() => _live.Shown.Snapshots.Count == 1);
        _live.StopCommand.Execute(null);
        await _live.Run!.Completion;

        var card = _live.Shown.Snapshots.Single();
        var path = card.Path.ShouldNotBeNull();
        await _live.Files.ShowSnapshotCommand.ExecuteAsync(card);
        _interaction.Shown.ShouldBe([path]);

        File.Delete(path);
        await _live.Files.ShowSnapshotCommand.ExecuteAsync(card);
        _interaction.Shown.Count.ShouldBe(1);
        _live.Shown.Status.ShouldBe("That snapshot file is no longer there.");
    }

    [Fact]
    public async Task A_timeline_removed_outside_HeapScope_is_said_to_be_gone()
    {
        await _live.RefreshCommand.ExecuteAsync(null);
        _live.SelectedProcess = _live.Processes.Single();
        await _live.StartCommand.ExecuteAsync(null);
        await _feed.SendAsync(100 * Mb, timeInGc: 2);
        await WaitUntilAsync(() => _live.Shown.Samples.Count == 1);
        _live.StopCommand.Execute(null);
        await _live.Run!.Completion;
        await WaitUntilAsync(() => !_live.IsRunning);
        File.Delete(_live.Files.TimelinePath.ShouldNotBeNull());

        await _live.Files.OpenTimelineCommand.ExecuteAsync(null);

        _interaction.Opened.ShouldBeEmpty();
        (_live.Files.HasTimeline, _live.Shown.Status).ShouldBe((false, "The timeline file is no longer there."));
    }

    [Fact]
    public void The_chart_stacks_heap_kinds_over_the_samples_time_span()
    {
        var at = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        HeapSample[] samples =
        [
            new(at, 40, 10, 10, 20, 0, 0, 0, 0, 0, 0, 0, 0),
            new(at.AddSeconds(10), 100, 10, 10, 50, 10, 10, 0, 0, 0, 0, 0, 0),
        ];

        var layers = TimelineLayout.Layers(samples, width: 200, height: 100, out var max);

        max.ShouldBe(100);
        layers.Count.ShouldBe(6);
        layers[5][1].Y.ShouldBe(0.0, "allocated since the last GC (10 bytes) tops the stack at the heap now");
        layers[4][1].Y.ShouldBe(10.0);
        layers[0].Select(p => (p.X, p.Y)).ShouldBe([(0.0, 90.0), (200.0, 90.0)], "gen0 at the bottom");
        layers[2].Select(p => p.Y).ShouldBe([60.0, 30.0], "gen2 stacked on gen0 and gen1");
        TimelineLayout.XOf(samples, at.AddSeconds(5), 200).ShouldBe(100.0);
        TimelineLayout.XOf(samples, at.AddSeconds(20), 200).ShouldBeNull();
    }

    public async ValueTask DisposeAsync()
    {
        await _live.DisposeAsync();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "condition not met in time");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class RefusingSource : ICounterSource
    {
        public Task RunAsync(int processId, TimeSpan interval, Action<HeapSample> onSample, CancellationToken cancellationToken) =>
            Task.FromException(global::HeapScope.Capture.ElevationRequiredException.ForMonitoring(processId, "app"));
    }

    /// <summary>Samples sent by the test, handed out on the source's own thread like EventPipe's.</summary>
    private sealed class FeedSource : ICounterSource
    {
        private readonly Channel<(long Heap, double TimeInGc)> _samples = Channel.CreateUnbounded<(long, double)>();
        private readonly SemaphoreSlim _handled = new(0);
        private DateTimeOffset _at = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

        public async Task SendAsync(long heapBytes, double timeInGc)
        {
            await _samples.Writer.WriteAsync((heapBytes, timeInGc));
            await _handled.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public async Task RunAsync(int processId, TimeSpan interval, Action<HeapSample> onSample, CancellationToken cancellationToken)
        {
            try
            {
                while (await _samples.Reader.WaitToReadAsync(cancellationToken))
                {
                    var (heap, timeInGc) = await _samples.Reader.ReadAsync(cancellationToken);
                    _at += interval;
                    onSample(new HeapSample(_at, heap + (10 * Mb), 0, heap / 10, heap * 7 / 10, heap / 5, 0, 1, 0, 0, Mb, timeInGc, heap * 2));
                    _handled.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped.
            }
        }
    }

    [Theory]
    [InlineData(941L * 1024 * 1024, new long[] { 200, 400, 600, 800 })]
    [InlineData(50L * 1024 * 1024, new long[] { 20, 40 })]
    [InlineData(3L * 1024 * 1024, new long[] { 1, 2, 3 })]
    [InlineData(0L, new long[0])]
    public void The_chart_has_a_few_round_gridlines(long maxBytes, long[] megabytes) =>
        TimelineLayout.ByteTicks(maxBytes).ShouldBe(megabytes.Select(m => m * 1024 * 1024));

    [Fact]
    public async Task The_empty_state_hides_when_samples_arrive_and_returns_when_a_new_run_clears_them()
    {
        var raised = new List<string?>();
        _live.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        _live.Shown.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        _live.Shown.HasSamples.ShouldBeFalse();
        await _live.RefreshCommand.ExecuteAsync(null);
        _live.SelectedProcess = _live.Processes.Single();

        await _live.StartCommand.ExecuteAsync(null);
        await _feed.SendAsync(100 * Mb, timeInGc: 2);
        await WaitUntilAsync(() => _live.Shown.Samples.Count == 1);

        _live.Shown.HasSamples.ShouldBeTrue();
        raised.ShouldContain(nameof(LiveRunState.HasSamples), "a list does not notify, so this must");
        _live.Shown.ChartStart.ShouldBe(_live.Shown.Samples[0].At);
        _live.StopCommand.Execute(null);
        await _live.Run!.Completion;
        await WaitUntilAsync(() => !_live.IsRunning);

        await _live.StartCommand.ExecuteAsync(null);
        _live.Shown.HasSamples.ShouldBeFalse("a new run starts empty");
        _live.Shown.ChartStart.ShouldBeNull();
        _live.StopCommand.Execute(null);
        await _live.Run!.Completion;
        await WaitUntilAsync(() => !_live.IsRunning);
    }

    /// <summary>A run a new one replaces is disposed (peer review P11), and the new one is the page's.</summary>
    [Fact]
    public async Task A_replaced_run_is_disposed()
    {
        await StartLiveAsync();
        var first = _live.Run!;
        await StopLiveAsync();
        first.Stop();

        await StartLiveAsync();

        _live.Run.ShouldNotBeSameAs(first);
        Should.Throw<ObjectDisposedException>(first.Stop);
        await StopLiveAsync();
    }

    [Fact]
    public void The_time_axis_counts_from_the_run_s_start_once_the_window_has_slid()
    {
        var at = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        HeapSample Sample(int seconds) => new(at.AddSeconds(seconds), 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        var ticks = TimelineLayout.TimeTicks([Sample(630), Sample(930)], 600, at);

        ticks.Select(t => t.Label).ShouldBe(["11:00", "12:00", "13:00", "14:00", "15:00"]);
        ticks[0].X.ShouldBe(60, "30 s into the window");
        TimelineLayout.TimeTicks([Sample(0), Sample(300)], 600, at).Select(t => t.Label).First().ShouldBe("0:00");
    }

    [Fact]
    public void The_time_axis_labels_elapsed_time_at_round_steps()
    {
        var at = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        HeapSample Sample(int seconds) => new(at.AddSeconds(seconds), 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        var ticks = TimelineLayout.TimeTicks([Sample(0), Sample(300)], 600);
        ticks.Select(t => t.Label).ShouldBe(["0:00", "1:00", "2:00", "3:00", "4:00", "5:00"]);
        ticks[1].X.ShouldBe(120);
        TimelineLayout.TimeTicks([Sample(0), Sample(7200)], 600).Select(t => t.Label).ShouldContain("1:00:00");
        TimelineLayout.TimeTicks([Sample(0)], 600).ShouldBeEmpty();
    }
}
