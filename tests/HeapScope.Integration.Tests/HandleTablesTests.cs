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
    [InlineData("1", "1", "0")]
    [InlineData("1", "0", "2")]
    [InlineData("1", "0", "1")]
    [InlineData("0", "0", "0")]
    public async Task Every_handle_is_a_root_whatever_the_heap_count_did(string server, string datas, string heaps)
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

        // The walk as the runtime does it, and then as from a dump that does not record its processors (a core file).
        int own;
        using (var plain = DumpAnalysis.Open(dump))
        {
            own = plain.Runtime.EnumerateHandles().Count(h => h.HandleKind == ClrHandleKind.Strong && h.Object.Type?.Name == typeof(HandleHeld).FullName);
        }

        output.WriteLine($"the runtime's own walk has {own} of {expected}");
        bool[] known = server == "1" && heaps == "2" ? [true, false] : [true];
        foreach (var processorsKnown in known)
        {
            CheckLoaded(dump, processorsKnown, expected, own, server, heaps);
        }

        if (heaps == "2")
        {
            // Turned off, nothing is said to be whole.
            var off = new ClrMdDumpLoader(new ClrMdLoaderOptions { SecondHandleWalk = false }).Load(dump, cancellationToken: Ct);
            using var graph = off.Graph;
            off.Diagnostics.Handles!.Outcome.ShouldBe(HandleWalkOutcome.Unknown);
            off.Info.Facts.HasFlag(HeapScope.Core.SnapshotFacts.HandlesChecked).ShouldBeFalse();
        }
    }

    private void CheckLoaded(string dump, bool processorsKnown, int expected, int own, string server, string heaps)
    {
        var result = new ClrMdDumpLoader(new ClrMdLoaderOptions { IgnoreProcessorsForTests = !processorsKnown }).Load(dump, cancellationToken: Ct);
        using var graph = result.Graph;
        var walk = result.Diagnostics.Handles.ShouldNotBeNull();
        output.WriteLine($"{walk}");
        walk.Outcome.ShouldBe(server == "1" && walk.Added > 0 ? HandleWalkOutcome.Recovered : HandleWalkOutcome.Complete, walk.How);
        result.Info.Facts.HasFlag(HeapScope.Core.SnapshotFacts.HandlesChecked).ShouldBeTrue();
        (walk.Processors is null).ShouldBe(!processorsKnown || !OperatingSystem.IsWindows());

        Rooted(graph, RootKind.StrongHandle, typeof(HandleHeld).FullName!).ShouldBe(expected);
        Rooted(graph, RootKind.StrongHandle, typeof(HandleKey).FullName!).ShouldBe(expected);
        Rooted(graph, RootKind.PinnedHandle, typeof(HandlePinned).FullName + "[]").ShouldBe(expected);

        // A value is held by its dependent handle only: an edge from its key.
        GraphQueries.CountTypes(graph)[typeof(HandleValue).FullName!].ShouldBe(expected);
        var reachable = HeapScope.Analysis.Retention.Reachability.ComputeFromPathRoots(graph, Ct);
        Enumerable.Range(0, graph.ObjectCount)
            .Count(o => graph.Types.GetName(graph.TypeIndices[o]) == typeof(HandleValue).FullName && reachable.IsReachable(o))
            .ShouldBe(expected);

        if (server == "1" && heaps != "0" && Environment.ProcessorCount > 2)
        {
            // Handles of more threads than tables walked: the case is real on this machine.
            walk.Added.ShouldBeGreaterThan(0);
        }

        // Handles the runtime's own walk lacks are only ever found by the second one.
        (own < expected && walk.Outcome != HandleWalkOutcome.Recovered).ShouldBeFalse();
    }

    private static int Rooted(HeapGraph graph, RootKind kind, string type) =>
        Enumerable.Range(0, graph.Roots.Count)
            .Select(r => graph.Roots.Roots[r])
            .Where(r => r.Kind == kind && graph.Types.GetName(graph.TypeIndices[r.ObjectIndex]) == type)
            .Select(r => r.ObjectIndex)
            .Distinct()
            .Count();
}
