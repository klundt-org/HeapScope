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

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeapScope.Application.Snapshots;
using HeapScope.Contracts;
using HeapScope.UI.Services;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>An open snapshot offered to a trend, ticked to take part.</summary>
public sealed partial class TrendSnapshotItem(SnapshotItemViewModel snapshot) : ObservableObject
{
    public SnapshotItemViewModel Snapshot { get; } = snapshot;

    [ObservableProperty]
    private bool _isIncluded = true;
}

/// <summary>
/// A trend across several open snapshots: each row's objects and bytes in every ticked snapshot,
/// ordered by capture time (open order when a time is only a file's), the change first to last and the steps it grew in.
/// </summary>
public sealed partial class TrendViewModel : TypeGridViewModel<CellRowItem>
{
    private readonly SnapshotSession _session;
    private bool _started;

    [ObservableProperty]
    private IReadOnlyList<(ResultColumn Column, int Index)> _columns = [];

    [ObservableProperty]
    private IReadOnlyList<SnapshotItemViewModel> _ordered = [];

    /// <summary>How the snapshots are ordered when capture times cannot say.</summary>
    [ObservableProperty]
    private string? _orderNote;

    public TrendViewModel(SnapshotSession session, ObservableCollection<SnapshotItemViewModel> snapshots, IUiDispatcher dispatcher, ILogger? logger = null, INavigator? navigator = null)
        : base(TypeTrendColumns.BytesDelta, dispatcher, logger)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(snapshots);
        Choices = [];
        snapshots.CollectionChanged += (_, _) => Sync(snapshots);
        Sync(snapshots);
        OpenRowCommand = new RelayCommand<CellRowItem?>(row =>
        {
            // A row opens its type's objects in the last snapshot of the trend.
            if (row?[TypeTrendColumns.TypeName] is { Length: > 0 } typeName && Ordered.LastOrDefault() is { } last)
            {
                navigator?.Navigate(NavigationTarget.Instances(last.Handle, typeName, row[TypeTrendColumns.Module]));
            }
        });
    }

    /// <summary>The open snapshots, each ticked or not.</summary>
    public ObservableCollection<TrendSnapshotItem> Choices { get; }

    /// <summary>Why there is no trend yet, if so.</summary>
    public string? Hint => Choices.Count(c => c.IsIncluded) switch
    {
        < 2 => "Tick two or more open snapshots to see their trend.",
        > TypeTrendColumns.MaxSnapshots => $"A trend takes at most {TypeTrendColumns.MaxSnapshots} snapshots: untick some.",
        _ => null,
    };

    protected override bool CanQuery => _started && Choices.Count(c => c.IsIncluded) is >= 2 and <= TypeTrendColumns.MaxSnapshots;

    protected override string BusyText => "Summarizing the snapshots...";

    /// <summary>Queries the first time the Trend view is shown; later, when the ticks change.</summary>
    public void Start()
    {
        if (!_started)
        {
            _started = true;
            Changed();
        }
    }

    /// <summary>A ticked snapshot was opened again as a new worker snapshot: the trend runs again.</summary>
    public void Reopened(SnapshotItemViewModel item)
    {
        if (Choices.Any(c => c.Snapshot == item && c.IsIncluded))
        {
            Changed();
        }
    }

    // Everything the last answer set, so nothing of an old set of snapshots is shown.
    protected override void OnCleared()
    {
        (Columns, Ordered, OrderNote) = ([], [], null);
        base.OnCleared();
    }

    // Whether a sort column is one a trend of this many snapshots has.
    private static bool Exists(string column, int snapshots) =>
        System.Text.RegularExpressions.Regex.Match(column, "^(?:Objects|Bytes)([0-9]+)$") is not { Success: true } perSnapshot
        || int.Parse(perSnapshot.Groups[1].Value, CultureInfo.InvariantCulture) <= snapshots;

    /// <summary>Ticks or unticks a snapshot; the change queries again.</summary>
    [RelayCommand]
    private void Toggle(TrendSnapshotItem? item)
    {
        if (item is not null)
        {
            item.IsIncluded = !item.IsIncluded;
        }
    }

    private void Sync(ObservableCollection<SnapshotItemViewModel> snapshots)
    {
        foreach (var gone in Choices.Where(c => !snapshots.Contains(c.Snapshot)).ToList())
        {
            Choices.Remove(gone);
        }

        foreach (var added in snapshots.Where(s => Choices.All(c => c.Snapshot != s)))
        {
            var item = new TrendSnapshotItem(added);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TrendSnapshotItem.IsIncluded))
                {
                    Changed();
                }
            };
            Choices.Add(item);
        }

        Changed();
    }

    /// <summary>Whether the trend is shown: hidden, a change waits until it is. True until Compare says otherwise.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            _isActive = value;
            if (value && _pending)
            {
                _pending = false;
                Changed();
            }
        }
    }

    private bool _isActive = true;

    private bool _pending;

    private void Changed()
    {
        OnPropertyChanged(nameof(Hint));
        if (CanQuery && !IsActive)
        {
            _pending = true;
            return;
        }

        if (CanQuery)
        {
            Requery(subjectChanged: true);
        }
        else if (_started)
        {
            ClearResults();
        }
    }

    protected override async Task<GridQuery<CellRowItem>> RunQueryAsync(CancellationToken cancellationToken)
    {
        // Capture time orders the snapshots unless one only has its file's time: then open order.
        var included = Choices.Where(c => c.IsIncluded).Select(c => c.Snapshot).ToList();
        var byOpenOrder = included.Any(s => s.Handle.Descriptor.CaptureTimeFromFile || s.Handle.Descriptor.CapturedAtUtc is null);
        var ordered = byOpenOrder ? included : [.. included.OrderBy(s => s.Handle.Descriptor.CapturedAtUtc)];
        var groupBy = GroupBy;
        Logger.LogInformation(
            "Trend of {Count} snapshots ordered by {Rule}: {SnapshotIds} captured {Times}",
            ordered.Count,
            byOpenOrder ? "open order (a capture time is only a file's)" : "capture time",
            string.Join(", ", ordered.Select(s => s.Handle.Descriptor.SnapshotId)),
            string.Join(", ", ordered.Select(s => s.Handle.Descriptor.CapturedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "unknown")));
        OrderNote = byOpenOrder ? "A snapshot records no capture time (only its file's), so the trend runs in the order the snapshots were opened." : null;
        var finished = false;
        var progress = new Progress<JobProgress>(p => Status = finished || cancellationToken.IsCancellationRequested ? Status : ReferrersText.Progress(p));
        // A snapshot's column that is gone (one of the snapshots was unticked or closed) sorts by the change instead.
        var sortColumn = SortColumn is { } asked && !Exists(asked, ordered.Count) ? TypeTrendColumns.BytesDelta : SortColumn;
        if (sortColumn != SortColumn)
        {
            Logger.LogInformation("Trend: sorted by {Used}, as a trend of {Count} snapshots has no {Asked}", sortColumn, ordered.Count, SortColumn);
            SortColumn = sortColumn;
        }

        var trend = await _session.QueryTypeTrendAsync([.. ordered.Select(s => s.Handle)], groupBy, Filter, sortColumn, Descending, progress, cancellationToken);
        finished = true;
        var result = trend.Result;
        return new GridQuery<CellRowItem>(
            trend.Rows,
            () =>
            {
                Ordered = ordered;
                Columns = [.. result.Rows.Columns.Select((c, i) => (c, i)).Where(c => !c.c.IsHidden)];
                Totals = string.Create(
                    CultureInfo.CurrentCulture,
                    $"{result.Rows.TotalCount:N0} of {result.UnfilteredRows:N0} {Plural(groupBy)} across {ordered.Count} snapshots ({string.Join(", then ", ordered.Select(s => s.Title))}); heap {string.Join(", ", result.TotalBytes.Select(Format.Bytes))}{(result.ExactCounts ? string.Empty : "; estimated: a snapshot is sampled or incomplete")}");
            },
            onError => CellRowItem.CreateList(trend.Rows, result.Rows, onError, Logger));
    }
}
