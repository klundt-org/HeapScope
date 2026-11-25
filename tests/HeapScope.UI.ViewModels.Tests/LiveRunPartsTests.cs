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
using HeapScope.Application.Jobs;
using HeapScope.Application.Sessions;
using HeapScope.Contracts;
using HeapScope.UI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>The Live page's past runs and run files (sprint 4.3), each without a monitoring run: what they show and where they read.</summary>
public sealed class LiveRunPartsTests : IDisposable
{
    private static readonly DateTimeOffset s_at = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));
    private readonly FakeCaptureService _capture;
    private readonly FakeUserInteraction _interaction = new();
    private readonly LiveRunState _shown = new();
    private readonly LiveRunFilesViewModel _files;
    private readonly LivePastRunsViewModel _past;

    public LiveRunPartsTests()
    {
        _capture = new FakeCaptureService(_root, new JobTracker(TimeProvider.System));
        _files = new LiveRunFilesViewModel(_shown, _interaction, allocations: null, NullLogger.Instance);
        _past = new LivePastRunsViewModel(_capture, _shown, _files, NullLogger.Instance);
    }

    [Fact]
    public async Task A_session_s_runs_are_listed_newest_first()
    {
        await _past.RefreshRunsCommand.ExecuteAsync(null);
        _past.Runs.ShouldBeEmpty("no session is created to list nothing");

        Record("a.csv", s_at);
        Record("b.csv", s_at.AddHours(1));
        await _past.RefreshRunsCommand.ExecuteAsync(null);

        _past.Runs.Select(r => r.Run.Timeline).ShouldBe(["b.csv", "a.csv"]);
    }

    [Fact]
    public async Task A_past_run_shows_its_samples_files_and_unreadable_lines()
    {
        var item = Record("a.csv", s_at, "not a row");
        _shown.RunWarning = "From the last run.";

        await _past.OpenPastRunCommand.ExecuteAsync(item);

        _shown.Samples.Select(s => s.GcHeapBytes).ShouldBe([100L, 200]);
        _shown.PastRunShown!.ShouldEndWith("(1 unreadable lines left out)");
        (_shown.RunWarning, _shown.ChartStart).ShouldBe((null, s_at));
        _files.TimelinePath.ShouldBe(Path.Combine(item.Folder, "a.csv"));
        _files.HasTimeline.ShouldBeTrue();
    }

    [Fact]
    public async Task A_refused_timeline_leaves_what_is_shown()
    {
        var item = Record("a.csv", s_at);
        await _past.OpenPastRunCommand.ExecuteAsync(item);

        await _past.OpenPastRunCommand.ExecuteAsync(item with { Run = item.Run with { Timeline = "../outside.csv" } });

        _shown.Samples.Count.ShouldBe(2);
        _files.TimelinePath.ShouldBe(Path.Combine(item.Folder, "a.csv"));
        _shown.Status!.ShouldStartWith("This run's timeline");
    }

    /// <summary>A queued context stands for the UI thread: a command that read the disk on it would end before it is pumped.</summary>
    [Fact]
    public async Task A_past_run_is_read_off_the_UI_thread_and_not_shown_over_a_run_that_started_meanwhile()
    {
        var item = Record("a.csv", s_at);

        await OnQueuedContextAsync(() => _past.OpenPastRunCommand.ExecuteAsync(item), beforePump: () =>
        {
            _shown.HasSamples.ShouldBeFalse("nothing is shown before the UI thread goes on");
            _past.IsRunning = true;
        });

        (_shown.HasSamples, _files.HasTimeline, _shown.Status).ShouldBe((false, false, null));
        _past.OpenPastRunCommand.CanExecute(item).ShouldBeFalse();
    }

    [Fact]
    public async Task The_run_s_files_are_looked_at_off_the_UI_thread()
    {
        var path = Path.Combine(Record("a.csv", s_at).Folder, "a.csv");
        _files.Show(path, trace: null);

        await OnQueuedContextAsync(() => _files.OpenTimelineCommand.ExecuteAsync(null));
        await OnQueuedContextAsync(() => _files.ShowSnapshotCommand.ExecuteAsync(new SnapshotCard(s_at, "manual", path, 1, null)));
        await OnQueuedContextAsync(() => _files.DeleteTimelineCommand.ExecuteAsync(null));

        (_interaction.Opened.Single(), _interaction.Shown.Single(), File.Exists(path)).ShouldBe((path, path, false));
    }

    [Fact]
    public async Task A_timeline_that_is_gone_does_not_take_another_run_s_with_it()
    {
        var other = Path.Combine(_root, "other.csv");
        _files.Show(Path.Combine(_root, "gone.csv"), trace: null);

        await OnQueuedContextAsync(() => _files.OpenTimelineCommand.ExecuteAsync(null), beforePump: () => _files.Show(other, trace: null));

        _files.TimelinePath.ShouldBe(other);
        _interaction.Opened.ShouldBeEmpty();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private PastRunItem Record(string timeline, DateTimeOffset started, params string[] more)
    {
        var folder = _capture.Library.Current.Folder;
        File.WriteAllLines(Path.Combine(folder, timeline), ["header", Row(0, 100), Row(1, 200), .. more]);
        var run = new SessionRun(Guid.NewGuid(), "app", 42, 1, string.Empty, SnapshotKindDto.FullDump, started, started.AddMinutes(1), timeline, null, []);
        _capture.Library.RecordRun(folder, run);
        return new PastRunItem(run, folder);

        static string Row(int second, long heap) => FormattableString.Invariant($"{s_at.AddSeconds(second):O},{heap},0,0,0,0,0,0,0,0,0,0,0,");
    }

    /// <summary>Starts a command on a context that runs nothing by itself, then runs what it queued until the command ends.</summary>
    private static async Task OnQueuedContextAsync(Func<Task> command, Action? beforePump = null)
    {
        var context = new QueuedContext();
        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        Task running;
        try
        {
            running = command();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
        }

        running.IsCompleted.ShouldBeFalse("its file work is not on this thread");
        beforePump?.Invoke();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!running.IsCompleted)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the command did not end");
            if (!context.Run())
            {
                await Task.Delay(5, TestContext.Current.CancellationToken);
            }
        }

        await running;
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public bool Run()
        {
            if (!_queue.TryDequeue(out var next))
            {
                return false;
            }

            var before = Current;
            SetSynchronizationContext(this);
            try
            {
                next.Callback(next.State);
            }
            finally
            {
                SetSynchronizationContext(before);
            }

            return true;
        }
    }
}
