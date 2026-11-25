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

using HeapScope.Application.Allocations;
using HeapScope.Application.Jobs;
using HeapScope.Application.Monitoring;
using HeapScope.Application.Sessions;
using HeapScope.Application.Snapshots;
using HeapScope.Application.Workers;
using HeapScope.Capture;
using HeapScope.Contracts;
using HeapScope.Core;
using HeapScope.Integration.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace HeapScope.Integration.Tests;

/// <summary>
/// One monitoring run of LeakyApi that records its allocations, with two full dumps and two gcdumps and the same work before each but
/// the first (sprint 4.4).
/// </summary>
public sealed class RecordedRun : IAsyncLifetime
{
    private readonly TempFolder _root = new();

    public SessionLibrary Library { get; private set; } = null!;

    public Guid RunId { get; private set; }

    public string Folder { get; private set; } = null!;

    /// <summary>The snapshots in the order they were captured: dump, dump, gcdump, gcdump.</summary>
    public IReadOnlyList<string> Snapshots { get; private set; } = [];

    /// <summary>When each capture was asked for.</summary>
    public IReadOnlyList<DateTimeOffset> Asked { get; private set; } = [];

    public async ValueTask InitializeAsync()
    {
        Library = new SessionLibrary(_root.Path);
        var capture = new CaptureService(new ProcessDiscovery(), new DumpCapturer(), Library, new JobTracker(TimeProvider.System));
        using var api = await FixtureProcess.StartApiAsync("--Leaks:Enabled=all", "--Leaks:ItemsPerRequest=100", "--Leaks:PayloadBytes=2000");
        await api.PostWorkAsync(2, CancellationToken.None);
        var recording = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var run = new MonitorService(capture).Start(
            api.ProcessId,
            new MonitorOptions(TimeSpan.FromSeconds(1), MonitorRules.None, RecordAllocations: true),
            r => r.TraceStarted += (_, _) => recording.TrySetResult());
        await recording.Task.WaitAsync(TimeSpan.FromSeconds(60));
        Folder = run.SessionFolder!;
        var (snapshots, asked) = (new List<string>(), new List<DateTimeOffset>());
        foreach (var kind in new[] { SnapshotKindDto.FullDump, SnapshotKindDto.FullDump, SnapshotKindDto.GcDump, SnapshotKindDto.GcDump })
        {
            if (snapshots.Count > 0)
            {
                await api.PostWorkAsync(60, CancellationToken.None);
            }

            asked.Add(DateTimeOffset.UtcNow);
            snapshots.Add((await capture.StartCapture(api.ProcessId, kind: kind).Result).Path);
        }

        run.Stop();
        await run.Completion;
        (Snapshots, Asked, RunId) = (snapshots, asked, SessionLibrary.TryRead(Folder)!.Document.Runs!.Single().Id);
    }

    public ValueTask DisposeAsync()
    {
        _root.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Sprint 4.4: what was allocated between two snapshots, from a real run through the real worker.</summary>
[Trait("Category", "Integration")]
public sealed class AllocatedBetweenTests(RecordedRun run) : IClassFixture<RecordedRun>, IAsyncDisposable
{
    private readonly TempFolder _scratch = new();
    private readonly ListLoggerProvider _logs = new();
    private WorkerHost? _host;
    private SnapshotSession? _snapshots;
    private AllocationSession? _allocations;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WorkerHost Host => _host ??= new WorkerHost(Options.Create(new WorkerHostOptions { WorkerPath = TestPaths.WorkerExe }), _logs);

    private SnapshotSession Snapshots => _snapshots ??= TestSessions.Create(Host, new JobTracker(TimeProvider.System));

    private AllocationSession Allocations => _allocations ??= new AllocationSession(Host, new JobTracker(TimeProvider.System), new TraceService(), new SessionLibrary(_scratch.File("sessions")));

    [Fact]
    public void A_capture_records_when_its_heap_was_taken()
    {
        var recorded = SessionLibrary.TryRead(run.Folder)!.Document.Snapshots;

        recorded.Count.ShouldBe(4);
        foreach (var (snapshot, asked) in recorded.Zip(run.Asked))
        {
            var heap = snapshot.HeapUtc.ShouldNotBeNull();
            heap.ShouldBeGreaterThan(asked);
            heap.ShouldBeLessThanOrEqualTo(snapshot.CapturedUtc);
        }
    }

    /// <summary>The same work between each two snapshots allocates the same, and between the dumps the heap keeps it.</summary>
    [Fact]
    public async Task The_same_work_allocates_the_same_whatever_the_snapshots_are()
    {
        var allocated = new List<long>();
        for (var from = 0; from < 3; from++)
        {
            var (diff, info) = await DiffAsync(from, from + 1);

            info.AskedFromSeconds.ShouldBeGreaterThanOrEqualTo(0, "the recording had started");
            info.AskedToSeconds.ShouldBeLessThanOrEqualTo(info.RecordedSeconds);
            (info.RowBytes + info.AbsentBytes + info.SharedNameBytes).ShouldBe(info.Bytes);
            info.RowBytes.ShouldBeGreaterThan(info.Bytes * 8 / 10, "most sampled bytes are of types with objects");
            diff.Rows.Info.Columns.Single(c => c.Name == TypeDiffColumns.AllocatedBytes).Bound.ShouldBe(ValueBound.Estimate);
            var rows = await RetainedSizesTests.AllRowsAsync(diff.Rows);
            rows.Sum(r => Long(r[TypeDiffColumns.AllocatedBytes])).ShouldBe(info.RowBytes);
            rows.ShouldContain(r => r[TypeDiffColumns.AllocatedBytes] == null && r[TypeDiffColumns.AllocatedSamples] == null, "a row without a sample says nothing");
            var bytes = rows.Single(r => r[TypeDiffColumns.Name] == "System.Byte[]");
            allocated.Add(Long(bytes[TypeDiffColumns.AllocatedBytes]));
            if (from == 0)
            {
                var gained = Long(bytes[TypeDiffColumns.BytesDelta]);
                gained.ShouldBeGreaterThan(5_000_000);
                ((double)allocated[0] / gained).ShouldBeInRange(0.8, 1.8, $"gained {gained}, allocated {allocated[0]}");
            }
        }

        allocated.ShouldAllBe(a => a > allocated[0] * 0.8 && a < allocated[0] * 1.25, string.Join(", ", allocated));
    }

    /// <summary>Two nested types of one innermost name are told apart by their method tables.</summary>
    [Fact]
    public async Task Full_dumps_are_joined_by_method_table_and_nested_types_keep_their_rows()
    {
        var (diff, info) = await DiffAsync(0, 1);
        var rows = await RetainedSizesTests.AllRowsAsync(diff.Rows);

        info.SharedNameBytes.ShouldBe(0);
        rows.ShouldContain(r => r[TypeDiffColumns.AllocatedBytes] != null && r[TypeDiffColumns.Name]!.Contains('+'));

        // Both dumps hold every type of the interval under the table the ticks name, and the trace spells it as the dump does.
        var line = _logs.Records.Last(r => r.Message.Contains("Allocations between", StringComparison.Ordinal)).Message;
        line.ShouldContain("by name 0 bytes");
        line.ShouldContain("; 0 method tables with another name in the trace");
    }

    [Fact]
    public async Task An_interval_is_the_one_the_allocations_page_shows_for_it()
    {
        var (diff, info) = await DiffAsync(0, 1);
        var trace = Allocations.Traces.ShouldHaveSingleItem();

        var types = await Allocations.QueryTypesAsync(trace, new AllocationScope(info.FromSeconds, info.ToSeconds), Ct);

        (types.Result.SampledBytes, types.Result.Samples).ShouldBe((info.Bytes, info.Samples));
        diff.Result.Allocations.ShouldBe(info);
    }

    [Fact]
    public async Task The_rows_come_back_after_the_worker_restarts()
    {
        var (diff, _) = await DiffAsync(0, 1);
        var before = (await RetainedSizesTests.AllRowsAsync(diff.Rows)).Select(r => (r[TypeDiffColumns.Name], r[TypeDiffColumns.AllocatedBytes])).ToList();

        await Host.RestartAsync(Ct);

        (await RetainedSizesTests.AllRowsAsync(diff.Rows)).Select(r => (r[TypeDiffColumns.Name], r[TypeDiffColumns.AllocatedBytes])).ShouldBe(before);
    }

    [Fact]
    public async Task Other_groupings_hold_the_same_bytes()
    {
        var (byType, info) = await DiffAsync(2, 3);
        var (byNamespace, _) = await DiffAsync(2, 3, TypeGrouping.Namespace);

        (await RetainedSizesTests.AllRowsAsync(byNamespace.Rows)).Sum(r => Long(r[TypeDiffColumns.AllocatedBytes])).ShouldBe(info.RowBytes);
        (await RetainedSizesTests.AllRowsAsync(byType.Rows)).Sum(r => Long(r[TypeDiffColumns.AllocatedBytes])).ShouldBe(info.RowBytes);
    }

    [Fact]
    public async Task The_command_line_finds_the_trace_and_says_where_to_look()
    {
        using var diff = ChildProcess.Start(TestPaths.CliExe, "diff", run.Snapshots[0], run.Snapshots[1], "--allocated", "--sort", "allocated", "--top", "3", "--format", "json");
        (await diff.WaitForExitAsync(TimeSpan.FromMinutes(2))).ShouldBeTrue();

        diff.Process.ExitCode.ShouldBe(0, diff.Output);
        using var json = System.Text.Json.JsonDocument.Parse(diff.StandardOutput);
        var counted = json.RootElement.GetProperty("Allocations");
        counted.GetProperty("Bytes").GetInt64().ShouldBeGreaterThan(5_000_000);
        json.RootElement.GetProperty("Rows")[0].GetProperty("Name").GetString().ShouldBe("System.Byte[]");
        diff.Output.ShouldContain("Where: allocations ");

        using var none = ChildProcess.Start(TestPaths.CliExe, "diff", run.Snapshots[0], _scratch.File("elsewhere.dmp"), "--allocated");
        (await none.WaitForExitAsync(TimeSpan.FromMinutes(2))).ShouldBeTrue();
        none.Process.ExitCode.ShouldBe(1);
    }

    [Fact]
    public void The_run_of_two_snapshots_is_found_in_either_order()
    {
        var found = AllocatedBetween.Find(run.Snapshots[3], run.Snapshots[0]);

        found.Reason.ShouldBeNull();
        found.RunId.ShouldBe(run.RunId);
        found.Late.ShouldBeFalse();
        found.FromUtc.ShouldBeLessThan(found.ToUtc);
        File.Exists(found.TracePath).ShouldBeTrue();
    }

    private async Task<(SnapshotDiff Diff, DiffAllocationInfo Info)> DiffAsync(int from, int to, TypeGrouping grouping = TypeGrouping.Type)
    {
        var found = AllocatedBetween.Find(run.Snapshots[from], run.Snapshots[to]);
        found.Reason.ShouldBeNull();
        var baseline = await Snapshots.StartOpen(run.Snapshots[from]).Result;
        var current = await Snapshots.StartOpen(run.Snapshots[to]).Result;
        var trace = await Allocations.OpenOnceAsync(found.TracePath!);
        var diff = await Snapshots.QueryTypeDiffAsync(
            baseline, current, grouping, null, TypeDiffColumns.AllocatedBytes, true, null, Ct, allocations: new DiffAllocations(Allocations, trace, found.FromUtc, found.ToUtc));
        return (diff, diff.Result.Allocations.ShouldNotBeNull());
    }

    private static long Long(string? cell) => cell is null ? 0 : long.Parse(cell, System.Globalization.CultureInfo.InvariantCulture);

    public async ValueTask DisposeAsync()
    {
        if (_host is { } host)
        {
            await host.DisposeAsync();
        }

        _scratch.Dispose();
    }
}
