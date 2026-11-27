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

using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HeapScope.Application.Allocations;
using HeapScope.Application.Workers;
using HeapScope.Contracts;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// The chart of the back traces or call tree shown (ADR-046): an icicle or a sunburst of the same segments, queried with the tree for
/// the same interval, type and kind. Its segments carry the tree's keys, so a segment opens its parents in the tree and selects its
/// row, and a selected row selects its segment. It has no focus of its own: the tree's rows already narrow by type and interval.
/// </summary>
public sealed partial class AllocationTreeViewModel
{
    /// <summary>What the chart says when no frame is wide enough to draw.</summary>
    public const string NothingWideEnough = "No frame reaches 0.1 percent of the sampled bytes; the tree below lists them all.";

    private CancellationTokenSource? _chart;
    private bool _syncingSelection;

    // The row a segment asked for, so its answer can say when the tree lists it further down than its first rows.
    private string? _chartReveal;

    // Whether the tree's last answer was cut at its row limit: its note then says what to do, not the chart's.
    private bool _cut;

    [ObservableProperty]
    private IReadOnlyList<IcicleSegmentDto> _chartSegments = [];

    [ObservableProperty]
    private long _chartTotal;

    [ObservableProperty]
    private IcicleSegmentDto? _selectedSegment;

    [ObservableProperty]
    private string? _chartStatus;

    /// <summary>How the chart is drawn; the view keeps it, and starts with the icicle.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSunburst), nameof(ChartCaption), nameof(ChartInfo))]
    private ChartShape _chartShape = ChartShape.Icicle;

    /// <summary>The chart query in progress (tests await it).</summary>
    public Task ChartTask { get; private set; } = Task.CompletedTask;

    /// <summary>Whether the chart is drawn as a sunburst (the view's choice).</summary>
    public bool ShowsSunburst
    {
        get => ChartShape == ChartShape.Sunburst;
        set => ChartShape = value ? ChartShape.Sunburst : ChartShape.Icicle;
    }

    /// <summary>The line above the chart, for the tree and the shape.</summary>
    public string ChartCaption => (Kind, ShowsSunburst) switch
    {
        (AllocationTreeKindDto.CallTree, false) => "Each bar is a method the bar above it called; its width is what was allocated through it.",
        (AllocationTreeKindDto.CallTree, true) => "Each arc is a method the arc inside it called; its angle is what was allocated through it.",
        (_, false) => "The first row is the methods that allocated; each bar below is a caller of the bar above it, as wide as what was allocated through it.",
        (_, true) => "The first ring is the methods that allocated; each arc further out is a caller of the arc inside it, its angle what was allocated through it.",
    };

    /// <summary>The chart's info text, for its shape.</summary>
    public string ChartInfo => ShowsSunburst ? InfoTexts.AllocationsSunburst : InfoTexts.AllocationsChart;

    partial void OnChartShapeChanged(ChartShape value)
    {
        using var activity = Act("draw the chart as {0}", value);
    }

    partial void OnSelectedRowChanged(AllocationRowItem? value)
    {
        if (_syncingSelection || value is null)
        {
            return;
        }

        _syncingSelection = true;
        SelectedSegment = ChartSegments.FirstOrDefault(s => s.Key == value.Key);
        _syncingSelection = false;
    }

    partial void OnSelectedSegmentChanged(IcicleSegmentDto? value)
    {
        if (_syncingSelection || value is null || value.Level == 0)
        {
            return;
        }

        // Every click is logged with the bytes it showed, so a contested figure is backed up whether or not the tree asks again.
        var share = ChartTotal > 0 ? (double)value.Width / ChartTotal : 0;
        using var activity = Act("show the chart's {0}", string.Create(CultureInfo.InvariantCulture, $"{value.Key} {value.Name}: {value.Width} bytes, {share:P1} of {ChartTotal} bytes"));

        // A segment's row is selected where it is loaded; otherwise its parents open, and the answer selects it.
        _syncingSelection = true;
        SelectedRow = FindLoadedRow(value.Key);
        _syncingSelection = false;
        if (SelectedRow is not null)
        {
            return;
        }

        foreach (var parent in ChartSegments.Where(s => s.Level > 0 && s.Level < value.Level && IcicleGeometry.IsOnPath(s, value)))
        {
            _expanded.Add(parent.Key);
        }

        _revealKey = value.Key;
        _revealFallback = -1;
        _chartReveal = value.Key;
        Requery();
    }

    /// <summary>After the rows a segment asked for: says so when its row is not among them (behind a "more" row, or past the row limit).</summary>
    private void ChartRevealed(string? key, int index)
    {
        if (key is null || key != _chartReveal)
        {
            return;
        }

        _chartReveal = null;
        if (index >= 0)
        {
            return;
        }

        // A tree cut at its row limit keeps its own note: collapsing rows is what helps there, not a "more" row.
        if (!_cut)
        {
            Note = "The tree lists that frame further down its list: expand the list's \"more\" row to find it.";
        }

        Logger.LogInformation("Allocations {Kind}: the chart's {Key} is not among the tree's rows ({Why})", Kind, key, _cut ? "past the row limit" : "behind a \"more\" row");
    }

    /// <summary>Queries the chart for the tree's trace, scope and kind; the old chart goes at once, so it cannot be clicked meanwhile.</summary>
    private void LoadChart()
    {
        ClearChart();
        if (Trace is not { } trace)
        {
            return;
        }

        _chart = new CancellationTokenSource();
        ChartTask = LoadChartAsync(trace, Scope, Kind, _chart.Token);
    }

    /// <summary>Stops the chart's query and empties it (a new scope or kind, or the trace closed).</summary>
    private void ClearChart()
    {
        _chart?.Cancel();
        _chart?.Dispose();
        _chart = null;
        _chartReveal = null;
        ChartTask = Task.CompletedTask;
        SelectedSegment = null;
        ChartSegments = [];
        ChartTotal = 0;
        ChartStatus = null;
    }

    private async Task LoadChartAsync(TraceHandle trace, AllocationScope scope, AllocationTreeKindDto kind, CancellationToken cancellationToken)
    {
        // Its trace id joins the action's.
        using var activity = new Activity("HeapScope.Query").SetTag("grid", "AllocationChart").Start();
        ChartStatus = "Drawing...";
        try
        {
            var result = await _session.QueryChartAsync(trace, scope, kind, null, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            ChartTotal = result.SampledBytes;
            ChartSegments = result.Segments.Count > 1 ? result.Segments : [];

            // No sample (the tree says so) or none wide enough to draw (this says so).
            ChartStatus = result.Segments.Count <= 1 && result.SampledBytes > 0 ? NothingWideEnough : null;
            if (ChartStatus is not null)
            {
                Logger.LogInformation("{Section} shown as: {Message}", "Allocation chart", ChartStatus);
            }

            // A row selected while the chart was drawn selects its segment now.
            _syncingSelection = true;
            SelectedSegment = SelectedRow is { } row ? ChartSegments.FirstOrDefault(s => s.Key == row.Key) : null;
            _syncingSelection = false;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // A newer interval, type or kind replaced this chart, or the trace was closed.
        }
        catch (WorkerOperationException ex) when (ex.ErrorCode == HeapScopeErrorCode.InvalidArgument)
        {
            // A refusal of the request itself (a trace still being recorded): the tree shows the same one.
            ChartSegments = [];
            ChartStatus = null;
            Logger.LogInformation("Allocations {Kind}: the chart was refused: {Reason}", kind, ex.Message);
        }
        catch (Exception ex) when (ex is WorkerException or OperationCanceledException)
        {
            // Anything else may be the chart's alone: the user is told, in the worker's own words with its next step.
            ChartSegments = [];
            ChartStatus = ex.Message;
            Logger.LogWarning("{Section} shown as: {Message}", "Allocation chart", ex.Message);
        }
        catch (Exception ex)
        {
            ChartSegments = [];
            Logger.LogError(ex, "The allocation chart failed");
            ChartStatus = "The chart failed. Details are in the log.";
        }
    }

    private AllocationRowItem? FindLoadedRow(string key)
    {
        foreach (var page in Rows.RequestedPages)
        {
            for (var index = page * VirtualRowList.PageSize; index < Math.Min(Rows.Count, (page + 1) * VirtualRowList.PageSize); index++)
            {
                if (Rows.GetRow(index) is { IsLoaded: true } row && row.Key == key)
                {
                    return row;
                }
            }
        }

        return null;
    }
}
