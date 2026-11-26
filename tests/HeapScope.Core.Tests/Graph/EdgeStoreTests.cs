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
/// The references written to a temporary file while a graph is built (sprint 4.7, task 10): the same graph as in memory; the
/// file released with the graph or the builder on every path; and memory used, once, when the folder lacks space or the file
/// cannot be created, written or mapped. Off Windows the file has no name once open, so "released" is checked there by the
/// process's open files and mappings (Linux; on macOS it is not seen, so not checked).
/// </summary>
public sealed class EdgeStoreTests : IDisposable
{
    private const int Rows = 600_000;

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
        builder.EdgesKept.ShouldStartWith("written to a temporary file and mapped (");
        builder.EdgesKept.ShouldContain(OperatingSystem.IsWindows() ? Path.Combine(_folder, "edges-") : "unnamed in " + _folder);
        new HeapGraphBuilder().EdgesKept.ShouldBeNull();
    }

    [Fact]
    public void The_file_is_released_when_the_graph_is_disposed()
    {
        var graph = Build(_folder);
        Held().ShouldBe(HeldWhileOpen);

        graph.Dispose();

        Held().ShouldBe(0);
        Should.Throw<ObjectDisposedException>(() => graph.Edges.Span.Length);
    }

    [Fact]
    public void A_builder_disposed_before_its_graph_is_built_releases_the_file()
    {
        var builder = NewBuilder(_folder, rows: 1000);
        builder.EdgesKept.ShouldBe("written to a temporary file");
        Held().ShouldBe(HeldWhileOpen);

        builder.Dispose();

        Held().ShouldBe(0);
    }

    [Fact]
    public void An_out_of_order_row_is_refused_and_disposing_the_builder_still_releases_the_file()
    {
        var builder = NewBuilder(_folder, rows: 1000, addReferences: false);
        builder.AddReferenceIndices(0, [1, 2]);
        builder.AddReferenceIndices(5, [3]);
        Should.Throw<InvalidOperationException>(() => builder.AddReferenceIndices(4, [1]));

        builder.Dispose();

        Held().ShouldBe(0);
    }

    [Fact]
    public void A_build_canceled_after_its_references_were_mapped_releases_the_file_when_disposed()
    {
        var builder = NewBuilder(_folder, rows: 1000);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        // Every row is added, so the build reaches the mapping before it checks the token.
        Should.Throw<OperationCanceledException>(() => builder.Build(canceled.Token));
        builder.EdgesKept.ShouldStartWith("written to a temporary file and mapped (");
        builder.Dispose();

        Held().ShouldBe(0);
    }

    [Fact]
    public void A_folder_that_cannot_be_made_keeps_the_references_in_memory_and_says_why()
    {
        var notAFolder = Path.Combine(_folder, "file");
        File.WriteAllText(notAFolder, "");
        using var expected = Build(edgeFolder: null, rows: 1000);
        var told = new List<string>();
        var builder = NewBuilder(Path.Combine(notAFolder, "edges"), rows: 1000, told.Add);

        using var graph = builder.Build(TestContext.Current.CancellationToken);

        graph.Edges.Span.SequenceEqual(expected.Edges.Span).ShouldBeTrue();
        builder.EdgesKept.ShouldStartWith("in memory: the temporary file in ");
        builder.EdgesKept.ShouldContain("could not be created");
        told.ShouldBe([builder.EdgesKept + ". Check that the folder is the user's own and can be written; the next load tries it again"], "said when it happens, so a load that fails later still says so");
    }

    [Fact]
    public void A_folder_without_the_space_the_file_may_need_keeps_the_references_in_memory()
    {
        var told = new List<string>();
        using var store = new EdgeStore(_folder, objectCount: 100_000_000, told.Add, new EdgeStoreHooks(FreeBytes: 1L << 30));

        store.Writing.ShouldBeFalse();
        store.Where.ShouldBe("in memory: 1,024.0 MB free in " + _folder + ", and the temporary file needs up to 1,525.9 MB beside 1,024.0 MB kept free");
        told.ShouldBe([store.Where + ". Free space on that drive for the next load"]);
        Held().ShouldBe(0);
    }

    [Fact]
    public void A_write_that_fails_keeps_what_was_written_and_reads_it_straight_into_the_column()
    {
        var count = (EdgeStore.WriteChunk * 3) + 17;
        var told = new List<string>();
        using var store = new EdgeStore(_folder, count, told.Add, new EdgeStoreHooks(FailWritesAfterBytes: (long)EdgeStore.WriteChunk * sizeof(int)));
        for (var i = 0; i < count; i++)
        {
            store.Add(i * 7);
        }

        store.Writing.ShouldBeFalse();
        store.Where.ShouldStartWith("in memory from reference 1,048,576: writing the temporary file in " + _folder + " failed after 4.0 MB (IOException: ");
        told.ShouldBe([store.Where + ". Free space on that drive for the next load"]);

        var column = store.Finish(ManagedColumnAllocator.Instance, out var owner);

        owner.ShouldBeNull();
        ShouldCount(column, count);
        Held().ShouldBe(0);
    }

    [Fact]
    public void A_last_write_that_fails_in_the_finish_keeps_every_reference()
    {
        var count = EdgeStore.WriteChunk + 5;
        using var store = new EdgeStore(_folder, count, hooks: new EdgeStoreHooks(FailWritesAfterBytes: (long)EdgeStore.WriteChunk * sizeof(int)));
        for (var i = 0; i < count; i++)
        {
            store.Add(i * 7);
        }

        store.Writing.ShouldBeTrue("the first chunk was written; the last is written only at the finish");

        var column = store.Finish(ManagedColumnAllocator.Instance, out var owner);

        owner.ShouldBeNull();
        store.Where.ShouldStartWith("in memory from reference 1,048,576: writing the temporary file");
        ShouldCount(column, count);
        Held().ShouldBe(0);
    }

    [Fact]
    public void A_mapping_that_fails_reads_the_file_into_the_column()
    {
        var count = (EdgeStore.WriteChunk * 2) + 3;
        var told = new List<string>();
        // A small read back slice, so the read crosses several (a real one is 64 MB: more than 2 GiB cannot be read as bytes at once).
        using var store = new EdgeStore(_folder, count, told.Add, new EdgeStoreHooks(FailMapping: true, ReadSlice: 300_001));
        for (var i = 0; i < count; i++)
        {
            store.Add(i * 7);
        }

        var column = store.Finish(ManagedColumnAllocator.Instance, out var owner);

        owner.ShouldBeNull();
        store.Where.ShouldStartWith("in memory: the temporary file of 8.0 MB in " + _folder + " could not be mapped (IOException: ");
        told.ShouldBe([store.Where + ". Nothing to do unless it happens again; then report it with this log"]);
        ShouldCount(column, count);
        Held().ShouldBe(0);
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
        builder.EdgesKept.ShouldBe("none (no references)");
        Held().ShouldBe(0);
    }

    [Fact]
    public void The_folder_it_makes_is_the_users_alone()
    {
        var folder = Path.Combine(_folder, "edges");
        using var store = new EdgeStore(folder, objectCount: 10);

        store.Writing.ShouldBeTrue();
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(folder).ShouldBe(PrivateFiles.FolderMode);
        }
    }

    /// <summary>What <see cref="Held"/> sees of one open file: on macOS an unnamed file is not seen at all.</summary>
    private static int HeldWhileOpen => OperatingSystem.IsWindows() || OperatingSystem.IsLinux() ? 1 : 0;

    private static void ShouldCount(Memory<int> column, int count)
    {
        column.Length.ShouldBe(count);
        var span = column.Span;
        for (var i = 0; i < count; i++)
        {
            if (span[i] != i * 7)
            {
                span[i].ShouldBe(i * 7, $"reference {i}");
            }
        }
    }

    /// <summary>
    /// The temporary files this process still holds in the test's folder: on Windows those listed (a file is deleted when its last
    /// handle closes); on Linux the open files and mappings of an unnamed file there; elsewhere those listed, which is always none.
    /// </summary>
    private int Held()
    {
        if (!OperatingSystem.IsLinux())
        {
            return Directory.GetFiles(_folder, "edges-*", SearchOption.AllDirectories).Length;
        }

        var open = Directory.GetFiles("/proc/self/fd")
            .Select(fd => { try { return new FileInfo(fd).LinkTarget; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; } })
            .Count(target => target is not null && target.StartsWith(_folder, StringComparison.Ordinal) && target.Contains("edges-", StringComparison.Ordinal));
        var mapped = File.ReadLines("/proc/self/maps").Count(line => line.Contains(_folder, StringComparison.Ordinal) && line.Contains("edges-", StringComparison.Ordinal));
        return Math.Max(open, mapped);
    }

    private static HeapGraph Build(string? edgeFolder, int rows = Rows) =>
        NewBuilder(edgeFolder, rows).Build(TestContext.Current.CancellationToken);

    /// <summary>Objects with 0 to 4 references each and a dependent handle from the first to the last; references added unless asked.</summary>
    private static HeapGraphBuilder NewBuilder(string? edgeFolder, int rows = Rows, Action<string>? keptInMemory = null, bool addReferences = true)
    {
        var builder = new HeapGraphBuilder(edgeFolder: edgeFolder, edgesKeptInMemory: keptInMemory);
        var type = builder.Types.GetOrAdd("App.Node", methodTable: 0x7ff0, baseSize: 24);
        for (var i = 0; i < rows; i++)
        {
            builder.AddObject(0x10000UL + ((ulong)i * 32), type, 24, HeapKind.Gen2);
        }

        builder.FreezeObjects(TestContext.Current.CancellationToken);
        builder.AddDependentHandle(0x10000UL, 0x10000UL + ((ulong)(rows - 1) * 32));
        if (!addReferences)
        {
            return builder;
        }

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
