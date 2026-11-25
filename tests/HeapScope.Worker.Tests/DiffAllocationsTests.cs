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
using HeapScope.Core.Allocations;
using HeapScope.Core.Loading;
using HeapScope.Core.Synthetic;
using HeapScope.Loaders.ClrMd;
using HeapScope.Storage;
using HeapScope.Worker.Rpc;
using HeapScope.Worker.Snapshots;
using HeapScope.Worker.Traces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeapScope.Worker.Tests;

/// <summary>A diff's allocations (sprint 4.4): which traces and intervals are refused, and what of an interval is counted.</summary>
public sealed class DiffAllocationsTests : IDisposable
{
    private static readonly DateTime s_start = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"))).FullName;
    private readonly ServiceProvider _services;
    private readonly Guid _baseline;
    private readonly Guid _current;

    public DiffAllocationsTests()
    {
        var options = new WorkerOptions("unused", null) { DataFolder = _folder };
        var locator = new IndexCacheLocator(options.Folders.Cache);
        _services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(options)
            .AddSingleton(new ResultSetStore())
            .AddSingleton(locator)
            .AddSingleton(IndexCacheSigner.None)
            .AddSingleton(_ => new SnapshotStore(options, locator, IndexCacheSigner.None, NullLogger<SnapshotStore>.Instance))
            .AddSingleton<TraceStore>()
            .AddSingleton<IHostApplicationLifetime, NoLifetime>()
            .AddWorkerRpcTargets()
            .BuildServiceProvider();
        _baseline = Snapshot("before.dmp", 1);
        _current = Snapshot("after.dmp", 3);

        Guid Snapshot(string name, int orders)
        {
            var dump = Path.Combine(_folder, name);
            File.WriteAllBytes(dump, [.. "MDMP"u8, 0x93, 0xA7, 0, 0, (byte)orders]);
            var builder = new SyntheticGraphBuilder();
            for (var i = 0; i < orders; i++)
            {
                builder.Object("App.Order", 40, module: "App.dll", methodTable: 0x10);
            }

            using (var graph = builder.Build())
            {
                var metadata = new IndexCacheMetadata(
                    new SnapshotInfo(SnapshotKind.FullDump, dump, ProcessId: 7),
                    SnapshotCapabilities.FullDump,
                    IndexCacheMetadata.CurrentWriterVersion,
                    LoaderVersion: ClrMdDumpLoader.CurrentVersion);
                IndexCacheWriter.Write(locator.GetCachePath(dump), IndexCacheSigner.None, graph, metadata, SnapshotFingerprint.Compute(dump), cancellationToken: Ct);
            }

            return _services.GetRequiredService<SnapshotStore>().OpenAsync(new OpenSnapshotRequest(dump), null, Ct).GetAwaiter().GetResult().SnapshotId;
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_interval_inside_the_recording_is_counted_and_joined()
    {
        var result = await DiffAsync(Trace(), 2, 8);

        var info = result.Allocations.ShouldNotBeNull();
        (info.FromSeconds, info.ToSeconds, info.AskedFromSeconds, info.AskedToSeconds, info.RecordedSeconds).ShouldBe((2, 8, 2, 8, 10));
        (info.Bytes, info.Samples, info.RowBytes, info.AbsentBytes, info.AbsentTypes, info.NameBytes).ShouldBe((330, 3, 300, 30, 1, 0));
        result.Rows.Columns.Select(c => c.Name).ShouldContain(TypeDiffColumns.AllocatedType);
    }

    /// <summary>What the recording does not cover is left out, and the answer says what was asked for; its last event counts.</summary>
    [Fact]
    public async Task An_interval_past_the_recording_s_ends_counts_what_there_is()
    {
        var info = (await DiffAsync(Trace(), -5, 20)).Allocations.ShouldNotBeNull();

        (info.FromSeconds, info.AskedFromSeconds, info.AskedToSeconds, info.RecordedSeconds).ShouldBe((0, -5, 20, 10));
        info.Bytes.ShouldBe(1_000 + 330 + 5_000, "the tick at the recording's very end included");
    }

    [Theory]
    [InlineData(-9, -4, "The recording started 4.0 s after the later snapshot")]
    [InlineData(14, 20, "The recording ended 4.0 s before the earlier snapshot")]
    public async Task An_interval_outside_the_recording_is_refused(int from, int to, string expected) =>
        (await Should.ThrowAsync<Exception>(() => DiffAsync(Trace(), from, to))).Message.ShouldContain(expected);

    [Fact]
    public async Task A_trace_of_another_process_is_refused_and_says_which_snapshot_differs() =>
        (await Should.ThrowAsync<Exception>(() => DiffAsync(Trace(processId: 8), 2, 8))).Message.ShouldContain("is of process 8, and the baseline is of process 7");

    [Fact]
    public async Task A_trace_that_is_not_open_is_asked_for_again() =>
        (await Should.ThrowAsync<Exception>(() => DiffAsync(Guid.NewGuid(), 2, 8))).Message.ShouldContain("is not open in the analysis worker");

    private Task<TypeDiffResult> DiffAsync(Guid trace, int from, int to) =>
        _services.GetRequiredService<HeapQueryHandler>().QueryTypeDiffAsync(
            new TypeDiffRequest(_baseline, _current, SortColumn: TypeDiffColumns.AllocatedBytes, Allocations: new DiffAllocationScope(trace, new DateTimeOffset(s_start).AddSeconds(from), new DateTimeOffset(s_start).AddSeconds(to))), null, Ct);

    private Guid Trace(int processId = 7) => _services.GetRequiredService<TraceStore>().Add(new OpenTrace("run.nettrace", new AllocationTrace(
        ["F"],
        ["App.Order", "App.Gone"],
        stackFrames: [0],
        stackParents: [-1],
        sampleTimes: [1_000, 2_000, 5_000, 7_999, 10_000],
        sampleStacks: new int[5],
        sampleTypes: [0, 0, 1, 0, 0],
        sampleBytes: [1_000, 200, 30, 100, 5_000],
        sampleKinds: new AllocationKind[5],
        new AllocationTraceInfo(processId, "app", s_start, 10_000, 0, 0),
        methodTables: new AllocationMethodTables([0x10, 0x20], [0, 0, 1, 0, 0]))));

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
