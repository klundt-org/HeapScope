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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeapScope.Application.Snapshots;
using HeapScope.Application.Workers;
using HeapScope.Contracts;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>The Retainers tab's chart of the focus: the icicle or the sunburst of the same segments (ADR-046), and its selection kept in step with the tree's.</summary>
public sealed partial class RetainersViewModel
{
    private CancellationTokenSource? _icicle;
    private bool _syncingSelection;

    /// <summary>How the chart is drawn; the tab keeps it, and a new tab starts with the icicle.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSunburst), nameof(ChartCaption), nameof(ChartInfo))]
    private ChartShape _chartShape = ChartShape.Icicle;

    /// <summary>The line above the chart, for its shape.</summary>
    public string ChartCaption => ShowsSunburst
        ? "Each arc is what the arc inside it keeps alive, and its angle is how much."
        : "Each bar is what the bar above it keeps alive, and its width is how much.";

    /// <summary>The chart's info text, for its shape.</summary>
    public string ChartInfo => ShowsSunburst ? InfoTexts.RetainersSunburst : InfoTexts.RetainersChart;

    /// <summary>Whether the chart is drawn as a sunburst (the view's choice).</summary>
    public bool ShowsSunburst
    {
        get => ChartShape == ChartShape.Sunburst;
        set => ChartShape = value ? ChartShape.Sunburst : ChartShape.Icicle;
    }

    partial void OnChartShapeChanged(ChartShape value)
    {
        using var activity = Act("draw the chart as {0}", value);
    }

    [ObservableProperty]
    private IReadOnlyList<IcicleSegmentDto> _icicleSegments = [];

    [ObservableProperty]
    private long _icicleTotal;

    [ObservableProperty]
    private IcicleSegmentDto? _selectedSegment;

    [ObservableProperty]
    private string? _icicleStatus;

    /// <summary>The icicle query in progress (tests and smoke runs await it).</summary>
    public Task IcicleTask { get; private set; } = Task.CompletedTask;

    partial void OnSelectedRowChanged(DominatorRowItem? value)
    {
        if (_syncingSelection || value is null)
        {
            return;
        }

        _syncingSelection = true;
        SelectedSegment = IcicleSegments.FirstOrDefault(s => s.Key == value.Key);
        _syncingSelection = false;
    }

    partial void OnSelectedSegmentChanged(IcicleSegmentDto? value)
    {
        if (_syncingSelection || value is null)
        {
            return;
        }

        // A bar selects its row when the row is in the loaded part of the tree; otherwise no row is selected, so the row
        // commands never act on another one. A double-click on the bar focuses on it.
        _syncingSelection = true;
        SelectedRow = FindLoadedRow(value.Key);
        _syncingSelection = false;
    }

    [RelayCommand]
    private void FocusSegment(IcicleSegmentDto? segment)
    {
        if (segment is not null)
        {
            using var activity = Act("focus on the icicle's 0x{0:x}", segment.Address);
            FocusOn(segment.Address);
        }
    }

    private void LoadIcicle()
    {
        _icicle?.Cancel();
        _icicle?.Dispose();
        _icicle = new CancellationTokenSource();
        IcicleTask = LoadIcicleAsync(Snapshot!, FocusAddress, _icicle.Token);
    }

    private async Task LoadIcicleAsync(SnapshotHandle snapshot, ulong focus, CancellationToken cancellationToken)
    {
        // Its trace id joins the action's.
        using var activity = new Activity("HeapScope.Query").SetTag("grid", "Icicle").Start();
        IcicleStatus = "Drawing...";
        try
        {
            var result = await _session.QueryIcicleAsync(snapshot, focus, null, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            IcicleTotal = result.Total;
            IcicleSegments = result.Segments;
            SelectedSegment = null;
            IcicleStatus = null;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // A newer focus replaced this chart.
        }
        catch (Exception ex) when (ex is WorkerException or OperationCanceledException)
        {
            // The grid shows the same refusal; the chart says it is empty rather than repeating it.
            IcicleSegments = [];
            IcicleStatus = null;
            Logger.LogInformation("Retainers: the icicle was refused: {Reason}", ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "The icicle chart failed");
            IcicleStatus = "The chart failed. Details are in the log.";
        }
    }
}
