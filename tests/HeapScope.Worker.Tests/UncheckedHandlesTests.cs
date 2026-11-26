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
using HeapScope.Core.Loading;
using HeapScope.Core.Synthetic;
using HeapScope.Loaders.ClrMd;
using HeapScope.Storage;
using HeapScope.Worker.Rpc;
using HeapScope.Worker.Snapshots;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeapScope.Worker.Tests;

/// <summary>
/// A snapshot whose GC handles could not all be checked (peer review 4.5), or whose statics could not be read (sprint 4.7): its note, and
/// what rests on reachable objects.
/// </summary>
public sealed class UncheckedHandlesTests : IDisposable
{
    private readonly string _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"))).FullName;
    private readonly ServiceProvider _services;
    private readonly IndexCacheLocator _locator;

    public UncheckedHandlesTests()
    {
        var options = new WorkerOptions("unused", null) { DataFolder = _folder };
        _locator = new IndexCacheLocator(options.Folders.Cache);
        _services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(options)
            .AddSingleton(new ResultSetStore())
            .AddSingleton(_locator)
            .AddSingleton(IndexCacheSigner.None)
            .AddSingleton(_ => new SnapshotStore(options, _locator, IndexCacheSigner.None, NullLogger<SnapshotStore>.Instance))
            .AddSingleton<IHostApplicationLifetime, NoLifetime>()
            .AddWorkerRpcTargets()
            .BuildServiceProvider();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task It_says_so_and_what_to_do()
    {
        var known = await OpenAsync("known.dmp", handlesChecked: true);
        var unknown = await OpenAsync("unknown.dmp", handlesChecked: false);

        known.Notes.ShouldNotContain(n => n.Contains("GC handles", StringComparison.Ordinal));
        unknown.Notes.ShouldContain(n => n.Contains("could not check all GC handles", StringComparison.Ordinal) && n.Contains("capture a gcdump", StringComparison.Ordinal));
        known.IsAvailable(SnapshotFeatureIds.UnreachableSet).ShouldBeTrue();
        unknown.IsAvailable(SnapshotFeatureIds.UnreachableSet).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true, false, "the baseline before.dmp")]
    [InlineData(false, true, "this snapshot")]
    [InlineData(false, false, "this snapshot")]
    public async Task Growth_with_a_side_that_lacks_handles_is_refused(bool current, bool baseline, string side) =>
        (await Should.ThrowAsync<RequestRefusedException>(() => GrowthAsync(current, baseline))).Message.ShouldContain("GC handles could be checked for " + side);

    [Fact]
    public async Task Growth_between_two_that_have_them_all_is_measured() =>
        (await GrowthAsync(current: true, baseline: true))?.ShouldNotContain("GC handles");

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_detector_over_reachable_objects_says_what_it_may_not_count(bool handlesChecked, bool noted)
    {
        var snapshot = await OpenAsync("one.dmp", handlesChecked);
        var (result, _, _, lease, _) = await _services.GetRequiredService<InspectionRunner>().RunAsync(new InspectionRequest(snapshot.SnapshotId, "type-growth"), null, Ct);
        lease.Dispose();

        result.Note.ShouldNotBeNull().Contains("may not all be counted", StringComparison.Ordinal).ShouldBe(noted);
    }

    // Sprint 4.7: a heap dump of .NET 8 leaves out its statics, which the loader says by leaving out StaticsChecked; everything that rests on the
    // roots says so as it does for handles.
    [Fact]
    public async Task A_dump_whose_statics_could_not_be_read_says_so_and_what_to_do()
    {
        var unread = await OpenAsync("statics.dmp", handlesChecked: true, less: SnapshotFacts.StaticsChecked);

        unread.Notes.ShouldContain(n => n.Contains("could not read the runtime's own static fields in this dump", StringComparison.Ordinal) && n.Contains("dotnet-dump collect --type Full", StringComparison.Ordinal));
        unread.IsAvailable(SnapshotFeatureIds.UnreachableSet).ShouldBeFalse();
        var (result, _, _, lease, _) = await _services.GetRequiredService<InspectionRunner>().RunAsync(new InspectionRequest(unread.SnapshotId, "type-growth"), null, Ct);
        lease.Dispose();
        result.Note.ShouldNotBeNull().ShouldContain("The static fields of this dump could not be checked (see its notes), so objects held only by a static field may not all be counted.");

        // The detector that finds collections by their static fields is refused there, not answered clean (the review of 4.7, H5).
        (await Should.ThrowAsync<Exception>(() => _services.GetRequiredService<InspectionRunner>().RunAsync(new InspectionRequest(unread.SnapshotId, "growing-static-collections"), null, Ct)))
            .Message.ShouldContain("finds collections by the static fields that hold them, and the static fields of this dump could not be checked");

        var earlier = await OpenAsync("statics-before.dmp", handlesChecked: true);
        (await Should.ThrowAsync<RequestRefusedException>(() => _services.GetRequiredService<InspectionRunner>().RunAsync(new InspectionRequest(unread.SnapshotId, "type-growth", BaselineSnapshotId: earlier.SnapshotId), null, Ct)))
            .Message.ShouldContain("the static fields of this snapshot could not be checked (see its notes), so objects held only by a static field could be counted on one side only. Choose no baseline.");
    }

    private async Task<string?> GrowthAsync(bool current, bool baseline)
    {
        var earlier = await OpenAsync("before.dmp", baseline);
        var later = await OpenAsync("after.dmp", current);
        var (result, _, _, lease, _) = await _services.GetRequiredService<InspectionRunner>().RunAsync(new InspectionRequest(later.SnapshotId, "type-growth", BaselineSnapshotId: earlier.SnapshotId), null, Ct);
        lease.Dispose();
        return result.Note;
    }

    private async Task<SnapshotDescriptor> OpenAsync(string name, bool handlesChecked, SnapshotFacts less = SnapshotFacts.None)
    {
        var dump = Path.Combine(_folder, name);
        File.WriteAllBytes(dump, [.. "MDMP"u8, 0x93, 0xA7, 0, 0, (byte)name.Length]);
        var builder = new SyntheticGraphBuilder();
        builder.Object("App.Order", 40, module: "App.dll", methodTable: 0x10);
        using (var graph = builder.Build())
        {
            var metadata = new IndexCacheMetadata(
                new SnapshotInfo(SnapshotKind.FullDump, dump, ProcessId: 7, Facts: ((handlesChecked ? SnapshotFacts.HandlesChecked : SnapshotFacts.None) | SnapshotFacts.StaticsChecked) & ~less),
                SnapshotCapabilities.FullDump,
                IndexCacheMetadata.CurrentWriterVersion,
                LoaderVersion: ClrMdDumpLoader.CurrentVersion,
                Handles: new HandleWalk(handlesChecked ? HandleWalkOutcome.Complete : HandleWalkOutcome.Unknown, 4, 4, 8, 0, "made up"));
            IndexCacheWriter.Write(_locator.GetCachePath(dump), IndexCacheSigner.None, graph, metadata, SnapshotFingerprint.Compute(dump), cancellationToken: Ct);
        }

        return await _services.GetRequiredService<SnapshotStore>().OpenAsync(new OpenSnapshotRequest(dump), null, Ct);
    }

    public void Dispose()
    {
        _services.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private sealed class NoLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
