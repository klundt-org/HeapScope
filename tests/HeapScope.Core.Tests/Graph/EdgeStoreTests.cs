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
using HeapScope.Tests.Shared;

namespace HeapScope.Core.Tests.Graph;

/// <summary>
/// The references written to a temporary file while a graph is built (sprint 4.7, task 10): the same graph as in memory, the
/// file gone with the graph or the builder, and memory used when the file cannot be created or written.
/// </summary>
public sealed class EdgeStoreTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("heapscope-edges-").FullName;

    public void Dispose() => TestFolders.Delete(_folder);

    [Fact]
    public void A_graph_built_with_its_references_in_a_file_is_the_graph_built_in_memory()
    {
        // More references than one write (4 MB), so rows cross the file's chunks, and dependent handles merged into rows.
        using var inMemory = Build(edgeFolder: null);
        var builder = NewBuilder(_folder);
        using var onDisk = builder.Build(TestContext.Current.CancellationToken);

        onDisk.EdgeCount.ShouldBeGreaterThan(EdgeStore.WriteChunk + 1);
        onDisk.EdgeCount.ShouldBe(inMemory.EdgeCount);
        onDisk.EdgeStarts.Span.SequenceEqual(inMemory.EdgeStarts.Span).ShouldBeTrue();
        onDisk.Edges.Span.SequenceEqual(inMemory.Edges.Span).ShouldBeTrue();
        onDisk.GetReferences(0).ToArray().ShouldBe(inMemory.GetReferences(0).ToArray());
        builder.EdgesKept.ShouldStartWith("written to a temporary file and mapped");
        new HeapGraphBuilder().EdgesKept.ShouldBeNull();
    }

    [Fact]
    public void The_file_is_gone_when_the_graph_is_disposed()
    {
        var graph = Build(_folder);
        // Windows deletes the file when its handle closes; elsewhere it has no name once open.
        Files().Length.ShouldBe(OperatingSystem.IsWindows() ? 1 : 0);

        graph.Dispose();

        Files().ShouldBeEmpty();
    }

    [Fact]
    public void A_builder_disposed_before_its_graph_is_built_deletes_the_file()
    {
        var builder = NewBuilder(_folder, rows: 1000);
        builder.EdgesKept.ShouldBe("written to a temporary file");

        builder.Dispose();

        Files().ShouldBeEmpty();
    }

    [Fact]
    public void A_folder_that_cannot_be_made_keeps_the_references_in_memory_and_says_why()
    {
        var notAFolder = Path.Combine(_folder, "file");
        File.WriteAllText(notAFolder, "");
        using var expected = Build(edgeFolder: null, rows: 1000);
        var builder = NewBuilder(Path.Combine(notAFolder, "edges"), rows: 1000);

        using var graph = builder.Build(TestContext.Current.CancellationToken);

        graph.Edges.Span.SequenceEqual(expected.Edges.Span).ShouldBeTrue();
        builder.EdgesKept.ShouldStartWith("in memory: the temporary file in ");
        builder.EdgesKept.ShouldContain("could not be created");
    }

    [Fact]
    public void A_write_that_fails_reads_back_what_was_written_and_keeps_the_rest_in_memory()
    {
        var count = (EdgeStore.WriteChunk * 3) + 17;
        using var store = new EdgeStore(_folder, failWritesAfterBytes: (long)EdgeStore.WriteChunk * sizeof(int));
        for (var i = 0; i < count; i++)
        {
            store.Add(i * 7);
        }

        store.OnDisk.ShouldBeFalse();
        store.Where.ShouldStartWith("in memory: writing the temporary file failed after 4 MB");
        Files().ShouldBeEmpty();

        var column = store.Finish(ManagedColumnAllocator.Instance, out var owner);

        owner.ShouldBeNull();
        column.Length.ShouldBe(count);
        var span = column.Span;
        for (var i = 0; i < count; i++)
        {
            span[i].ShouldBe(i * 7);
        }
    }

    [Fact]
    public void A_graph_without_references_maps_nothing()
    {
        var builder = new HeapGraphBuilder(edgeFolder: _folder);
        var type = builder.Types.GetOrAdd("App.Leaf");
        builder.AddObject(0x1000, type, 24, HeapKind.Gen0);

        using var graph = builder.Build(TestContext.Current.CancellationToken);

        graph.EdgeCount.ShouldBe(0);
        graph.EdgeStarts.Span.ToArray().ShouldBe([0, 0]);
        Files().ShouldBeEmpty();
    }

    private string[] Files() => Directory.GetFiles(_folder, "edges-*");

    private static HeapGraph Build(string? edgeFolder, int rows = 600_000) =>
        NewBuilder(edgeFolder, rows).Build(TestContext.Current.CancellationToken);

    /// <summary>Objects with 0 to 4 references each and a dependent handle from the first to the last, all references added.</summary>
    private static HeapGraphBuilder NewBuilder(string? edgeFolder, int rows = 600_000)
    {
        var builder = new HeapGraphBuilder(edgeFolder: edgeFolder);
        var type = builder.Types.GetOrAdd("App.Node", methodTable: 0x7ff0, baseSize: 24);
        for (var i = 0; i < rows; i++)
        {
            builder.AddObject(0x10000UL + ((ulong)i * 32), type, 24, HeapKind.Gen2);
        }

        builder.FreezeObjects(TestContext.Current.CancellationToken);
        builder.AddDependentHandle(0x10000UL, 0x10000UL + ((ulong)(rows - 1) * 32));
        var targets = new int[4];
        for (var i = 0; i < rows; i++)
        {
            var n = (i * 7) % 5;
            for (var k = 0; k < n; k++)
            {
                targets[k] = (int)(((long)i * 31 + (k * 977)) % rows);
            }

            builder.AddReferenceIndices(i, targets.AsSpan(0, n));
        }

        return builder;
    }
}
