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
using HeapScope.Application.Allocations;
using HeapScope.Application.Jobs;
using HeapScope.Application.Sessions;
using HeapScope.Application.Workers;
using HeapScope.Contracts;
using HeapScope.UI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>The Live page's allocations (sprint 4.3): following a run's trace, its types and stacks, and what is said of it.</summary>
public sealed class LiveAllocationsViewModelTests
{
    private const string TracePath = "live.nettrace";

    private readonly FakeWorkerClient _worker = new();
    private readonly LiveAllocationsViewModel _live;

    /// <summary>The waits between two questions: each is let go by the test.</summary>
    private readonly SemaphoreSlim _tick = new(0);

    private int _waits;

    public LiveAllocationsViewModelTests()
    {
        var session = new AllocationSession(_worker, new JobTracker(TimeProvider.System), new TraceService(), new SessionLibrary(Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"))));
        _live = new LiveAllocationsViewModel(session, NullLogger.Instance)
        {
            Refresh = TimeSpan.Zero,
            Wait = async (_, ct) =>
            {
                Interlocked.Increment(ref _waits);
                await _tick.WaitAsync(ct);
            },
        };
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeWorkerService Service => _worker.Service;

    [Fact]
    public async Task A_run_is_followed_from_its_recording_s_start_to_its_end_and_the_figures_stay()
    {
        _live.IsShown.ShouldBeFalse();
        _live.Follow(TracePath, "names");
        await WaitsAsync(1);

        _live.IsShown.ShouldBeTrue();
        _live.Summary.ShouldBe("Waiting for the first allocation sample.");
        Service.LiveOpened.ShouldHaveSingleItem().NamesPath.ShouldBe("names");

        State(s => s with { Version = 4, Samples = 4, SampledBytes = 400, Types = 2, DurationSeconds = 10, DelaySeconds = 0.2 });
        await TickAsync();
        _live.Summary!.ShouldContain("4 samples, 2 types, 10 s");
        _live.Types.Select(t => (t.Name, t.Bytes, t.Samples, t.Percent)).ShouldBe([("System.Byte[]", 300L, 3L, 75.0), ("System.String", 100L, 1L, 25.0)]);
        (_live.Buckets.Count, _live.DurationSeconds, _live.Delay, _live.Note).ShouldBe((2, 10.0, null, string.Empty));

        _live.End();
        State(s => s with { Completed = true });
        await TickAsync(ends: true);

        Service.LiveEnded.ShouldHaveSingleItem();
        _live.IsShown.ShouldBeTrue("the figures stay until another run");
        _live.Types.Count.ShouldBe(2);
        Service.TracesClosed.ShouldBeEmpty("the worker keeps them: an interval can still be chosen");
    }

    [Fact]
    public async Task Types_keep_their_rows_and_the_selected_one_as_figures_change()
    {
        await FollowAsync();
        var bytes = _live.Types[0];
        _live.SelectedType = _live.Types[1];

        Service.TypeRows = [["System.String", "500", "5", "62"], ["App.Order", "200", "2", "25"], ["System.Byte[]", "100", "1", "12"]];
        await RefreshAsync(s => s with { Version = 9 });

        _live.Types.Select(t => t.Name).ShouldBe(["System.String", "App.Order", "System.Byte[]"]);
        _live.SelectedType.ShouldBeSameAs(_live.Types[0]);
        (_live.Types[2], bytes.Bytes).ShouldBe((bytes, 100L), "the same row, with new figures");

        Service.TypeRows = [["System.String", "500", "5", "100"]];
        await RefreshAsync(s => s with { Version = 10 });
        _live.Types.ShouldHaveSingleItem().ShouldBeSameAs(_live.SelectedType);

        // It falls behind the types listed, and stays.
        Service.TypeRows = [.. Enumerable.Range(0, 30).Select(i => (IReadOnlyList<string?>)[$"T{i}", "100", "1", "1"]), ["System.String", "50", "5", "1"]];
        await RefreshAsync(s => s with { Version = 11 });
        _live.Types.Count.ShouldBe(LiveAllocationsViewModel.TopTypes + 1);
        (_live.Types[^1], _live.Types[^1].Bytes).ShouldBe((_live.SelectedType, 50L));

        Service.TypeRows = [["App.Order", "200", "2", "25"]];
        await RefreshAsync(s => s with { Version = 12 });
        (_live.Types.Single().Name, _live.SelectedType).ShouldBe(("App.Order", null), "nothing of it is in the interval any more");
    }

    [Fact]
    public async Task An_interval_is_taken_in_whole_stretches_and_narrows_the_types_at_the_next_question()
    {
        await FollowAsync(s => s with { BucketSeconds = 2 });

        _live.Selection = new TimeInterval(6.5, 3.1);

        (_live.Selection.FromSeconds, _live.Selection.ToSeconds).ShouldBe((2.0, 8.0));
        _live.Interval.ShouldBe("From 2 s to 8 s");
        await TickAsync();
        var asked = Service.TypeQueries[^1];
        (asked.FromSeconds, asked.ToSeconds).ShouldBe((2.0, 8.0), "asked although nothing new arrived");

        _live.WholeRunCommand.Execute(null);
        await TickAsync();
        (_live.Selection, _live.Interval, Service.TypeQueries[^1].ToSeconds).ShouldBe((null, "The whole run so far", null));
        _live.Selection = new TimeInterval(3, 3);
        _live.Selection.ShouldBeNull("a click is no interval");
    }

    [Fact]
    public async Task Choosing_an_interval_after_the_end_still_answers()
    {
        await FollowAsync();
        _live.End();
        State(s => s with { Completed = true });
        await TickAsync(ends: true);

        _live.Selection = new TimeInterval(1, 2);
        await _live.FollowTask;

        Service.TypeQueries[^1].ToSeconds.ShouldBe(2);
    }

    [Fact]
    public async Task Call_stacks_are_asked_for_on_demand_of_the_selected_type()
    {
        await FollowAsync();
        Service.StackQueries.ShouldBeEmpty("not before they are asked for");

        _live.ShowStacks = true;
        await WaitUntilAsync(() => _live.Stacks.Count == 2);
        _live.StacksOf.ShouldBe("all types");
        _live.SelectedFrames.ShouldBe(["App.Grow", "App.Handle", "App.Main"]);
        _live.StacksStatus!.ShouldContain("Refresh brings them up to date");
        _live.AllTypesCommand.CanExecute(null).ShouldBeFalse();

        _live.SelectedType = _live.Types[1];
        await WaitUntilAsync(() => _live.Stacks.Count == 1);
        (_live.StacksOf, Service.StackQueries[^1].TypeName).ShouldBe(("System.String", "System.String"));

        _live.AllTypesCommand.Execute(null);
        await WaitUntilAsync(() => _live.Stacks.Count == 2);
        var asked = Service.StackQueries.Count;
        _live.RefreshStacksCommand.Execute(null);
        await WaitUntilAsync(() => Service.StackQueries.Count == asked + 1);
        _live.ShowStacks = false;
        (_live.Stacks.Count, _live.StacksStatus).ShouldBe((0, null));
    }

    [Fact]
    public async Task What_the_figures_leave_out_and_how_far_behind_they_are_is_said()
    {
        await FollowAsync();
        State(s => s with { DelaySeconds = 4.2, LostEvents = 7, SamplesPastLimits = 3, HasStacks = false });
        await TickAsync();
        _live.Delay.ShouldBe("These figures are 4 s behind the process.");
        _live.Note!.ShouldContain("dropped 7 events");
        _live.Note!.ShouldContain("3 samples are added up");
        _live.Note!.ShouldContain("Call stacks cannot be shown");

        State(s => s with { DelaySeconds = 0.3 });
        await TickAsync();
        _live.Delay.ShouldBeNull("it caught up");

        State(s => s with { Problem = "It stopped.", Completed = true });
        await TickAsync(ends: true);
        _live.Note!.ShouldStartWith("It stopped. The figures stop there.");
    }

    [Fact]
    public async Task Another_run_takes_the_place_of_the_one_shown()
    {
        await FollowAsync();
        _live.ShowStacks = true;
        _live.SelectedType = _live.Types[0];

        State(s => s with { Version = 0, Samples = 0 });
        _live.Follow("other.nettrace", null);
        await WaitUntilAsync(() => Service.TracesClosed.Count == 1);

        Service.LiveOpened[^1].Path.ShouldEndWith("other.nettrace");
        (_live.Types.Count, _live.Stacks.Count, _live.ShowStacks, _live.SelectedType, _live.Selection).ShouldBe((0, 0, false, null, null));
        _live.Close();
        (_live.IsShown, _live.Summary).ShouldBe((false, null));
        await WaitUntilAsync(() => Service.TracesClosed.Count == 2);
    }

    [Fact]
    public async Task A_worker_that_stops_is_asked_again_and_another_failure_says_why()
    {
        await FollowAsync();
        Service.LiveFailure = new WorkerCrashedException("The worker stopped.");
        await TickAsync();
        _live.Note.ShouldBe(string.Empty, "it restarts by itself");
        Service.LiveFailure = null;
        await RefreshAsync(s => s with { Version = 20 });
        _live.Types.Count.ShouldBe(2);

        Service.LiveFailure = new WorkerOperationException(HeapScopeErrorCode.TraceChanged, "It is another recording.", null);
        await TickAsync(ends: true);
        _live.Note.ShouldBe("It is another recording.");
        _live.IsShown.ShouldBeTrue();
    }

    [Fact]
    public async Task A_trace_that_cannot_be_followed_says_why_and_the_run_is_untouched()
    {
        Service.LiveFailure = new WorkerOperationException(HeapScopeErrorCode.InsufficientMemory, "Following the recording needs about 160 MB.", null);

        _live.Follow(TracePath, null);
        await _live.FollowTask;

        (_live.IsShown, _live.Note).ShouldBe((true, "Following the recording needs about 160 MB."));
    }

    [Fact]
    public void Without_a_session_nothing_is_shown()
    {
        var none = new LiveAllocationsViewModel(null, NullLogger.Instance);
        none.Follow(TracePath, null);
        none.End();
        none.IsShown.ShouldBeFalse();
    }

    private void State(Func<LiveTraceDto, LiveTraceDto> change) => Service.LiveState = change(Service.LiveState);

    private async Task FollowAsync(Func<LiveTraceDto, LiveTraceDto>? change = null)
    {
        State(s => s with { Version = 4, Samples = 4, SampledBytes = 400, Types = 2, DurationSeconds = 10, DelaySeconds = 0.2 });
        State(change ?? (s => s));
        _live.Follow(TracePath, null);
        await WaitsAsync(1);
        _live.Types.Count.ShouldBe(2);
    }

    /// <summary>Lets the next question be asked and waits for the one after it, or for the end.</summary>
    private async Task TickAsync(bool ends = false)
    {
        var before = Volatile.Read(ref _waits);
        _tick.Release();
        if (ends)
        {
            await _live.FollowTask.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        }
        else
        {
            await WaitsAsync(before + 1);
        }
    }

    /// <summary>A new state, and the question that brings what is shown up to it.</summary>
    private async Task RefreshAsync(Func<LiveTraceDto, LiveTraceDto> change)
    {
        State(change);
        var asked = Service.TypeQueries.Count;
        await TickAsync();
        Service.TypeQueries.Count.ShouldBe(asked + 1);
    }

    private Task WaitsAsync(int count) => WaitUntilAsync(() => Volatile.Read(ref _waits) >= count);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "condition not met in time");
            await Task.Delay(10, Ct);
        }
    }
}
