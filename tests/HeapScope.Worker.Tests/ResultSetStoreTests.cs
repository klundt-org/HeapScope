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

using HeapScope.Contracts;
using HeapScope.Core;
using HeapScope.Core.Synthetic;
using HeapScope.Worker.Rpc;
using StreamJsonRpc;

namespace HeapScope.Worker.Tests;

public class ResultSetStoreTests
{
    [Fact]
    public void Pages_are_formatted_on_demand_and_capped()
    {
        var store = new ResultSetStore();
        var info = store.Add(null, new CountingSource(25_000));

        var page = store.TryGetPage(info.Id, 10, 3)!;
        page.Rows.Select(r => r[0]).ShouldBe(["10", "11", "12"]);
        page.TotalCount.ShouldBe(25_000);
        store.TryGetPage(info.Id, 0, 50_000)!.Rows.Count.ShouldBe(WorkerProtocol.MaxPageSize);
        store.TryGetPage(info.Id, 30_000, 5)!.Rows.Count.ShouldBe(0);
    }

    [Fact]
    public void Count_limit_evicts_the_least_recently_used_set_and_disposes_its_owner()
    {
        var store = new ResultSetStore(capacity: 2);
        var firstOwner = new Owner();
        var first = store.Add(null, new CountingSource(1), firstOwner);
        var second = store.Add(null, new CountingSource(1));
        store.TryGetPage(first.Id, 0, 1).ShouldNotBeNull();

        store.Add(null, new CountingSource(1));

        store.TryGetPage(second.Id, 0, 1).ShouldBeNull();
        store.TryGetPage(first.Id, 0, 1).ShouldNotBeNull();
        firstOwner.Disposed.ShouldBeFalse();
    }

    [Fact]
    public void Byte_budget_evicts_older_sets_but_keeps_the_newest()
    {
        var store = new ResultSetStore(capacity: 100, byteBudget: 1_000);
        var ownerA = new Owner();
        var a = store.Add(null, new CountingSource(1, bytes: 600), ownerA);
        var b = store.Add(null, new CountingSource(1, bytes: 600));

        store.TryGetPage(a.Id, 0, 1).ShouldBeNull();
        ownerA.Disposed.ShouldBeTrue();
        store.EstimatedBytes.ShouldBe(600);

        var huge = store.Add(null, new CountingSource(1, bytes: 5_000));
        store.TryGetPage(b.Id, 0, 1).ShouldBeNull();
        store.TryGetPage(huge.Id, 0, 1).ShouldNotBeNull();
        store.Count.ShouldBe(1);
    }

    [Fact]
    public void Release_frees_bytes_and_disposes_the_owner()
    {
        var store = new ResultSetStore();
        var owner = new Owner();
        var info = store.Add(null, new CountingSource(3, bytes: 300), owner);

        store.Release(info.Id);
        store.Release(info.Id);

        owner.Disposed.ShouldBeTrue();
        store.EstimatedBytes.ShouldBe(0);
        store.TryGetPage(info.Id, 0, 1).ShouldBeNull();
    }

    [Fact]
    public void The_shown_snapshots_sets_survive_the_others_queries()
    {
        // Three snapshots open (ADR-044): the client shows a; b's and c's queries fill the store, and a's sets stay.
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var store = new ResultSetStore(capacity: 4);
        store.Show(a);
        var aSets = new[] { store.Add(a, new CountingSource(1)), store.Add(a, new CountingSource(1)) };
        for (var i = 0; i < 10; i++)
        {
            store.Add(i % 2 == 0 ? b : c, new CountingSource(1));
        }

        store.ShownSnapshot.ShouldBe(a);
        store.Count.ShouldBe(4);
        aSets.ShouldAllBe(set => store.TryGetPage(set.Id, 0, 1) != null);
    }

    [Fact]
    public void Eviction_takes_the_hidden_snapshots_sets_first_least_recently_used_then_the_rest()
    {
        var (shown, hidden) = (Guid.NewGuid(), Guid.NewGuid());
        var store = new ResultSetStore(capacity: 3);
        store.Show(shown);
        var mine = store.Add(shown, new CountingSource(1));
        var trace = store.Add(null, new CountingSource(1));
        var older = store.Add(hidden, new CountingSource(1));

        // Over the cap: the hidden snapshot's set goes, although the shown one's and the trace's are older.
        var newer = store.Add(hidden, new CountingSource(1));
        store.TryGetPage(older.Id, 0, 1).ShouldBeNull();
        store.TryGetPage(mine.Id, 0, 1).ShouldNotBeNull();
        store.TryGetPage(trace.Id, 0, 1).ShouldNotBeNull();

        // The newest set always stays; with no hidden set left but it, the least recently used of the rest goes.
        store.Add(shown, new CountingSource(1));
        store.TryGetPage(newer.Id, 0, 1).ShouldBeNull("the hidden set goes before any other");
        var last = store.Add(hidden, new CountingSource(1));
        store.TryGetPage(last.Id, 0, 1).ShouldNotBeNull("the newest set is kept");
        store.Count.ShouldBe(3);
    }

    [Fact]
    public void Closing_a_snapshot_drops_its_sets_and_returns_their_leases()
    {
        var (closed, open) = (Guid.NewGuid(), Guid.NewGuid());
        var store = new ResultSetStore();
        var lease = new Owner();
        var gone = store.Add(closed, new CountingSource(1), lease);
        var kept = store.Add(open, new CountingSource(1));
        var trace = store.Add(null, new CountingSource(1));

        store.ReleaseSnapshot(closed).ShouldBe(1);

        lease.Disposed.ShouldBeTrue("the lease no longer keeps the closed snapshot's graph");
        store.TryGetPage(gone.Id, 0, 1).ShouldBeNull();
        store.TryGetPage(kept.Id, 0, 1).ShouldNotBeNull();
        store.TryGetPage(trace.Id, 0, 1).ShouldNotBeNull();
        store.ReleaseSnapshot(closed).ShouldBe(0);
    }

    [Fact]
    public void Two_workspaces_of_inspection_sets_fit_the_default_cap()
    {
        // About 20 sets per workspace after an inspections run: two of them stay within the default count cap.
        var (shown, hidden) = (Guid.NewGuid(), Guid.NewGuid());
        var store = new ResultSetStore();
        store.Show(shown);
        var hiddenSets = Enumerable.Range(0, 20).Select(_ => store.Add(hidden, new CountingSource(1))).ToList();
        for (var i = 0; i < 20; i++)
        {
            store.Add(shown, new CountingSource(1));
        }

        hiddenSets.ShouldAllBe(set => store.TryGetPage(set.Id, 0, 1) != null);
    }

    [Fact]
    public void Eviction_takes_other_snapshots_then_the_shown_snapshots_other_tabs()
    {
        // ADR-045: a set of the shown snapshot belongs to the tab shown when it was added. The shown tab's own set is older
        // than the other tab's, so least recently used order alone would take it.
        var (snapshot, other, first, second) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var store = new ResultSetStore(capacity: 3);
        store.Show(snapshot, second);
        var secondsOlder = store.Add(snapshot, new CountingSource(1));
        store.Show(snapshot, first);
        var firstsNewer = store.Add(snapshot, new CountingSource(1));
        var others = store.Add(other, new CountingSource(1));
        store.Show(snapshot, second);

        store.Add(snapshot, new CountingSource(1));
        store.TryGetPage(others.Id, 0, 1).ShouldBeNull("another snapshot's set goes first");

        store.Add(snapshot, new CountingSource(1));
        store.TryGetPage(firstsNewer.Id, 0, 1).ShouldBeNull("then another tab's, though newer");
        store.TryGetPage(secondsOlder.Id, 0, 1).ShouldNotBeNull("the shown tab's own stays, though older");
        store.ShownScope.ShouldBe(second);

        store.Show(null, second);
        store.ShownScope.ShouldBeNull("no tab without a snapshot");
    }

    [Fact]
    public void What_the_tabs_share_stays_with_the_snapshot_across_a_tab_switch()
    {
        // The inspections run in the first tab; the second tab is shown; over the cap, the first tab's own set goes, not theirs.
        var (snapshot, first, second) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var store = new ResultSetStore(capacity: 3);
        store.Show(snapshot, first);
        var shared = store.Add(snapshot, new CountingSource(1), perTab: false);
        var firstTabs = store.Add(snapshot, new CountingSource(1));
        store.Show(snapshot, second);
        store.Add(snapshot, new CountingSource(1));

        store.Add(snapshot, new CountingSource(1));

        store.TryGetPage(firstTabs.Id, 0, 1).ShouldBeNull("the other tab's set goes");
        store.TryGetPage(shared.Id, 0, 1).ShouldNotBeNull("the shared inspections stay, though older");
    }

    [Fact]
    public void With_no_snapshot_shown_eviction_is_least_recently_used()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var store = new ResultSetStore(capacity: 2);
        var first = store.Add(a, new CountingSource(1));
        store.Add(b, new CountingSource(1));
        store.Add(b, new CountingSource(1));

        store.ShownSnapshot.ShouldBeNull();
        store.TryGetPage(first.Id, 0, 1).ShouldBeNull();
    }

    [Fact]
    public void Type_summary_is_sorted_by_size_and_does_not_need_the_graph_afterwards()
    {
        var builder = new SyntheticGraphBuilder();
        builder.Object("Small", 24);
        builder.Object("Big", 1000);
        builder.Object("Small", 24);
        TypeSummarySource source;
        using (var graph = builder.Build())
        {
            source = TypeSummarySource.Create(graph, TestContext.Current.CancellationToken);
        }

        var cells = new string?[source.Columns.Count];
        source.Count.ShouldBe(2);
        source.FormatRow(0, cells);
        cells.Take(4).ShouldBe(["Big", "1", "1000", "95.42"]);
        source.FormatRow(1, cells);
        cells.Take(4).ShouldBe(["Small", "2", "48", "4.58"]);
        source.EstimatedBytes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Domain_exceptions_map_to_error_codes()
    {
        WorkerErrors.CodeOf(new CapabilityNotSupportedException(SnapshotCapabilities.FieldValues)).ShouldBe(HeapScopeErrorCode.CapabilityNotSupported);
        // Sprint 3.12 (B5): a refusal the worker means is InvalidArgument; any other ArgumentException is a bug, without a code.
        WorkerErrors.CodeOf(new HeapScope.Core.RequestRefusedException("x")).ShouldBe(HeapScopeErrorCode.InvalidArgument);
        WorkerErrors.CodeOf(new ArgumentOutOfRangeException("x")).ShouldBeNull();
        WorkerErrors.CodeOf(new InvalidOperationException()).ShouldBeNull();

        var ex = await Should.ThrowAsync<LocalRpcException>(() => WorkerErrors.GuardAsync<int>(
            () => throw new CapabilityNotSupportedException(SnapshotCapabilities.StringContents)));
        ex.ErrorCode.ShouldBe((int)HeapScopeErrorCode.CapabilityNotSupported);
        await Should.ThrowAsync<OperationCanceledException>(() => WorkerErrors.GuardAsync<int>(() => throw new OperationCanceledException()));
    }

    [Fact]
    public async Task A_set_released_while_a_page_is_formatted_keeps_its_owner_until_the_page_is_done()
    {
        var store = new ResultSetStore();
        var owner = new Owner();
        using var formatting = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        var info = store.Add(null, new BlockingSource(formatting, proceed), owner);

        var page = Task.Run(() => store.TryGetPage(info.Id, 0, 1), TestContext.Current.CancellationToken);
        formatting.Wait(TestContext.Current.CancellationToken);
        store.Release(info.Id);

        owner.Disposed.ShouldBeFalse("the page still reads the snapshot the owner keeps alive");
        proceed.Set();
        (await page).ShouldNotBeNull().Rows.Count.ShouldBe(1);
        owner.Disposed.ShouldBeTrue("the last reader disposes the owner of a released set");
        store.TryGetPage(info.Id, 0, 1).ShouldBeNull();
    }

    /// <summary>Signals when it starts formatting and waits to be let go, like a source reading the dump.</summary>
    private sealed class BlockingSource(ManualResetEventSlim formatting, ManualResetEventSlim proceed) : IResultSource
    {
        public IReadOnlyList<ResultColumn> Columns { get; } = [new("Index", ResultColumnKind.Integer)];

        public int Count => 1;

        public long EstimatedBytes => 100;

        public void FormatRow(int index, string?[] cells)
        {
            formatting.Set();
            proceed.Wait(TimeSpan.FromSeconds(30));
            cells[0] = "0";
        }
    }

    private sealed class CountingSource(int count, long bytes = 100) : IResultSource
    {
        public IReadOnlyList<ResultColumn> Columns { get; } = [new("Index", ResultColumnKind.Integer)];

        public int Count => count;

        public long EstimatedBytes => bytes;

        public void FormatRow(int index, string?[] cells) => cells[0] = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class Owner : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
