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


using System.Text.RegularExpressions;
using Fixtures.Leaks;
using HeapScope.Core.Graph;
using HeapScope.Core.Loading;
using HeapScope.Integration.Tests.Infrastructure;
using HeapScope.Loaders.ClrMd;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Runtime;

namespace HeapScope.Integration.Tests;

/// <summary>
/// The GC handles of a process whose heap count changed (peer review 4.5), against the counts the process itself says it made: with
/// Server GC and DATAS the runtime's own walk leaves out those of the heaps that are gone.
/// </summary>
[Collection(nameof(HeavyTestsCollection))]
[Trait("Category", "Integration")]
[Trait("Run", "Heavy")]
public sealed class HandleTablesTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("1", "1", "0", false)]
    [InlineData("1", "0", "2", false)]
    [InlineData("1", "0", "2", true)]
    [InlineData("0", "0", "0", false)]
    public async Task Every_handle_is_a_root_whatever_the_heap_count_did(string server, string datas, string heaps, bool processorsUnknown)
    {
        using var folder = new TempFolder();
        var dump = folder.File("handles.dmp");
        int expected;
        using (var process = ChildProcess.Start(
            TestPaths.LeakyConsoleExe,
            new Dictionary<string, string> { ["DOTNET_gcServer"] = server, ["DOTNET_GCDynamicAdaptationMode"] = datas, ["DOTNET_GCHeapCount"] = heaps },
            "--leak", "none", "--handles-under-load", "100", "--parent-pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        {
            (await process.WaitForOutputAsync("READY", TimeSpan.FromMinutes(2))).ShouldBeTrue(process.Output);
            expected = int.Parse(Regex.Match(process.Output, @"HANDLES (\d+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            new DiagnosticsClient(process.Process.Id).WriteDump(DumpType.Full, dump, logDumpGeneration: false);
        }

        var result = new ClrMdDumpLoader(new ClrMdLoaderOptions { IgnoreProcessorsForTests = processorsUnknown }).Load(dump, cancellationToken: Ct);
        using var graph = result.Graph;
        var walk = result.Diagnostics.Handles.ShouldNotBeNull();
        output.WriteLine($"{walk}");
        walk.Outcome.ShouldBe(server == "1" && walk.Added > 0 ? HandleWalkOutcome.Recovered : HandleWalkOutcome.Complete);

        Rooted(graph, RootKind.StrongHandle, typeof(HandleHeld).FullName!).ShouldBe(expected);
        Rooted(graph, RootKind.StrongHandle, typeof(HandleKey).FullName!).ShouldBe(expected);
        Rooted(graph, RootKind.PinnedHandle, typeof(HandlePinned).FullName + "[]").ShouldBe(expected);

        // A value is held by its dependent handle only: an edge from its key.
        GraphQueries.CountTypes(graph)[typeof(HandleValue).FullName!].ShouldBe(expected);
        var reachable = HeapScope.Analysis.Retention.Reachability.ComputeFromPathRoots(graph, Ct);
        Enumerable.Range(0, graph.ObjectCount)
            .Count(o => graph.Types.GetName(graph.TypeIndices[o]) == typeof(HandleValue).FullName && reachable.IsReachable(o))
            .ShouldBe(expected);

        if (heaps == "2" && Environment.ProcessorCount > 2)
        {
            // Handles of more threads than tables walked: the case is real on this machine.
            walk.Added.ShouldBeGreaterThan(0);
        }

        // The whole point: the walk as the runtime does it has fewer when the heaps changed.
        using var plain = DumpAnalysis.Open(dump);
        var own = plain.Runtime.EnumerateHandles().Count(h => h.HandleKind == ClrHandleKind.Strong && h.Object.Type?.Name == typeof(HandleHeld).FullName);
        output.WriteLine($"the runtime's own walk has {own} of {expected}");
        (own < expected).ShouldBe(walk.Outcome == HandleWalkOutcome.Recovered);
    }

    /// <summary>What is said of a walk: whole only when it ended where the tables do, past the processors.</summary>
    [Theory]
    [InlineData(24, 24, 5, false, HandleWalkOutcome.Recovered)]
    [InlineData(25, 24, 0, false, HandleWalkOutcome.Complete)]
    [InlineData(18, 24, 5, false, HandleWalkOutcome.Unknown)]
    [InlineData(25, null, 5, false, HandleWalkOutcome.Recovered)]
    [InlineData(25, null, 0, false, HandleWalkOutcome.Complete)]
    [InlineData(25, null, 5, true, HandleWalkOutcome.Unknown)]
    public void A_walk_that_stopped_before_the_last_table_is_not_complete(int tables, int? processors, int added, bool doubt, HandleWalkOutcome expected) =>
        HandleTables.Judge(tables, processors, added, doubt).ShouldBe(expected);

    /// <summary>The search on a made-up process of <paramref name="tables"/> tables, two handles in each past its 4 heaps.</summary>
    [Theory]
    [InlineData(24, 24)]
    [InlineData(24, null)]
    [InlineData(80, 48)]
    [InlineData(4, 4)]
    [InlineData(4, null)]
    [InlineData(1, null)]
    [InlineData(16, 24)]
    public void The_search_ends_at_the_last_table(int tables, int? processors)
    {
        var heaps = Math.Min(4, tables);
        var (best, works, doubt) = HandleTables.Search(heaps, processors, count => count <= tables ? Works((count - heaps) * 2) : new(HandleTables.AttemptOutcome.End, HandleTables.Found.None));

        (best.LeftOut.Count, works, doubt).ShouldBe(((tables - heaps) * 2, tables, false));
    }

    [Fact]
    public void A_walk_that_fails_otherwise_than_past_the_tables_leaves_a_doubt()
    {
        var (best, works, doubt) = HandleTables.Search(4, null, count => count <= 7 ? Works(count) : new(HandleTables.AttemptOutcome.Doubt, HandleTables.Found.None));
        (best.LeftOut.Count, works, doubt).ShouldBe((7, 7, true));

        // A larger count with fewer handles is not a better one.
        (_, works, doubt) = HandleTables.Search(4, null, count => Works(count <= 7 ? count : 1));
        (works, doubt).ShouldBe((7, true));
    }

    private static HandleTables.Attempt Works(int handles) =>
        new(HandleTables.AttemptOutcome.Works, new HandleTables.Found([.. Enumerable.Range(1, handles).Select(static i => new LeftOutHandle((ulong)i, ClrHandleKind.Strong, 0, 0, true))], 0));

    private static int Rooted(HeapGraph graph, RootKind kind, string type) =>
        Enumerable.Range(0, graph.Roots.Count)
            .Select(r => graph.Roots.Roots[r])
            .Where(r => r.Kind == kind && graph.Types.GetName(graph.TypeIndices[r.ObjectIndex]) == type)
            .Select(r => r.ObjectIndex)
            .Distinct()
            .Count();
}
