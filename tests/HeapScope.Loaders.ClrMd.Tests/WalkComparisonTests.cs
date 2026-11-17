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

using HeapScope.Core.Graph;
using Microsoft.Diagnostics.Runtime;

namespace HeapScope.Loaders.ClrMd.Tests;

/// <summary>
/// The loader's own object walk (sprint 3.12, R4; the review's M1) against ClrMD's, object by object, on the fixture dump of
/// the system the tests run on: CI runs it on Windows and on Linux (a core dump), where the integration tests do not run.
/// Runs when <c>HEAPSCOPE_FIXTURE_DUMP</c> names a dump of the LeakyConsole fixture; CI requires it there.
/// </summary>
public sealed class WalkComparisonTests
{
    [Fact]
    public void Every_object_of_this_systems_fixture_dump_is_the_one_clrmds_walk_finds()
    {
        var dump = Environment.GetEnvironmentVariable("HEAPSCOPE_FIXTURE_DUMP");
        var reason = "HEAPSCOPE_FIXTURE_DUMP names no dump (set by CI after it takes one).";
        if (string.IsNullOrEmpty(dump) || !File.Exists(dump))
        {
            Assert.False(Environment.GetEnvironmentVariable("HEAPSCOPE_REQUIRE_FIXTURE") == "1", reason);
            Assert.Skip(reason);
        }

        using var graph = new ClrMdDumpLoader().Load(dump!, cancellationToken: TestContext.Current.CancellationToken).Graph;
        using var target = DataTarget.LoadDump(dump!);
        using var runtime = target.ClrVersions[0].CreateRuntime();
        var heap = runtime.Heap;
        var expected = heap.Segments.SelectMany(s => s.EnumerateObjects()).Where(o => !o.IsFree)
            .Select(o => (o.Address, Type: o.Type?.Name ?? string.Empty, Size: o.Size))
            .OrderBy(o => o.Address)
            .ToList();
        var finalizable = heap.EnumerateFinalizableObjects()
            .Where(o => o.Type is { } type && !type.IsFinalizeSuppressed(o.Address))
            .Select(o => o.Address)
            .ToHashSet();

        graph.ObjectCount.ShouldBe(expected.Count);
        var mismatches = new List<string>();
        for (var i = 0; i < expected.Count && mismatches.Count < 10; i++)
        {
            var (address, type, size) = expected[i];
            var actual = (graph.Addresses[i], graph.Types.GetName(graph.TypeIndices[i]), graph.GetSize(i));
            if (actual != (address, type, size))
            {
                mismatches.Add($"#{i}: ClrMD 0x{address:x} {type} {size}, HeapScope 0x{actual.Item1:x} {actual.Item2} {actual.Item3}");
            }

            if (((graph.Flags[i] & ObjectFlags.HasFinalizer) != 0) != finalizable.Contains(address))
            {
                mismatches.Add($"#{i}: 0x{address:x} {type} finalizer flag differs from ClrMD's");
            }
        }

        mismatches.ShouldBeEmpty();
    }
}
