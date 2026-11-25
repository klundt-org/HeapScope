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


using HeapScope.Application;
using HeapScope.Application.Allocations;
using HeapScope.Application.Jobs;
using HeapScope.Application.Presentation;
using HeapScope.Application.Sessions;
using HeapScope.Contracts;
using HeapScope.UI.Services;
using HeapScope.UI.ViewModels;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>Compare's Allocated between (sprint 4.4): the run's trace, the note and the step to the Allocations page.</summary>
public sealed class CompareAllocatedTests : IDisposable
{
    private static readonly DateTimeOffset s_start = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));
    private readonly FakeWorkerClient _worker = new();
    private readonly SessionLibrary _library;
    private readonly AllocationsViewModel _allocations;
    private readonly SnapshotsViewModel _snapshots;
    private int _shown;

    public CompareAllocatedTests()
    {
        var jobs = new JobTracker(TimeProvider.System);
        _library = new SessionLibrary(_root);
        _allocations = new AllocationsViewModel(new AllocationSession(_worker, jobs, new TraceService(), _library), new ImmediateUiDispatcher(), new FakeUserInteraction());
        _allocations.TraceShown += (_, _) => _shown++;
        _snapshots = new SnapshotsViewModel(TestSessions.Create(_worker, jobs), new WorkerOperations(_worker, jobs), new ImmediateUiDispatcher(), new FakeUserInteraction(), allocations: _allocations);
        _worker.Service.CapturedAt = path => Path.GetFileName(path) == "first.dmp" ? s_start : s_start.AddSeconds(10);
        _worker.Service.DiffRows.Add(["App.Order", "", "2", "5", "3", "80", "200", "120", "1", "1", "App.Order", "App.dll"]);
        _worker.Service.DiffRows.Add(["App.Cache", "", "1", "2", "1", "50", "100", "50", "1", "1", "App.Cache", "App.dll"]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CompareViewModel Compare => _snapshots.Compare;

    private FakeWorkerService Service => _worker.Service;

    [Fact]
    public async Task Ticking_it_reads_the_run_s_trace_and_shows_estimates_where_there_are_samples()
    {
        await OpenBothAsync(recorded: true);
        Compare.OffersAllocated.ShouldBeTrue();

        await TickAsync();

        Service.TracesOpened.ShouldHaveSingleItem().Path.ShouldEndWith("run.nettrace");
        var scope = Service.DiffQueries[^1].Allocations.ShouldNotBeNull();
        (scope.FromUtc, scope.ToUtc).ShouldBe((s_start.AddSeconds(1), s_start.AddSeconds(9)));
        Compare.VisibleColumnGroups.ShouldContain(ColumnGroups.Allocated);
        Compare.AllocatedNote!.ShouldStartWith("About ");
        _allocations.Traces.Count.ShouldBe(1, "the trace is on the Allocations page, where it can be closed");
        _shown.ShouldBe(0, "the page is not switched to");
        var rows = await RowsAsync();
        (rows[0].AllocatedBytes, rows[0].AllocatedSamples).ShouldBe((5000L, 2L));
        (rows[1].AllocatedBytes, rows[1].AllocatedSamples).ShouldBe((null, null), "no sample is not zero bytes");

        // A second question takes the open trace.
        Compare.Filter = "order";
        await WaitUntilAsync(() => Service.DiffQueries.Count == 3);
        await Compare.QueryTask;
        Service.TracesOpened.Count.ShouldBe(1);

        Compare.ShowAllocated = false;
        await WaitUntilAsync(() => Service.DiffQueries.Count == 4);
        await Compare.QueryTask;
        (Compare.AllocatedShown, Compare.AllocatedNote).ShouldBe((false, null));
    }

    [Fact]
    public async Task A_row_opens_the_allocations_page_at_the_interval_and_type()
    {
        await OpenBothAsync(recorded: true);
        Compare.ShowWhereAllocatedCommand.Execute(null);
        Compare.Status.ShouldBe(AllocatedBetweenTexts.TickFirst);
        await TickAsync();
        var rows = await RowsAsync();

        Compare.ShowWhereAllocatedCommand.Execute(rows[1]);
        (Compare.Status, _shown).ShouldBe((AllocatedBetweenTexts.NoSample, 0));
        Compare.ShowWhereAllocatedCommand.Execute(rows[0]);

        _shown.ShouldBe(1);
        var tab = _allocations.Selected.ShouldNotBeNull();
        (tab.Selection, tab.TypeName).ShouldBe((new TimeInterval(2, 6), "Traced"));
        await tab.LoadTask;
        (Service.StackQueries[^1].FromSeconds, Service.StackQueries[^1].ToSeconds, Service.StackQueries[^1].TypeName).ShouldBe((2.0, (double?)6, "Traced"));

        // Again, with the interval already chosen: the type is set anew.
        tab.AllTypesCommand.Execute(null);
        Compare.ShowWhereAllocatedCommand.Execute(rows[0]);
        await tab.LoadTask;
        Service.StackQueries[^1].TypeName.ShouldBe("Traced");

        // Review M3: the rows are asked for with the trace, so closing it takes the allocations away, and says so.
        var queries = Service.DiffQueries.Count;
        await _allocations.CloseCommand.ExecuteAsync(tab);
        await WaitUntilAsync(() => Service.DiffQueries.Count == queries + 1);
        await Compare.QueryTask;
        (Compare.ShowAllocated, Compare.AllocatedShown, Compare.AllocatedNote).ShouldBe((false, false, AllocatedBetweenTexts.TraceClosed));
        Service.DiffQueries[^1].Allocations.ShouldBeNull();
        Compare.ShowWhereAllocatedCommand.Execute(rows[0]);
        Compare.Status.ShouldBe(AllocatedBetweenTexts.TraceClosed);

        await TickAsync(queries + 2);
        (Compare.AllocatedShown, Service.TracesOpened.Count).ShouldBe((true, 2));
    }

    /// <summary>Review H2: a question asked again while the trace is read waits for the same read.</summary>
    [Fact]
    public async Task A_trace_is_read_once_however_often_the_question_changes_meanwhile()
    {
        await OpenBothAsync(recorded: true);
        Service.OpenTraceGate = new TaskCompletionSource();
        Compare.ShowAllocated = true;
        await WaitUntilAsync(() => Service.TracesOpened.Count == 1);
        Compare.ShowWhereAllocatedCommand.Execute(null);
        Compare.Status.ShouldBe(AllocatedBetweenTexts.StillReading);

        Compare.SortByCommand.Execute(TypeDiffColumns.Name);
        Compare.ShowAllocated = false;
        Compare.ShowAllocated = true;
        Service.OpenTraceGate.SetResult();
        await WaitUntilAsync(() => Compare.AllocatedShown);

        (Service.TracesOpened.Count, _allocations.Traces.Count).ShouldBe((1, 1));
    }

    /// <summary>A read nobody waits for any more still gets its tab, where it can be closed.</summary>
    [Fact]
    public async Task A_read_that_is_not_waited_for_gets_its_tab()
    {
        await OpenBothAsync(recorded: true);
        Service.OpenTraceGate = new TaskCompletionSource();
        Compare.ShowAllocated = true;
        await WaitUntilAsync(() => Service.TracesOpened.Count == 1);

        Compare.ShowAllocated = false;
        await WaitUntilAsync(() => Service.DiffQueries.Count == 2);
        Service.OpenTraceGate.SetResult();

        await WaitUntilAsync(() => _allocations.Traces.Count == 1);
        Compare.AllocatedShown.ShouldBeFalse();
    }

    /// <summary>The page the step leads to says what it shows more than the row.</summary>
    [Fact]
    public async Task A_nested_type_s_row_says_that_the_page_shows_every_type_of_its_name()
    {
        Service.DiffRows.Clear();
        Service.DiffRows.Add(["App.One+Scope", "", "2", "5", "3", "80", "200", "120", "1", "1", "App.One+Scope", "App.dll"]);
        Service.DiffRows.Add(["App.Cache", "", "1", "2", "1", "50", "100", "50", "1", "1", "App.Cache", "App.dll"]);
        await OpenBothAsync(recorded: true);
        await TickAsync();

        Compare.ShowWhereAllocatedCommand.Execute((await RowsAsync())[0]);

        _allocations.Status.ShouldBe(AllocatedBetweenTexts.EveryOfName("Traced"));
    }

    /// <summary>Review H1: a tab read before the file was complete is not the file's any more.</summary>
    [Fact]
    public async Task A_trace_that_changed_since_it_was_read_is_read_again()
    {
        await OpenBothAsync(recorded: true);
        Service.TraceFileBytes = 1;
        await _allocations.OpenPathAsync(Path.Combine(_library.Current.Folder, "run.nettrace"));
        var early = _allocations.Selected;

        await TickAsync();

        Service.TracesOpened.Count.ShouldBe(2);
        _allocations.Traces.ShouldHaveSingleItem().ShouldNotBe(early);
        Compare.AllocatedShown.ShouldBeTrue();
    }

    [Fact]
    public async Task Closing_a_snapshot_takes_the_note_with_the_rows()
    {
        await OpenBothAsync(recorded: true);
        await TickAsync();

        await _snapshots.CloseCommand.ExecuteAsync(Compare.Current);

        (Compare.AllocatedShown, Compare.AllocatedNote).ShouldBe((false, null));
    }

    [Fact]
    public async Task Without_a_recorded_run_the_note_says_why_and_the_comparison_is_shown()
    {
        await OpenBothAsync(recorded: false);

        await TickAsync();

        (Compare.AllocatedShown, Compare.AllocatedNote).ShouldBe((false, AllocatedBetweenTexts.NoTrace));
        Service.TracesOpened.ShouldBeEmpty();
        Compare.Rows.Count.ShouldBe(2);
        Compare.ShowWhereAllocatedCommand.Execute((await RowsAsync())[0]);
        Compare.Status.ShouldBe(AllocatedBetweenTexts.NoTrace);
    }

    [Fact]
    public async Task A_refused_trace_leaves_the_comparison_without_it()
    {
        Service.AllocatedRefusal = "another process";
        await OpenBothAsync(recorded: true);

        await TickAsync(queries: 3);

        (Compare.AllocatedShown, Compare.AllocatedNote, Compare.Rows.Count).ShouldBe((false, "another process", 2));
        Service.DiffQueries[^1].Allocations.ShouldBeNull();
        Compare.Status.ShouldBeNull();
    }

    [Fact]
    public async Task A_trace_that_cannot_be_read_leaves_the_comparison_without_it()
    {
        await OpenBothAsync(recorded: true);
        Service.OpenFailure = new HeapScope.Application.Workers.WorkerOperationException(HeapScopeErrorCode.SnapshotFormat, "damaged", null);

        await TickAsync();

        (Compare.AllocatedShown, Compare.AllocatedNote, Compare.Rows.Count).ShouldBe((false, "damaged", 2));
    }

    private async Task TickAsync(int queries = 2)
    {
        Compare.ShowAllocated = true;
        await WaitUntilAsync(() => Service.DiffQueries.Count == queries);
        await Compare.QueryTask;
    }

    private async Task<DiffRowItem[]> RowsAsync()
    {
        DiffRowItem[] rows = [Compare.Rows.GetRow(0), Compare.Rows.GetRow(1)];
        await Compare.Rows.WhenLoadedAsync();
        return rows;
    }

    private async Task OpenBothAsync(bool recorded)
    {
        var folder = _library.Current.Folder;
        var trace = Path.Combine(folder, "run.nettrace");
        await File.WriteAllTextAsync(trace, "trace", Ct);
        File.SetLastWriteTimeUtc(trace, DateTime.UnixEpoch);
        Service.TraceFileBytes = new FileInfo(trace).Length;
        foreach (var (name, at) in new[] { ("first.dmp", 1), ("second.dmp", 9) })
        {
            var path = Path.Combine(folder, name);
            await File.WriteAllBytesAsync(path, [1], Ct);
            _library.AddTo(folder, new SessionSnapshot(Guid.NewGuid(), path, "app", 42, "10.0.0", s_start.AddSeconds(at + 1), 1, HeapUtc: s_start.AddSeconds(at)));
            await _snapshots.OpenPathAsync(path);
        }

        _library.RecordRun(folder, new SessionRun(Guid.NewGuid(), "app", 42, 1, "", SnapshotKindDto.FullDump, s_start, s_start.AddSeconds(60), null, recorded ? "run.nettrace" : null, []));
        await Compare.QueryTask;
        Service.DiffQueries.Count.ShouldBe(1);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "condition not met in time");
            await Task.Delay(10, Ct);
        }
    }

    public void Dispose()
    {
        _snapshots.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
