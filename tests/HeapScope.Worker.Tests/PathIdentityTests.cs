// HeapScope
// Copyright (C) 2026 Ray Klundt
// https://github.com/rklundt/HeapScope
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
using HeapScope.Loaders.ClrMd;
using HeapScope.Storage;
using HeapScope.Worker.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeapScope.Worker.Tests;

/// <summary>
/// Sprint 3.12 (B3): one rule for when two paths name the same file: without case on Windows, exactly elsewhere. Before it,
/// the worker answered an open of <c>App.dmp</c> on Linux with the open snapshot of <c>app.dmp</c>, another file.
/// </summary>
public sealed class PathIdentityTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("heapscope-paths-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Paths_differing_only_in_case_are_one_file_where_the_system_ignores_case(bool ignoreCase, bool same)
    {
        PathIdentity.Same(@"C:\dumps\app.dmp", @"C:\dumps\App.dmp", ignoreCase).ShouldBe(same);
        PathIdentity.Same("/dumps/app.dmp", "/dumps/app.dmp", ignoreCase).ShouldBeTrue();
        PathIdentity.Same(null, null, ignoreCase).ShouldBeFalse();
        PathIdentity.Comparer(ignoreCase).Equals("a.dmp", "A.dmp").ShouldBe(same);
        PathIdentity.IsUnder(Path.Combine("root", "Sessions", "x.dmp"), Path.Combine("root", "sessions"), ignoreCase).ShouldBe(same);
    }

    // Sprint 3.12 review M3: Windows and macOS ignore case by default, Linux does not.
    [Fact]
    public void Windows_and_macos_ignore_case()
    {
        PathIdentity.IgnoresCase.ShouldBe(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
    }

    [Fact]
    public void A_folder_whose_name_only_starts_like_another_is_not_inside_it()
    {
        PathIdentity.IsUnder(Path.Combine("root", "sessions-old", "x.dmp"), Path.Combine("root", "sessions")).ShouldBeFalse();
        PathIdentity.IsUnder(Path.Combine("root", "sessions", "x.dmp"), Path.Combine("root", "sessions")).ShouldBeTrue();
    }

    /// <summary>The worker's open (sprint 3.12, B3): run by CI on Linux, where the two names are two files.</summary>
    [Fact]
    public async Task Two_dumps_whose_names_differ_only_in_case_open_as_two_snapshots_where_the_system_tells_them_apart()
    {
        var options = new WorkerOptions("unused", null) { DataFolder = _folder };
        var locator = new IndexCacheLocator(options.Folders.Cache);
        var lower = Write(locator, "app.dmp", 1);
        var upper = Path.Combine(_folder, "App.dmp");
        using var store = new SnapshotStore(options, locator, IndexCacheSigner.None, NullLogger<SnapshotStore>.Instance) { DacCheckForTests = _ => null };

        var first = await store.OpenAsync(new OpenSnapshotRequest(lower), null, Ct);
        if (OperatingSystem.IsWindows())
        {
            // One file: the open snapshot answers.
            (await store.OpenAsync(new OpenSnapshotRequest(upper), null, Ct)).SnapshotId.ShouldBe(first.SnapshotId);
            return;
        }

        Write(locator, "App.dmp", 2);
        var second = await store.OpenAsync(new OpenSnapshotRequest(upper), null, Ct);

        second.SnapshotId.ShouldNotBe(first.SnapshotId);
        second.Path.ShouldBe(upper);
        (first.ObjectCount, second.ObjectCount).ShouldBe((10, 20), "each file's own graph");
    }

    private string Write(IndexCacheLocator locator, string name, int scale)
    {
        var dump = Path.Combine(_folder, name);
        File.WriteAllBytes(dump, [.. "MDMP"u8, 0x93, 0xA7, 0, 0, (byte)scale, 2, 3, 4]);
        using var graph = SyntheticGraphs.Chain(10 * scale);
        var metadata = new IndexCacheMetadata(
            new SnapshotInfo(SnapshotKind.FullDump, dump),
            SnapshotCapabilities.ObjectGraph | SnapshotCapabilities.CompleteGraph,
            IndexCacheMetadata.CurrentWriterVersion,
            LoaderVersion: ClrMdDumpLoader.CurrentVersion);
        IndexCacheWriter.Write(locator.GetCachePath(dump), IndexCacheSigner.None, graph, metadata, SnapshotFingerprint.Compute(dump), cancellationToken: Ct);
        return dump;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A mapped cache can outlive the test by a moment; the temp folder is the system's to clean.
        }
    }
}
