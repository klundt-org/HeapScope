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

using HeapScope.Application.Allocations;
using HeapScope.Application.Jobs;
using HeapScope.Application.Sessions;
using HeapScope.Contracts;
using HeapScope.UI.Services;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>The Allocations page (sprint 2.5): a trace's types, trees and stacks over an interval, narrowed by type.</summary>
public sealed class AllocationsViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));
    private readonly FakeWorkerClient _worker = new();
    private readonly FakeUserInteraction _interaction = new();
    private readonly AllocationsViewModel _page;
    private int _shown;

    public AllocationsViewModelTests()
    {
        Directory.CreateDirectory(_folder);
        var jobs = new JobTracker(TimeProvider.System);
        var session = new AllocationSession(_worker, jobs, new TraceService(), new SessionLibrary(Path.Combine(_folder, "sessions")));
        _page = new AllocationsViewModel(session, new ImmediateUiDispatcher(), _interaction);
        _page.TraceShown += (_, _) => _shown++;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeWorkerService Service => _worker.Service;

    [Fact]
    public async Task Opening_a_trace_shows_its_types_back_traces_stacks_and_timeline()
    {
        _page.IsEmpty.ShouldBeTrue();

        var trace = await OpenAsync();

        _page.IsEmpty.ShouldBeFalse();
        _page.Selected.ShouldBe(trace);
        _shown.ShouldBe(1, "the shell shows the page");
        Service.TracesOpened.ShouldHaveSingleItem().Path.ShouldEndWith("app.nettrace");
        trace.Summary.ShouldContain("App (42)");
        trace.Note.ShouldContain("Estimated from samples");
        trace.Interval.ShouldBe("The whole trace");
        trace.Buckets.Count.ShouldBe(2);
        (await TypesAsync(trace)).ShouldBe(["System.Byte[]", "System.String"]);
        trace.Types.Columns.Single(c => c.Column.Name == AllocationTypeColumns.Bytes).Column.Bound.ShouldBe(ValueBound.Estimate);
        (await TreeAsync(trace)).Select(r => (r.Name, r.Total, r.Own)).ShouldBe([("App.Grow", 300L, (long?)300), ("App.Log", 100L, (long?)100)]);
        Service.AllocationTreeQueries.ShouldHaveSingleItem().Kind.ShouldBe(AllocationTreeKindDto.BackTraces);
        trace.Stacks.Select(s => (s.Rank, s.Top, s.Caller, s.Percent)).ShouldBe([(1, "App.Grow", "App.Handle", 75.0), (2, "App.Log", "App.Main", 25.0)]);
        trace.SelectedStack.ShouldBe(trace.Stacks[0]);
    }

    [Fact]
    public async Task Selecting_a_type_narrows_the_trees_and_stacks_and_all_types_restores_them()
    {
        var trace = await OpenAsync();
        await TypesAsync(trace);

        trace.Types.SelectedRow = trace.Types.Rows.GetRow(1);
        await SettleAsync(trace);

        trace.TypeName.ShouldBe("System.String");
        Service.AllocationTreeQueries[^1].TypeName.ShouldBe("System.String");
        Service.StackQueries[^1].TypeName.ShouldBe("System.String");
        trace.Stacks.Select(s => s.Top).ShouldBe(["App.Log"]);
        Service.TypeQueries.Count.ShouldBe(1, "the types list is what one picks from: it does not narrow itself");

        trace.AllTypesCommand.Execute(null);
        await SettleAsync(trace);

        trace.TypeName.ShouldBeNull();
        Service.StackQueries[^1].TypeName.ShouldBeNull();
        trace.Stacks.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Selecting_an_interval_asks_every_view_about_it()
    {
        var trace = await OpenAsync();

        trace.Selection = new TimeInterval(6, 2);
        await SettleAsync(trace);

        trace.Interval!.ShouldContain("2");
        (Service.TypeQueries[^1].FromSeconds, Service.TypeQueries[^1].ToSeconds).ShouldBe((2.0, (double?)6));
        (Service.AllocationTreeQueries[^1].FromSeconds, Service.AllocationTreeQueries[^1].ToSeconds).ShouldBe((2.0, (double?)6));
        (Service.StackQueries[^1].FromSeconds, Service.StackQueries[^1].ToSeconds).ShouldBe((2.0, (double?)6));

        trace.WholeTraceCommand.Execute(null);
        await SettleAsync(trace);

        trace.Interval.ShouldBe("The whole trace");
        Service.StackQueries[^1].ToSeconds.ShouldBeNull();
    }

    [Fact]
    public async Task Expanding_a_row_shows_its_callers_and_keeps_it_selected()
    {
        var trace = await OpenAsync();
        var grow = (await TreeAsync(trace))[0];

        trace.Tree.ToggleCommand.Execute(grow);
        await trace.Tree.QueryTask;
        await trace.Tree.SelectTask;

        var query = Service.AllocationTreeQueries[^1];
        query.Expanded.ShouldBe(["b:1"]);
        query.RevealKey.ShouldBe("b:1");
        (await TreeAsync(trace)).Select(r => (r.Depth, r.Name)).ShouldBe([(0, "App.Grow"), (1, "App.Handle"), (0, "App.Log")]);
        trace.Tree.SelectedRow!.Key.ShouldBe("b:1");
        trace.Tree.CollapseCommand.CanExecute(trace.Tree.SelectedRow).ShouldBeTrue();
        trace.Tree.ExpandCommand.CanExecute(trace.Tree.SelectedRow).ShouldBeFalse();
    }

    [Fact]
    public async Task A_segment_of_the_chart_opens_its_parents_in_the_tree_and_selects_its_row()
    {
        var trace = await OpenAsync();
        var chart = trace.Tree.ChartSegments;
        chart.Select(s => (s.Level, s.Key)).ShouldBe([(0, "root"), (1, "b:1"), (1, "b:3"), (2, "b:1/2"), (2, "b:3/0")]);
        trace.Tree.ChartTotal.ShouldBe(400);

        trace.Tree.SelectedSegment = chart.Single(s => s.Key == "b:1/2");
        await trace.Tree.QueryTask;
        await trace.Tree.SelectTask;

        var query = Service.AllocationTreeQueries[^1];
        query.Expanded.ShouldBe(["b:1"]);
        query.RevealKey.ShouldBe("b:1/2");
        trace.Tree.SelectedRow!.Key.ShouldBe("b:1/2");
        trace.Tree.SelectedSegment!.Key.ShouldBe("b:1/2");

        // A row selects its segment, and a segment whose row is loaded selects it without asking again.
        var rows = await TreeAsync(trace);
        var queries = Service.AllocationTreeQueries.Count;
        trace.Tree.SelectedRow = rows.Single(r => r.Key == "b:3");
        trace.Tree.SelectedSegment!.Key.ShouldBe("b:3");
        trace.Tree.SelectedSegment = chart.Single(s => s.Key == "b:1");
        trace.Tree.SelectedRow!.Key.ShouldBe("b:1");
        Service.AllocationTreeQueries.Count.ShouldBe(queries);
    }

    [Fact]
    public async Task The_chart_follows_the_trees_interval_type_and_kind_and_its_shape_asks_nothing()
    {
        var trace = await OpenAsync();
        Service.AllocationChartQueries.Count.ShouldBe(1);
        trace.Tree.ChartCaption.ShouldStartWith("The first row is the methods that allocated");

        trace.Selection = new TimeInterval(6, 2);
        await SettleAsync(trace);
        (Service.AllocationChartQueries[^1].FromSeconds, Service.AllocationChartQueries[^1].ToSeconds).ShouldBe((2.0, (double?)6));

        await TypesAsync(trace);
        trace.Types.SelectedRow = trace.Types.Rows.GetRow(1);
        await SettleAsync(trace);
        Service.AllocationChartQueries[^1].TypeName.ShouldBe("System.String");
        trace.Tree.ChartSegments.Select(s => s.Key).ShouldBe(["root", "b:3", "b:3/0"]);

        trace.ShowCallTree = true;
        await SettleAsync(trace);
        Service.AllocationChartQueries[^1].Kind.ShouldBe(AllocationTreeKindDto.CallTree);
        trace.Tree.ChartCaption.ShouldStartWith("Each bar is a method");

        var asked = Service.AllocationChartQueries.Count;
        trace.Tree.ShowsSunburst = true;
        trace.Tree.ChartShape.ShouldBe(ChartShape.Sunburst);
        trace.Tree.ChartCaption.ShouldStartWith("Each arc is a method");
        trace.Tree.ChartInfo.ShouldBe(InfoTexts.AllocationsSunburst);
        Service.AllocationChartQueries.Count.ShouldBe(asked, "the shape asks the worker nothing");
    }

    [Fact]
    public async Task A_segment_whose_row_the_tree_lists_further_down_says_so()
    {
        var trace = await OpenAsync();
        var missing = new IcicleSegmentDto(2, 300, 50, "b:3/9", "App.Elsewhere", 0);
        trace.Tree.ChartSegments = [.. trace.Tree.ChartSegments, missing];

        trace.Tree.SelectedSegment = missing;
        await trace.Tree.QueryTask;

        Service.AllocationTreeQueries[^1].Expanded.ShouldBe(["b:3"]);
        trace.Tree.Note.ShouldNotBeNull().ShouldContain("further down");
        trace.Tree.SelectedRow.ShouldBeNull();
    }

    /// <summary>A tree cut at its row limit keeps its own next step: collapsing rows helps there, a "more" row does not.</summary>
    [Fact]
    public async Task A_segment_missing_from_a_cut_tree_keeps_the_trees_own_note()
    {
        Service.TreeTruncated = true;
        var trace = await OpenAsync();
        var cut = trace.Tree.Note.ShouldNotBeNull();
        cut.ShouldContain("collapse rows");
        var missing = new IcicleSegmentDto(2, 300, 50, "b:3/9", "App.Elsewhere", 0);
        trace.Tree.ChartSegments = [.. trace.Tree.ChartSegments, missing];

        trace.Tree.SelectedSegment = missing;
        await trace.Tree.QueryTask;

        trace.Tree.Note.ShouldBe(cut);
    }

    [Fact]
    public async Task A_chart_that_fails_alone_says_why_and_a_refused_request_leaves_that_to_the_tree()
    {
        Service.ChartFailure = new HeapScope.Application.Workers.WorkerOperationException(HeapScopeErrorCode.InsufficientMemory, "Too little memory.", null);
        var trace = await OpenAsync();

        trace.Tree.ChartSegments.ShouldBeEmpty();
        trace.Tree.ChartStatus.ShouldBe("Too little memory.", "the tree answered, so the chart says why it did not");
        (await TreeAsync(trace)).ShouldNotBeEmpty();

        Service.ChartFailure = new HeapScope.Application.Workers.WorkerOperationException(HeapScopeErrorCode.InvalidArgument, "A trace that is being recorded has no call tree.", null);
        trace.ShowCallTree = true;
        await SettleAsync(trace);
        trace.Tree.ChartStatus.ShouldBeNull("the tree shows the same refusal");
    }

    [Fact]
    public async Task A_chart_with_no_frame_wide_enough_says_so()
    {
        Service.ChartTopOnly = true;
        var trace = await OpenAsync();

        trace.Tree.ChartSegments.ShouldBeEmpty();
        trace.Tree.ChartStatus.ShouldBe(AllocationTreeViewModel.NothingWideEnough);
    }

    [Fact]
    public async Task While_a_chart_is_drawn_the_old_one_is_gone_and_a_row_selected_meanwhile_selects_its_segment()
    {
        var trace = await OpenAsync();
        trace.Tree.ChartSegments.ShouldNotBeEmpty();
        Service.ChartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        trace.ShowCallTree = true;
        await trace.Tree.QueryTask;
        trace.Tree.ChartSegments.ShouldBeEmpty("the back traces' chart cannot be clicked under the call tree");
        trace.Tree.ChartStatus.ShouldBe("Drawing...");
        var main = (await TreeAsync(trace)).Single(r => r.Key == "n:0");
        trace.Tree.SelectedRow = main;
        trace.Tree.SelectedSegment.ShouldBeNull();

        Service.ChartGate.SetResult();
        await trace.Tree.ChartTask;
        trace.Tree.ChartSegments.Select(s => s.Key).ShouldContain("n:0");
        trace.Tree.SelectedSegment.ShouldNotBeNull().Key.ShouldBe("n:0");
        trace.Tree.SelectedRow.ShouldBeSameAs(main);
    }

    [Fact]
    public async Task Closing_a_trace_stops_its_chart()
    {
        Service.ChartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var path = Path.Combine(_folder, "app.nettrace");
        await File.WriteAllBytesAsync(path, [1], Ct);
        await _page.OpenPathAsync(path);
        var trace = _page.Selected.ShouldNotBeNull();
        await trace.LoadTask;
        await trace.Tree.QueryTask;
        var chart = trace.Tree.ChartTask;
        chart.IsCompleted.ShouldBeFalse();

        trace.Close();

        Service.LastChartToken.IsCancellationRequested.ShouldBeTrue("the worker stops laying it out");
        await chart;
        (trace.Tree.ChartSegments.Count, trace.Tree.ChartStatus).ShouldBe((0, (string?)null));
    }

    [Fact]
    public async Task The_call_tree_starts_closed_and_shows_own_bytes()
    {
        var trace = await OpenAsync();
        trace.Tree.ToggleCommand.Execute((await TreeAsync(trace))[0]);
        await trace.Tree.QueryTask;

        trace.ShowCallTree = true;
        await trace.Tree.QueryTask;

        var query = Service.AllocationTreeQueries[^1];
        (query.Kind, query.Expanded.Count).ShouldBe((AllocationTreeKindDto.CallTree, 0));
        (await TreeAsync(trace)).Select(r => (r.Name, r.Total, r.Own)).ShouldBe([("App.Main", 400L, (long?)0)]);
    }

    [Fact]
    public async Task Closing_a_trace_closes_it_in_the_worker_and_opening_it_again_shows_the_open_tab()
    {
        var trace = await OpenAsync();
        await _page.OpenPathAsync(trace.Trace.Path);
        _page.Traces.Count.ShouldBe(1, "a trace already open is shown, not read again");
        Service.TracesOpened.Count.ShouldBe(1);

        await _page.CloseCommand.ExecuteAsync(trace);

        _page.IsEmpty.ShouldBeTrue();
        _page.Selected.ShouldBeNull();
        Service.TracesClosed.ShouldHaveSingleItem().ShouldBe(trace.Trace.Descriptor.TraceId);
    }

    [Fact]
    public async Task A_file_that_is_not_a_trace_says_why_on_the_page()
    {
        Service.OpenFailure = new HeapScope.Application.Workers.WorkerOperationException(HeapScopeErrorCode.SnapshotFormat, "notes.txt is not an EventPipe trace (.nettrace).", null);
        _interaction.NextFile = Path.Combine(_folder, "notes.txt");

        await _page.OpenCommand.ExecuteAsync(null);

        _page.Status.ShouldBe("notes.txt is not an EventPipe trace (.nettrace).");
        _page.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task After_a_worker_restart_one_action_reads_the_trace_once()
    {
        var trace = await OpenAsync();
        _worker.NewGeneration();
        Service.OpenTraceGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Types, tree and stacks all find the trace gone at once.
        trace.Selection = new TimeInterval(1, 4);
        Service.OpenTraceGate.SetResult();
        await SettleAsync(trace);

        Service.TracesOpened.Count.ShouldBe(2, "the first open, then one read again for all three queries");
        trace.StacksStatus.ShouldBeNull();
        trace.Stacks.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_trace_whose_file_changed_is_not_read_again_silently()
    {
        var trace = await OpenAsync();
        _worker.NewGeneration();
        Service.TraceFileBytes = 2000;

        trace.Selection = new TimeInterval(1, 4);
        await SettleAsync(trace);

        trace.StacksStatus!.ShouldContain("changed since it was opened");
        Service.TracesClosed.ShouldHaveSingleItem("the read of the changed file is closed again");
    }

    [Fact]
    public async Task All_types_clears_the_row_so_the_same_type_narrows_again()
    {
        var trace = await OpenAsync();
        await TypesAsync(trace);
        trace.Types.SelectedRow = trace.Types.Rows.GetRow(0);
        await SettleAsync(trace);
        trace.TypeName.ShouldBe("System.Byte[]");

        trace.AllTypesCommand.Execute(null);
        await SettleAsync(trace);
        trace.Types.SelectedRow.ShouldBeNull();
        trace.Types.SelectedRow = trace.Types.Rows.GetRow(0);
        await SettleAsync(trace);

        trace.TypeName.ShouldBe("System.Byte[]");
        Service.StackQueries[^1].TypeName.ShouldBe("System.Byte[]");
    }

    [Fact]
    public async Task A_click_without_a_drag_is_the_whole_trace_and_closing_mid_load_reports_nothing()
    {
        var trace = await OpenAsync();
        var queries = Service.StackQueries.Count;

        trace.Selection = new TimeInterval(3, 3);
        await SettleAsync(trace);

        trace.Selection.ShouldBeNull();
        trace.Interval.ShouldBe("The whole trace");
        Service.StackQueries.Count.ShouldBe(queries, "nothing to ask again");

        Service.OpenTraceGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _worker.NewGeneration();
        trace.Selection = new TimeInterval(1, 2);
        await _page.CloseCommand.ExecuteAsync(trace);
        Service.OpenTraceGate.SetResult();
        await trace.LoadTask;

        trace.StacksStatus.ShouldNotBe("The call stacks failed. Details are in the log.");
    }

    [Fact]
    public void The_timeline_maps_buckets_to_bars_and_pixels_to_seconds()
    {
        AllocationBucketDto[] buckets = [new(0, 300, 3), new(5, 100, 1)];

        var bars = AllocationTimelineGeometry.Bars(buckets, 5, 10, 200, 100);

        bars.Select(b => (b.X, Math.Round(b.Height, 6))).ShouldBe([(0.0, 100.0), (100.0, 33.333333)]);
        bars[1].Y.ShouldBe(100 - bars[1].Height);
        AllocationTimelineGeometry.SecondsAt(50, 200, buckets, 5, 10).ShouldBe(2.5);
        AllocationTimelineGeometry.SecondsAt(-5, 200, buckets, 5, 10).ShouldBe(0);
        AllocationTimelineGeometry.SecondsAt(500, 200, buckets, 5, 10).ShouldBe(10);
        AllocationTimelineGeometry.XOf(5, 200, buckets, 5, 10).ShouldBe(100);
        AllocationTimelineGeometry.Bars([], 5, 10, 200, 100).ShouldBeEmpty();
        new TimeInterval(6, -1).ShouldBe(new TimeInterval(0, 6), "ends in either order, never before the start");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private async Task<AllocationTraceViewModel> OpenAsync()
    {
        var path = Path.Combine(_folder, "app.nettrace");
        await File.WriteAllBytesAsync(path, [1], Ct);
        await _page.OpenPathAsync(path);
        var trace = _page.Selected.ShouldNotBeNull();
        await SettleAsync(trace);
        return trace;
    }

    private static async Task SettleAsync(AllocationTraceViewModel trace)
    {
        await trace.LoadTask;
        await trace.Types.QueryTask;
        await trace.Tree.QueryTask;
        await trace.Tree.ChartTask;
    }

    private static async Task<List<string?>> TypesAsync(AllocationTraceViewModel trace)
    {
        var rows = Enumerable.Range(0, trace.Types.Rows.Count).Select(trace.Types.Rows.GetRow).ToList();
        await trace.Types.Rows.WhenLoadedAsync();
        return [.. rows.Select(r => r[AllocationTypeColumns.Type])];
    }

    private static async Task<List<AllocationRowItem>> TreeAsync(AllocationTraceViewModel trace)
    {
        var rows = Enumerable.Range(0, trace.Tree.Rows.Count).Select(trace.Tree.Rows.GetRow).ToList();
        await trace.Tree.Rows.WhenLoadedAsync();
        return rows;
    }
}
