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
using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeapScope.Application.Allocations;
using HeapScope.Application.Presentation;
using HeapScope.Application.Snapshots;
using HeapScope.Application.Workers;
using HeapScope.Contracts;
using HeapScope.UI.Services;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// The comparison grid (dotMemory's comparison view, ADR-018): two pickers over the open snapshots (the
/// earlier capture is the default baseline), per-type deltas, sorting by any column, the shared grouping and filter.
/// </summary>
public sealed partial class CompareViewModel : TypeGridViewModel<DiffRowItem>
{
    private readonly SnapshotSession _session;
    private readonly AllocationsViewModel? _allocations;
    private bool _settingPair;

    // The trace and interval of the allocations shown, for the step to the Allocations page.
    private (TraceHandle Trace, TimeInterval Interval)? _allocated;

    // Queries that read the trace now; why Allocated between was unticked for the user.
    private int _readingTrace;
    private string? _untickedNote;

    [ObservableProperty]
    private SnapshotItemViewModel? _baseline;

    [ObservableProperty]
    private SnapshotItemViewModel? _current;

    /// <summary>Asks for New and Dead objects by address (two snapshots of one process).</summary>
    [ObservableProperty]
    private bool _matchObjects;

    /// <summary>Asks for each row's retained lower bound in both snapshots.</summary>
    [ObservableProperty]
    private bool _showRetained;

    /// <summary>Shows the per-generation byte deltas when both snapshots have generations.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleColumnGroups))]
    private bool _showGenerations;

    /// <summary>Asks for the sampled allocations between the snapshots (two of one recorded monitoring run).</summary>
    [ObservableProperty]
    private bool _showAllocated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleColumnGroups))]
    private bool _allocatedShown;

    /// <summary>What the allocations shown are, or why there are none.</summary>
    [ObservableProperty]
    private string? _allocatedNote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleColumnGroups))]
    private bool _identityShown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleColumnGroups))]
    private bool _retainedShown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleColumnGroups))]
    private bool _hasGenerations;

    /// <summary>Why New and Dead objects are not shown although asked for, or what the shown ones mean.</summary>
    [ObservableProperty]
    private string? _identityNote;

    /// <summary>Where the retained columns come from, or why they are not shown.</summary>
    [ObservableProperty]
    private string? _retainedNote;

    public CompareViewModel(SnapshotSession session, ObservableCollection<SnapshotItemViewModel> snapshots, IUiDispatcher dispatcher, ILogger<CompareViewModel>? logger = null, INavigator? navigator = null, AllocationsViewModel? allocations = null)
        : base(TypeDiffColumns.BytesDelta, dispatcher, logger)
    {
        OpenRowCommand = new RelayCommand<DiffRowItem?>(row => OpenRow(row, navigator));
        ShowInRetainersCommand = new RelayCommand<DiffRowItem?>(row => Link(row, navigator, (s, r) => NavigationTarget.Retainers(s, typeName: r.TypeName, module: r.Module)));
        RetainedBySetCommand = new RelayCommand<DiffRowItem?>(row => Link(row, navigator, (s, r) => NavigationTarget.RetainedBySet(s, r.TypeName!, r.Module)));
        OpenAsSetCommand = new RelayCommand<System.Collections.IList?>(rows => OpenAsSet(rows, navigator));
        OpenNewCommand = new RelayCommand<DiffRowItem?>(row => OpenChange(row, ObjectChange.New, navigator));
        OpenDeadCommand = new RelayCommand<DiffRowItem?>(row => OpenChange(row, ObjectChange.Dead, navigator));
        OpenSurvivedCommand = new RelayCommand<DiffRowItem?>(row => OpenChange(row, ObjectChange.Survived, navigator));
        ShowWhereAllocatedCommand = new RelayCommand<DiffRowItem?>(ShowWhereAllocated);
        _allocations = allocations;
        if (allocations is not null)
        {
            allocations.Traces.CollectionChanged += OnTracesChanged;
        }

        Trend = new TrendViewModel(session, snapshots, dispatcher, logger, navigator);
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        Snapshots.CollectionChanged += OnSnapshotsChanged;
        PickDefaults();
    }

    /// <summary>The open snapshots the pickers offer.</summary>
    public ObservableCollection<SnapshotItemViewModel> Snapshots { get; }

    /// <summary>The trend across several snapshots, shown instead of the pair when <see cref="IsTrendShown"/>.</summary>
    public TrendViewModel Trend { get; }

    [ObservableProperty]
    private bool _isTrendShown;

    /// <summary>The optional column groups shown: New and Dead, retained bytes, per-generation deltas.</summary>
    public IReadOnlyList<string> VisibleColumnGroups => ColumnGroups.Of((ColumnGroups.Identity, IdentityShown), (ColumnGroups.Retained, RetainedShown), (ColumnGroups.Generations, ShowGenerations && HasGenerations), (ColumnGroups.Allocated, AllocatedShown));

    /// <summary>Opens a row's new objects (current snapshot) as a set.</summary>
    public System.Windows.Input.ICommand OpenNewCommand { get; }

    /// <summary>Opens a row's dead objects (in the baseline) as a set.</summary>
    public System.Windows.Input.ICommand OpenDeadCommand { get; }

    /// <summary>Opens a row's survived objects (current snapshot) as a set.</summary>
    public System.Windows.Input.ICommand OpenSurvivedCommand { get; }

    partial void OnMatchObjectsChanged(bool value) => RequeryIfPair();

    partial void OnShowRetainedChanged(bool value) => RequeryIfPair();

    partial void OnShowAllocatedChanged(bool value)
    {
        if (value)
        {
            _untickedNote = null;
        }

        RequeryIfPair();
    }

    protected override void OnCleared()
    {
        (_allocated, AllocatedShown, AllocatedNote, _untickedNote) = (null, false, null, null);

        // Everything the last answer set, so nothing of an old pair is shown.
        // Not HasGenerations: the next query's sort depends on it, and its answer sets it again.
        (IdentityNote, IdentityShown, RetainedNote, RetainedShown) = (null, false, null, false);
        base.OnCleared();
    }

    /// <summary>Whether the app has allocation traces at all (a smoke or test shell may not).</summary>
    public bool OffersAllocated => _allocations is not null;

    /// <summary>Shows the interval of the allocations on the Allocations page, narrowed to a row's type.</summary>
    public System.Windows.Input.ICommand ShowWhereAllocatedCommand { get; }

    partial void OnIsTrendShownChanged(bool value)
    {
        Trend.IsActive = value && IsActive;
        if (value)
        {
            Trend.Start();
        }
    }

    private void RequeryIfPair()
    {
        if (CanQuery && IsActive)
        {
            Requery();
        }
        else
        {
            _pending = CanQuery;
        }
    }

    /// <summary>Why there is nothing to compare yet, if so.</summary>
    public string? Hint => Snapshots.Count < 2
        ? "Open two snapshots to compare them: the earlier one is the baseline."
        : Baseline is null || Current is null ? "Pick a baseline and a current snapshot."
        : ByOpenOrder ? "A Linux or macOS core dump records no capture time (its file's time is shown), so the snapshot opened first is the baseline. Swap if that is the later one."
        : null;

    // A capture time that is only the file's time can be changed by a copy, so it does not
    // decide which snapshot is the baseline.
    private bool ByOpenOrder => Snapshots.Any(s => s.Handle.Descriptor.CaptureTimeFromFile);

    protected override bool CanQuery => Baseline is not null && Current is not null;

    protected override string BusyText => "Comparing...";

    partial void OnBaselineChanged(SnapshotItemViewModel? value)
    {
        OfferInterfaces();
        if (!_settingPair)
        {
            OnPairChanged();
        }
    }

    partial void OnCurrentChanged(SnapshotItemViewModel? value)
    {
        OfferInterfaces();
        if (!_settingPair)
        {
            OnPairChanged();
        }
    }

    // Interface rows need both snapshots' interfaces, as each snapshot's availability list says; a side not chosen yet
    // leaves the offer to the other.
    private void OfferInterfaces() =>
        OfferGroupings(new[] { Baseline, Current }.All(s => s is null || s.Handle.Descriptor.IsAvailable(SnapshotFeatureIds.InterfaceGrouping)));

    protected override async Task<GridQuery<DiffRowItem>> RunQueryAsync(CancellationToken cancellationToken)
    {
        var groupBy = GroupBy;
        var scope = Scope(Baseline!.Handle.Descriptor.Kind, Current!.Handle.Descriptor.Kind);
        var (match, retained) = (MatchObjects, ShowRetained && groupBy != TypeGrouping.Interface);
        var (baseline, current) = (Baseline!.Handle, Current!.Handle);
        var (allocations, allocatedNote, clockShift) = ShowAllocated ? await AllocationsAsync(baseline.Path, current.Path, cancellationToken) : (null, _untickedNote, 0);

        // A sort by a column this query may not have falls back to bytes (as in the types grid).
        var unavailable = (!match && SortColumn is TypeDiffColumns.NewObjects or TypeDiffColumns.DeadObjects)
            || (!retained && SortColumn is TypeDiffColumns.BaselineRetained or TypeDiffColumns.CurrentRetained or TypeDiffColumns.RetainedDelta)
            || (allocations is null && SortColumn is TypeDiffColumns.AllocatedBytes or TypeDiffColumns.AllocatedSamples)
            || (!HasGenerations && SortColumn is TypeDiffColumns.Gen0Delta or TypeDiffColumns.Gen1Delta or TypeDiffColumns.Gen2Delta or TypeDiffColumns.LohDelta or TypeDiffColumns.PohDelta);
        var sortColumn = unavailable ? TypeDiffColumns.BytesDelta : SortColumn;
        if (unavailable)
        {
            Logger.LogInformation("Compare: sorted by {Used}, as this comparison has no {Asked}", sortColumn, SortColumn);
        }

        var finished = false;
        var progress = retained ? new Progress<JobProgress>(p => Status = finished || cancellationToken.IsCancellationRequested ? Status : ReferrersText.Progress(p)) : null;
        var (filter, descending) = (Filter, Descending);
        SnapshotDiff diff;
        try
        {
            diff = await _session.QueryTypeDiffAsync(baseline, current, groupBy, filter, sortColumn, descending, progress, cancellationToken, match, retained, allocations);
        }
        catch (Exception ex) when (allocations is not null && !cancellationToken.IsCancellationRequested && IsAboutTheTrace(ex))
        {
            // The trace cannot be used (another process, not between them, its file changed): the comparison is shown without it.
            Logger.LogInformation("Compare: the allocations were refused, so the comparison runs without them, sorted by {Sort}; said as: {Reason}", sortColumn is TypeDiffColumns.AllocatedBytes or TypeDiffColumns.AllocatedSamples ? TypeDiffColumns.BytesDelta : sortColumn, ex.Message);
            (allocations, allocatedNote) = (null, ex.Message);
            sortColumn = sortColumn is TypeDiffColumns.AllocatedBytes or TypeDiffColumns.AllocatedSamples ? TypeDiffColumns.BytesDelta : sortColumn;
            diff = await _session.QueryTypeDiffAsync(baseline, current, groupBy, filter, sortColumn, descending, progress, cancellationToken, match, retained);
        }

        finished = true;
        var result = diff.Result;
        return new GridQuery<DiffRowItem>(
            diff.Rows,
            () =>
            {
                IdentityShown = result.ObjectsMatched;
                IdentityNote = result.ObjectsMatched
                    ? ObjectIdentityTexts.Matched(result.NewObjects, result.DeadObjects, CultureInfo.CurrentCulture) + (result.IdentityNote is { } caution ? " " + caution : string.Empty)
                    : result.IdentityNote;
                RetainedShown = result.BaselineDominators is not null;
                RetainedNote = result.BaselineDominators is { } b && result.CurrentDominators is { } c
                    ? "Retained bytes are lower bounds in each snapshot. Baseline: " + RetainedSizeTexts.Source(b, CultureInfo.CurrentCulture) + " Current: " + RetainedSizeTexts.Source(c, CultureInfo.CurrentCulture)
                    : ShowRetained && groupBy == TypeGrouping.Interface ? "Interface rows have no retained sizes: a type counts under each interface it implements. Group by type, namespace or assembly to see them." : null;
                HasGenerations = result.KindDeltas is not null;
                _allocated = allocations is { } a && result.Allocations is { } shown ? (a.Trace, new TimeInterval(shown.FromSeconds, shown.ToSeconds)) : null;
                if (allocations is { Trace.IsClosed: true } closed)
                {
                    // Closed after the worker answered.
                    Untick(closed.Trace, AllocatedBetweenTexts.TraceClosed);
                    return;
                }

                AllocatedShown = result.Allocations is not null;
                AllocatedNote = result.Allocations is { } info
                    ? AllocatedBetweenTexts.Summary(info, Format.Bytes, CultureInfo.CurrentCulture, clockShift) + (allocatedNote is { } heapTime ? " " + heapTime : string.Empty)
                    : allocatedNote;
                Totals = string.Create(
                CultureInfo.CurrentCulture,
                $"{result.Rows.TotalCount:N0} of {result.UnfilteredRows:N0} {Plural(groupBy)} ({result.NewRows:N0} new, {result.GoneRows:N0} gone); matching rows {Format.SignedBytes(result.MatchingBytesDelta)}; heap {Format.SignedBytes(result.CurrentBytes - result.BaselineBytes)}, {SignedCount(result.CurrentObjects - result.BaselineObjects)} objects ({scope}{(result.ExactCounts ? string.Empty : "; estimated: a snapshot is sampled or incomplete")})");
            },
            onError => DiffRowItem.CreateList(
                diff.Rows,
                result.Rows,
                ex =>
                {
                    if (ex is AllocationsUnavailableException && allocations is { } used && ShowAllocated)
                    {
                        Untick(used.Trace, ex.Message);
                    }
                    else
                    {
                        onError(ex);
                    }
                },
                Logger));
    }

    // A refusal of the question may be about the trace: without it, anything else is refused again and shown.
    private static bool IsAboutTheTrace(Exception ex) =>
        ex is AllocationsUnavailableException or WorkerOperationException { ErrorCode: HeapScopeErrorCode.InvalidArgument or HeapScopeErrorCode.TraceNotLoaded };

    // What the totals count, with the caution for a full dump against a gcdump (garbage shows as a drop).
    private static string Scope(SnapshotKindDto baseline, SnapshotKindDto current) =>
        SnapshotScopeTexts.Comparison + (SnapshotScopeTexts.MixedKinds(baseline, current) is { } mixed ? "; " + mixed : string.Empty);

    /// <summary>Swaps baseline and current.</summary>
    [RelayCommand]
    private void Swap() => SetPair(Current, Baseline, "swapped by the user");

    /// <summary>A diff row's group in the current snapshot's dominator tree.</summary>
    public System.Windows.Input.ICommand ShowInRetainersCommand { get; }

    /// <summary>What a diff row's objects in the current snapshot retain together.</summary>
    public System.Windows.Input.ICommand RetainedBySetCommand { get; }

    /// <summary>
    /// Opens the selected rows' types as one object set in the current snapshot, or in the baseline when none
    /// of them is in the current one. The parameter is the grid's selected rows.
    /// </summary>
    public System.Windows.Input.ICommand OpenAsSetCommand { get; }

    /// <summary>
    /// <paramref name="item"/> was opened again as a new worker snapshot, maybe of a rebuilt cache: a pair that holds
    /// it runs again, logged with its new id, and so does a trend that ticks it. Their rows came from the earlier snapshot, and a
    /// page fetched later would run the old query against the new one, mixing two graphs in one grid. A silent reopen after a
    /// worker restart does not come here: its graph is the same.
    /// </summary>
    public void Reopened(SnapshotItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item == Baseline || item == Current)
        {
            OnPairChanged("run again, a snapshot of it was opened again");
        }

        Trend.Reopened(item);
    }

    // How the pair was chosen, for the log line that backs up a disputed comparison.
    private void OnPairChanged(string reason = "picked by the user")
    {
        _untickedNote = null;
        OnPropertyChanged(nameof(Hint));
        if (Baseline is { } baseline && Current is { } current)
        {
            Logger.LogInformation(
                "Compare pair {Reason}: baseline {BaselinePath} (worker snapshot {BaselineId}, captured {BaselineCaptured:O}{BaselineFileTime}), current {CurrentPath} (worker snapshot {CurrentId}, captured {CurrentCaptured:O}{CurrentFileTime})",
                reason,
                baseline.Handle.Path,
                baseline.Handle.Descriptor.SnapshotId,
                baseline.Handle.Descriptor.CapturedAtUtc,
                baseline.Handle.Descriptor.CaptureTimeFromFile ? ", the file's time" : string.Empty,
                current.Handle.Path,
                current.Handle.Descriptor.SnapshotId,
                current.Handle.Descriptor.CapturedAtUtc,
                current.Handle.Descriptor.CaptureTimeFromFile ? ", the file's time" : string.Empty);
        }

        if (CanQuery && IsActive)
        {
            Requery(subjectChanged: true);
        }
        else
        {
            // Hidden, nothing is computed: the comparison runs when the tab is shown.
            ClearResults();
            _pending = CanQuery;
        }
    }

    /// <summary>Whether the Compare tab is shown: hidden, a new pair waits. True until a tab is chosen.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            Trend.IsActive = value && IsTrendShown;
            if (value && _pending)
            {
                _pending = false;
                OnPairChanged("run when the tab was shown");
            }
        }
    }

    private bool _isActive = true;

    private bool _pending;

    private void OnSnapshotsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A picked snapshot that was closed leaves its picker; an empty picker takes another open snapshot.
        if (Baseline is not null && !Snapshots.Contains(Baseline))
        {
            Baseline = null;
        }

        if (Current is not null && !Snapshots.Contains(Current))
        {
            Current = null;
        }

        PickDefaults();
        OnPropertyChanged(nameof(Hint));
    }

    private void PickDefaults()
    {
        if ((Baseline is not null && Current is not null) || Snapshots.Count < 2)
        {
            return;
        }

        // By capture time, open order breaking ties; snapshots without a capture time count as the newest. Open order alone
        // when a capture time is only a file's time.
        var ordered = Snapshots
            .Select((s, i) => (Item: s, Order: i))
            .OrderBy(x => ByOpenOrder ? DateTimeOffset.MinValue : x.Item.Handle.Descriptor.CapturedAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Order)
            .Select(x => x.Item)
            .ToList();
        if (Baseline is null && Current is null)
        {
            // The two most recent captures, the earlier one as the baseline.
            SetPair(ordered[^2], ordered[^1], Automatic);
        }
        else if (Baseline is null)
        {
            // The latest capture before the current one, else the earliest other snapshot.
            var others = ordered.Where(s => s != Current).ToList();
            var position = ordered.IndexOf(Current!);
            SetPair(others.LastOrDefault(s => ordered.IndexOf(s) < position) ?? others[0], Current, Automatic);
        }
        else
        {
            // The first capture after the baseline, else the latest other snapshot.
            var others = ordered.Where(s => s != Baseline).ToList();
            var position = ordered.IndexOf(Baseline);
            SetPair(Baseline, others.FirstOrDefault(s => ordered.IndexOf(s) > position) ?? others[^1], Automatic);
        }
    }

    private string Automatic => ByOpenOrder ? "chosen automatically by open order (a capture time is only a file's time)" : "chosen automatically by capture time";

    /// <summary>Sets both pickers and queries once.</summary>
    private void SetPair(SnapshotItemViewModel? baseline, SnapshotItemViewModel? current, string reason)
    {
        _settingPair = true;
        try
        {
            Baseline = baseline;
            Current = current;
        }
        finally
        {
            _settingPair = false;
        }

        OnPairChanged(reason);
    }

    private static string SignedCount(long value) => value.ToString("+#,0;-#,0;0", CultureInfo.CurrentCulture);
}
