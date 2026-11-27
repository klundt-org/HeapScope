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

using System.Globalization;
using System.IO;
using HeapScope.Application.Workers;
using HeapScope.Contracts;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>The fake worker's AllocationTraces (<see cref="IAllocationTraceService"/>).</summary>
internal sealed partial class FakeWorkerService : IAllocationTraceService
{
    public Task<TraceDescriptor> OpenTraceAsync(OpenTraceRequest request, IProgress<JobProgress>? progress, CancellationToken cancellationToken)
    {
        if (OpenFailure is { } failure)
        {
            return Task.FromException<TraceDescriptor>(failure);
        }

        TracesOpened.Add(request);
        var descriptor = new TraceDescriptor(Guid.NewGuid(), request.Path, 42, "App", DateTimeOffset.UnixEpoch, 10, 4, 400, 2, 0, 0, 0.1, TraceFileBytes, DateTimeOffset.UnixEpoch);
        return OpenTraceGate is { } gate ? gate.Task.ContinueWith(_ => descriptor, TaskScheduler.Default) : Task.FromResult(descriptor);
    }

    /// <summary>What a live trace answers (sprint 4.3); a test changes it as the recording goes on.</summary>
    public LiveTraceDto LiveState { get; set; } = new(0, 42, "App", DateTimeOffset.UnixEpoch, 0, 1, 0, 0, 0, 0, 0, 0, 0, null, true, false, true, null);

    public List<OpenLiveTraceRequest> LiveOpened { get; } = [];

    public List<Guid> LiveEnded { get; } = [];

    /// <summary>When set, the next types query fails with it.</summary>
    public Exception? TypesFailure { get; set; }

    /// <summary>How many more times opening a live trace fails as a worker that stopped.</summary>
    public int LiveOpenCrashes { get; set; }

    public Exception? LiveFailure { get; set; }

    public Task<LiveTraceDescriptor> OpenLiveTraceAsync(OpenLiveTraceRequest request, CancellationToken cancellationToken)
    {
        LiveOpened.Add(request);
        if (LiveOpenCrashes > 0)
        {
            LiveOpenCrashes--;
            return Task.FromException<LiveTraceDescriptor>(new WorkerCrashedException("The worker stopped."));
        }

        return LiveFailure is { } failure ? Task.FromException<LiveTraceDescriptor>(failure) : Task.FromResult(new LiveTraceDescriptor(Guid.NewGuid(), request.Path, LiveState));
    }

    public Task<LiveTraceDto> QueryLiveTraceAsync(LiveTraceRequest request, CancellationToken cancellationToken) =>
        LiveFailure is { } failure ? Task.FromException<LiveTraceDto>(failure) : Task.FromResult(LiveState);

    public Task EndLiveTraceAsync(Guid traceId, CancellationToken cancellationToken)
    {
        LiveEnded.Add(traceId);
        return Task.CompletedTask;
    }

    public Task CloseTraceAsync(Guid traceId, CancellationToken cancellationToken)
    {
        TracesClosed.Add(traceId);
        return Task.CompletedTask;
    }

    public Task<AllocationTimelineResult> QueryAllocationTimelineAsync(AllocationTimelineRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new AllocationTimelineResult([new(0, 300, 3), new(5, 100, 1)], 5, 10));

    public List<IReadOnlyList<string?>> TypeRows { get; set; } = [["System.Byte[]", "300", "3", "75"], ["System.String", "100", "1", "25"]];

    /// <summary>The interval a live trace's queries say they counted.</summary>
    public AllocationIntervalDto? LiveInterval { get; set; }

    /// <summary>When set, the next types query waits for it.</summary>
    public TaskCompletionSource? TypesGate { get; set; }

    public async Task<AllocationTypesResult> QueryAllocationTypesAsync(AllocationTypesRequest request, CancellationToken cancellationToken)
    {
        TypeQueries.Add(request);
        if (TypesGate is { } gate)
        {
            TypesGate = null;
            await gate.Task;
        }

        if (TypesFailure is { } failure)
        {
            TypesFailure = null;
            throw failure;
        }

        var rows = TypeRows;
        var id = Guid.NewGuid();
        _sets[id] = rows;
        return new AllocationTypesResult(new ResultSetInfo(id, rows.Count, AllocationTypeColumnsList), 400, 4, LiveInterval);
    }

    public Task<AllocationStacksResult> QueryAllocationStacksAsync(AllocationStacksRequest request, CancellationToken cancellationToken)
    {
        StackQueries.Add(request);
        AllocationStackDto[] stacks = [new(300, 3, ["App.Grow", "App.Handle", "App.Main"]), new(100, 1, ["App.Log", "App.Main"])];
        var shown = request.TypeName switch
        {
            "System.Byte[]" => stacks[..1],
            "System.String" => stacks[1..],
            _ => stacks,
        };
        return Task.FromResult(new AllocationStacksResult(shown, shown.Sum(s => s.Bytes), shown.Sum(s => s.Samples)));
    }

    public Task<AllocationTreeResult> QueryAllocationTreeAsync(AllocationTreeRequest request, IProgress<JobProgress>? progress, CancellationToken cancellationToken)
    {
        AllocationTreeQueries.Add(request);
        var nodes = (request.Kind == AllocationTreeKindDto.CallTree ? CallTreeNodes : BackTraceNodes)
            .Where(n => request.TypeName is null || n.Type is null || n.Type == request.TypeName)
            .ToArray();
        var expanded = request.Expanded.ToHashSet(StringComparer.Ordinal);
        var rows = new List<IReadOnlyList<string?>>();
        var keys = new List<string>();
        var total = nodes.Where(n => n.Parent == AllocationTreeKeys.Root).Sum(n => n.Total);
        void Emit(string parent, int depth)
        {
            foreach (var node in nodes.Where(n => n.Parent == parent))
            {
                var children = nodes.Count(n => n.Parent == node.Key);
                var open = children > 0 && expanded.Contains(node.Key);
                rows.Add(
                [
                    node.Key, node.Parent, depth.ToString(CultureInfo.InvariantCulture), AllocationRowKinds.Frame, node.Name,
                    node.Total.ToString(CultureInfo.InvariantCulture), node.Own < 0 ? null : node.Own.ToString(CultureInfo.InvariantCulture), "1",
                    (100.0 * node.Total / total).ToString("0.##", CultureInfo.InvariantCulture), children.ToString(CultureInfo.InvariantCulture), open ? "1" : "0",
                ]);
                keys.Add(node.Key);
                if (open)
                {
                    Emit(node.Key, depth + 1);
                }
            }
        }

        Emit(AllocationTreeKeys.Root, 0);
        var id = Guid.NewGuid();
        _sets[id] = rows;
        return Task.FromResult(new AllocationTreeResult(new ResultSetInfo(id, rows.Count, AllocationTreeColumnsList), total, 4, TreeTruncated, request.RevealKey is { } reveal ? keys.IndexOf(reveal) : -1));
    }

    public List<AllocationChartRequest> AllocationChartQueries { get; } = [];

    /// <summary>When set, every tree answers as cut at its row limit.</summary>
    public bool TreeTruncated { get; set; }

    /// <summary>When set, the next chart query fails with it.</summary>
    public Exception? ChartFailure { get; set; }

    /// <summary>When set, chart queries wait for it (tests make a chart arrive late), and end when their token is cancelled.</summary>
    public TaskCompletionSource? ChartGate { get; set; }

    /// <summary>When set, a chart holds its top alone, as when no frame reaches the share.</summary>
    public bool ChartTopOnly { get; set; }

    /// <summary>The token of the last chart query.</summary>
    public CancellationToken LastChartToken { get; private set; }

    /// <summary>The chart of the same nodes as the tree: each level's children side by side inside their parent.</summary>
    public async Task<AllocationChartResult> QueryAllocationChartAsync(AllocationChartRequest request, IProgress<JobProgress>? progress, CancellationToken cancellationToken)
    {
        AllocationChartQueries.Add(request);
        LastChartToken = cancellationToken;
        if (ChartGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        return await Chart(request);
    }

    private Task<AllocationChartResult> Chart(AllocationChartRequest request)
    {
        if (ChartFailure is { } failure)
        {
            ChartFailure = null;
            return Task.FromException<AllocationChartResult>(failure);
        }

        var nodes = (request.Kind == AllocationTreeKindDto.CallTree ? CallTreeNodes : BackTraceNodes)
            .Where(n => request.TypeName is null || n.Type is null || n.Type == request.TypeName)
            .ToArray();
        var total = nodes.Where(n => n.Parent == AllocationTreeKeys.Root).Sum(n => n.Total);
        var segments = new List<IcicleSegmentDto> { new(0, 0, total, AllocationTreeKeys.Root, "All sampled allocations", 0) };
        var level = new List<(string Key, long Offset)> { (AllocationTreeKeys.Root, 0) };
        for (var depth = 1; level.Count > 0; depth++)
        {
            var next = new List<(string Key, long Offset)>();
            foreach (var (key, offset) in level)
            {
                var x = offset;
                foreach (var node in nodes.Where(n => n.Parent == key))
                {
                    segments.Add(new IcicleSegmentDto(depth, x, node.Total, node.Key, node.Name, 0));
                    next.Add((node.Key, x));
                    x += node.Total;
                }
            }

            level = next;
        }

        return Task.FromResult(new AllocationChartResult(ChartTopOnly ? segments[..1] : segments, total, 4));
    }
}
