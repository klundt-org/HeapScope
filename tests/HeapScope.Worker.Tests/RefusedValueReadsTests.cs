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
using HeapScope.Core.Graph;
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

/// <summary>
/// Every view on a full dump whose value reads are refused (sprint 3.12, task 9 and its review H): the graph answers, and
/// each read of field values or strings gives the snapshot's one reason instead of failing. Through the worker's own
/// services, on a snapshot opened from its index cache while its DAC is missing.
/// </summary>
public sealed class RefusedValueReadsTests : IDisposable
{
    private const string Reason =
        "Field values and strings are not read: the DAC (Data Access Component) of this dump's .NET 10.0.14 runtime was not found. Install that runtime or put its DAC in the DAC folder (Symbols:DacFolder, or --dac-folder on the command line), then close the snapshot and open the dump again.";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services;
    private readonly SyntheticObject _holder;
    private readonly SyntheticObject _text;
    private readonly Guid _id;

    public RefusedValueReadsTests()
    {
        Directory.CreateDirectory(_folder);
        var dump = Path.Combine(_folder, "app.dmp");
        File.WriteAllBytes(dump, [.. "MDMP"u8, 0x93, 0xA7, 0, 0, 1, 2, 3, 4]);
        var options = new WorkerOptions("unused", null) { DataFolder = _folder };
        var locator = new IndexCacheLocator(options.Folders.Cache);

        var builder = new SyntheticGraphBuilder();
        _holder = builder.Object("App.Holder", 40);
        _text = builder.Object("System.String", 30, typeKind: TypeKind.String);
        var item = builder.Object("App.Item", 24);
        builder.Reference(_holder, _text).Reference(_holder, item).Root(_holder);
        using (var graph = builder.Build())
        {
            var metadata = new IndexCacheMetadata(
                new SnapshotInfo(SnapshotKind.FullDump, dump),
                SnapshotCapabilities.FullDump,
                IndexCacheMetadata.CurrentWriterVersion,
                LoaderVersion: ClrMdDumpLoader.CurrentVersion);
            IndexCacheWriter.Write(locator.GetCachePath(dump), IndexCacheSigner.None, graph, metadata, SnapshotFingerprint.Compute(dump), cancellationToken: Ct);
        }

        _services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(options)
            .AddSingleton(new ResultSetStore())
            .AddSingleton(locator)
            .AddSingleton(IndexCacheSigner.None)
            .AddSingleton(_ => new SnapshotStore(options, locator, IndexCacheSigner.None, NullLogger<SnapshotStore>.Instance)
            {
                DacCheckForTests = _ => new RuntimeComponentMissingException("10.0.14", "mscordaccore.dll", []),
            })
            .AddSingleton<TraceStore>()
            .AddSingleton<IHostApplicationLifetime, NoLifetime>()
            .AddWorkerRpcTargets()
            .BuildServiceProvider();
        var opened = _services.GetRequiredService<SnapshotStore>().OpenAsync(new OpenSnapshotRequest(dump), null, Ct).GetAwaiter().GetResult();
        opened.WhyUnavailable(SnapshotFeatureIds.FieldValues).ShouldBe(Reason);
        _id = opened.SnapshotId;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HeapQueryHandler Heap => _services.GetRequiredService<HeapQueryHandler>();

    [Fact]
    public async Task Object_details_list_no_fields_and_no_text_and_say_why()
    {
        var details = await Heap.GetObjectAsync(new ObjectRequest(_id, _text.Address), null, Ct);

        details.TypeName.ShouldBe("System.String");
        details.Fields.ShouldBeNull();
        details.Text.ShouldBeNull();
        details.FieldsUnavailable.ShouldBe(Reason);
    }

    [Fact]
    public async Task References_are_listed_from_the_graph()
    {
        var references = await Heap.QueryReferencesAsync(new ReferencesRequest(_id, _holder.Address), null, Ct);

        references.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Referrers_are_listed_and_their_empty_field_column_says_why()
    {
        var referrers = await Heap.QueryReferrersAsync(new ReferrersRequest(_id, _text.Address), null, Ct);

        referrers.Rows.TotalCount.ShouldBe(1);
        referrers.FieldsNote.ShouldBe(Reason);
    }

    [Fact]
    public async Task Paths_to_a_root_are_found_and_their_fields_say_why()
    {
        var paths = await _services.GetRequiredService<RetentionHandler>().QueryObjectPathsAsync(new ObjectPathsRequest(_id, _text.Address), null, Ct);

        paths.Reachable.ShouldBeTrue();
        paths.RootNodeIds.ShouldNotBeEmpty();
        paths.FieldsNote.ShouldBe(Reason);
    }

    [Fact]
    public async Task String_instances_are_listed_without_values_and_a_value_filter_is_refused_with_the_reason()
    {
        var strings = await Heap.QueryInstancesAsync(new InstancesRequest(_id, "System.String", null), null, Ct);

        strings.TotalObjects.ShouldBe(1);
        strings.ValuesAvailable.ShouldBeFalse();

        var filtered = await Should.ThrowAsync<StreamJsonRpc.LocalRpcException>(() => Heap.QueryInstancesAsync(new InstancesRequest(_id, "System.String", null, Filter: "/abc/"), null, Ct));
        filtered.Message.ShouldBe(Reason);
        filtered.ErrorCode.ShouldBe((int)HeapScopeErrorCode.CapabilityNotSupported);
    }

    [Fact]
    public async Task Grouping_by_interface_is_refused_with_the_reason()
    {
        var refused = await Should.ThrowAsync<StreamJsonRpc.LocalRpcException>(() => Heap.QueryTypeSummaryAsync(new TypeSummaryRequest(_id, TypeGrouping.Interface), null, Ct));

        refused.Message.ShouldBe(Reason);
        refused.ErrorCode.ShouldBe((int)HeapScopeErrorCode.CapabilityNotSupported);
        (await Heap.QueryTypeSummaryAsync(new TypeSummaryRequest(_id), null, Ct)).Rows.TotalCount.ShouldBe(3, "the graph answers");
    }

    public void Dispose()
    {
        _services.Dispose();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A mapped cache can outlive the test by a moment; the temp folder is the system's to clean.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
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
