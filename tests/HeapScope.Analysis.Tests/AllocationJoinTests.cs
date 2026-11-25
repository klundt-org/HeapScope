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


using HeapScope.Analysis.Allocations;
using HeapScope.Core.Allocations;
using HeapScope.Core.Graph;
using HeapScope.Core.Synthetic;

namespace HeapScope.Analysis.Tests;

/// <summary>Sampled allocations joined to a diff's rows (sprint 4.4).</summary>
public sealed class AllocationJoinTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Types_of_an_interval_are_told_apart_by_name_and_method_table()
    {
        var types = AllocationJoin.Types(Trace(), new AllocationFilter(10, 40), Ct);

        types.ShouldBe([new AllocatedType("Scope", 0x20, 300, 1), new AllocatedType("App.Order", 0x10, 200, 1), new AllocatedType("Scope", 0x21, 50, 1)]);
    }

    [Fact]
    public void A_trace_without_method_tables_has_one_entry_per_name()
    {
        var trace = new AllocationTrace(["F"], ["T"], [0], [-1], [0, 1], [0, 0], [0, 0], [10, 20], [AllocationKind.Small, AllocationKind.Small], new AllocationTraceInfo(1, null, DateTime.UnixEpoch, 10, 0, 0));

        AllocationJoin.Types(trace, AllocationFilter.All, Ct).ShouldBe([new AllocatedType("T", 0, 30, 2)]);
    }

    [Fact]
    public void Full_dumps_join_by_method_table_whatever_the_names_and_once_when_a_type_is_in_both()
    {
        var (before, after) = (new SyntheticGraphBuilder(), new SyntheticGraphBuilder());
        before.Object("A.One+Scope", 40, module: "A.dll", methodTable: 0x20);
        before.Object("App.Old", 40, module: "A.dll", methodTable: 0x30);
        after.Object("A.One+Scope", 40, module: "A.dll", methodTable: 0x20);
        after.Object("B.Two+Scope", 40, module: "B.dll", methodTable: 0x21);

        var (rows, totals) = Diff(before, after, [new("Scope", 0x20, 300, 3), new("Scope", 0x21, 50, 1), new("App.Old", 0x30, 20, 1), new("Scope", 0x22, 7, 1)]);

        rows["A.One+Scope"].ShouldBe((300, 3));
        rows["B.Two+Scope"].ShouldBe((50, 1));
        rows["App.Old"].ShouldBe((20, 1), "a type the current snapshot lost takes its bytes in the baseline");
        (totals.Bytes, totals.ByMethodTable, totals.ByName, totals.Absent, totals.AbsentTypes, totals.OtherNames).ShouldBe((377, 370, 0, 7, 1, 0));
        totals.TopAbsent.ShouldHaveSingleItem().MethodTable.ShouldBe(0x22UL, "a table no dump has is absent, however the name is spelled");
    }

    [Fact]
    public void A_gcdump_joins_by_name_and_a_name_of_two_modules_goes_to_no_row()
    {
        var (before, after) = (new SyntheticGraphBuilder(), new SyntheticGraphBuilder());
        before.Object("Entry<System.Int32, App.X>[]", 40, module: "System.Private.CoreLib.dll");
        before.Object("Scope", 40, module: "A.dll");
        after.Object("Entry<System.Int32, App.X>[]", 40, module: "System.Private.CoreLib.dll");
        after.Object("Scope", 40, module: "B.dll");

        var (rows, totals) = Diff(before, after, [new("Entry[System.Int32,App.X][]", 0x20, 300, 3), new("Scope", 0x21, 50, 1), new("Gone", 0x22, 7, 1)]);

        rows["Entry<System.Int32, App.X>[]"].ShouldBe((300, 3));
        rows.Where(r => r.Key.StartsWith("Scope", StringComparison.Ordinal)).ShouldAllBe(r => r.Value.Bytes == null);
        (totals.Bytes, totals.ByMethodTable, totals.ByName, totals.SharedName, totals.Absent, totals.Samples).ShouldBe((357, 0, 300, 50, 7, 5));
    }

    [Fact]
    public void A_full_dump_and_a_gcdump_join_to_the_row_both_share()
    {
        var (before, after) = (new SyntheticGraphBuilder(), new SyntheticGraphBuilder());
        before.Object("App.Cache+Entry", 40, module: "App.dll", methodTable: 0x20);
        after.Object("Entry", 40, module: "App.dll");
        after.Object("Fresh", 40, module: "App.dll");

        var (rows, totals) = Diff(before, after, [new("Entry", 0x20, 300, 3), new("Fresh", 0x21, 50, 1)], TypeNameForms.AsGcDumpName);

        rows["Entry"].ShouldBe((300, 3));
        rows["Fresh"].ShouldBe((50, 1));
        (totals.ByMethodTable, totals.ByName).ShouldBe((300, 50));
    }

    [Fact]
    public void A_trace_refuses_a_method_table_that_is_not_there()
    {
        Should.Throw<ArgumentException>(() => new AllocationTrace(
            ["A"], ["T"], [0], [-1], [0], [0], [0], [1], [AllocationKind.Small],
            new AllocationTraceInfo(1, null, DateTime.UnixEpoch, 10, 0, 0), methodTables: new AllocationMethodTables([0x10], [1]))).Message.ShouldContain("method table");
    }

    private static AllocationTrace Trace() => new(
        ["F"],
        ["App.Order", "Scope"],
        stackFrames: [0],
        stackParents: [-1],
        sampleTimes: [0, 10, 20, 30, 40],
        sampleStacks: [0, 0, 0, 0, 0],
        sampleTypes: [0, 0, 1, 1, 0],
        sampleBytes: [1000, 200, 300, 50, 1000],
        sampleKinds: new AllocationKind[5],
        new AllocationTraceInfo(1, null, DateTime.UnixEpoch, 50, 0, 0),
        methodTables: new AllocationMethodTables([0x10, 0x20, 0x21], [0, 0, 1, 2, 0]));

    private static (Dictionary<string, (long? Bytes, int? Samples)> Rows, AllocationJoinTotals Totals) Diff(
        SyntheticGraphBuilder before, SyntheticGraphBuilder after, AllocatedType[] allocated, Func<string, string>? baselineNames = null)
    {
        using var baseline = before.Build();
        using var current = after.Build();
        var baselineTypes = TypeAggregation.Compute(baseline, cancellationToken: Ct);
        var currentTypes = TypeAggregation.Compute(current, cancellationToken: Ct);
        var join = AllocationJoin.Join(allocated, baseline, baselineTypes, current, currentTypes, Ct);
        var rows = TypeDiff.Build(baseline, baselineTypes, current, currentTypes, GroupKey.Type, baselineNames, extras: new DiffExtras { Allocated = join });
        return (rows.ToDictionary(r => r.Name, r => (r.AllocatedBytes, r.AllocatedSamples)), join.Totals);
    }
}
