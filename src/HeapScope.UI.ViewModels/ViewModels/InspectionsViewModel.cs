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
using HeapScope.Application.Presentation;
using HeapScope.Application.Snapshots;
using HeapScope.Application.Workers;
using HeapScope.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// The Inspections tab (sprint 1.6, dotMemory's inspections overview): a tile per inspection with its finding, cause and
/// fix, fragmentation bars, and the selected inspection's rows. The inspections of a snapshot run once, one at a time,
/// the first time the tab is shown for it; double-clicking a row opens its objects on the Snapshot tab.
/// </summary>
public sealed partial class InspectionsViewModel : ObservableObject
{
    private const int TileRows = 3;

    private readonly SnapshotSession _session;
    private readonly ILogger _logger;
    private CancellationTokenSource? _running;
    private CancellationTokenSource? _rerun;
    private Task _runAll = Task.CompletedTask;
    private bool _started;

    /// <summary>Whether the run or one of its inspections failed, so Try again is offered (peer review 4.5 M7).</summary>
    [ObservableProperty]
    private bool _canTryAgain;

    [ObservableProperty]
    private InspectionTileViewModel? _selected;

    [ObservableProperty]
    private VirtualRowList<CellRowItem> _rows = Empty();

    /// <summary>The selected inspection's visible columns, with their index in the rows' cells.</summary>
    [ObservableProperty]
    private IReadOnlyList<(ResultColumn Column, int Index)> _columns = [];

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _isActive;

    /// <summary>The baseline the growth detectors compare with (sprint 2.6); "None" by default unless an earlier snapshot of the same process is open.</summary>
    [ObservableProperty]
    private BaselineChoice? _baseline;

    private bool _choosingBaseline;

    // The person chose the baseline (None included), so a refresh keeps it while it stays open (2.6 review M12).
    private bool _baselineChosen;

    public InspectionsViewModel(SnapshotSession session, ILogger? logger = null, INavigator? navigator = null)
    {
        _navigator = navigator;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _logger = logger ?? NullLogger.Instance;
    }

    public SnapshotHandle? Snapshot { get; private set; }

    /// <summary>The tiles: the leak detectors first, then the waste inspections.</summary>
    public ObservableCollection<InspectionTileViewModel> Tiles { get; } = [];

    /// <summary>"None" and the other open snapshots, for the baseline picker.</summary>
    public ObservableCollection<BaselineChoice> BaselineChoices { get; } = [];

    /// <summary>Whether any inspection uses a baseline (the picker is shown).</summary>
    public bool UsesBaseline => Tiles.Any(t => t.Info.UsesBaseline && t.IsAvailable);

    public ObservableCollection<FragmentItem> Fragments { get; } = [];

    public bool HasFragments => Fragments.Count > 0;

    /// <summary>The run in progress (tests and smoke runs await it).</summary>
    public Task RunTask { get; private set; } = Task.CompletedTask;

    // Where a row leads: the Snapshots page, which picks the tab (2.3.7 review M8).
    private readonly INavigator? _navigator;

    /// <summary>Shows <paramref name="snapshot"/>'s inspections (null clears); they run when the tab is active.</summary>
    public void Show(SnapshotHandle? snapshot) => Show(snapshot, keepBaseline: false);

    /// <param name="keepBaseline">Whether a chosen baseline that is still open stays chosen (Try again).</param>
    /// <param name="keepAnyBaseline">Whether the baseline in place stays, chosen or the default (a suspended run, H13).</param>
    private void Show(SnapshotHandle? snapshot, bool keepBaseline, bool keepAnyBaseline = false)
    {
        var (kept, wasChosen) = (Baseline?.Snapshot, _baselineChosen);
        _running?.Cancel();
        _running?.Dispose();
        _running = null;
        _rerun?.Dispose();
        _rerun = null;
        foreach (var tile in Tiles)
        {
            Release(tile.Run);
        }

        Tiles.Clear();
        Fragments.Clear();
        OnPropertyChanged(nameof(HasFragments));
        OnPropertyChanged(nameof(UsesBaseline));
        Selected = null;
        Status = null;
        CanTryAgain = false;
        Snapshot = snapshot;
        _started = false;
        _baselineChosen = false;
        ChooseBaselines(snapshot);
        if (keepBaseline && (wasChosen || keepAnyBaseline) && kept is not null && BaselineChoices.FirstOrDefault(c => c.Snapshot == kept) is { } same)
        {
            // Before the run starts, so its growth tiles run with it (the second review).
            _choosingBaseline = true;
            Baseline = same;
            _choosingBaseline = false;
            _baselineChosen = wasChosen;
        }

        if (IsActive)
        {
            Start();
        }
    }

    /// <summary>
    /// Stops a run that has not finished, for a workspace that is no longer shown (peer review 4.5 H13): the worker's time goes to
    /// the snapshot shown, and the inspections run again when this one is shown. A finished run is kept as it is.
    /// </summary>
    public void Suspend()
    {
        if (!_started || RunTask.IsCompleted)
        {
            return;
        }

        _logger.LogInformation("Inspections: the run of {Snapshot} stopped, its workspace no longer shown; it runs again when shown", Snapshot?.Path);
        Show(Snapshot, keepBaseline: true, keepAnyBaseline: true);
    }

    /// <summary>Runs every inspection of the snapshot again, after a failure.</summary>
    [RelayCommand]
    private void TryAgain()
    {
        _logger.LogInformation("Inspections: tried again for {Snapshot}, baseline {Baseline}", Snapshot?.Path, Baseline?.Snapshot?.Path ?? "none");

        // The baseline chosen stays chosen (review of 4.6).
        Show(Snapshot, keepBaseline: true);
    }

    partial void OnIsActiveChanged(bool value)
    {
        if (value && !_started)
        {
            Start();
        }
    }

    partial void OnSelectedChanged(InspectionTileViewModel? value)
    {
        Rows.Cancel();
        if (value?.Run is { } run)
        {
            var columns = run.Rows.Info.Columns;
            Columns = [.. columns.Select((c, i) => (c, i)).Where(c => !c.c.IsHidden)];
            Rows = CellRowItem.CreateList(run.Rows, run.Rows.Info, ex => Status = ex.Message, _logger);
        }
        else
        {
            Columns = [];
            Rows = Empty();
        }
    }

    [RelayCommand]
    private void OpenRow(CellRowItem? row)
    {
        if (row is not { IsLoaded: true } || Snapshot is not { } snapshot)
        {
            return;
        }

        // A leak detector's row of a type opens the objects behind it, as a set (sprint 3.1, 2.6 review M24), and so does one
        // with no type or address (a timer's static method, the finalizer backlog's other types: 3.10 review M26). A row that
        // names one object (a growing collection) still opens that object (the 3.1 review).
        if (Selected is { IsDetector: true } && TargetOf(row) is null or { TypeName: not null } && SetOf(row) is { } set)
        {
            _navigator?.Navigate(NavigationTarget.ObjectSet(snapshot, set.Query, set.Title));
            return;
        }

        if (TargetOf(row) is not { } target)
        {
            return;
        }

        // The objects of its type in its scope, or its object, on the Snapshot tab.
        _navigator?.Navigate(target.TypeName is { } typeName
            ? NavigationTarget.Instances(snapshot, typeName, target.Module, target.Scope, target.Address)
            : NavigationTarget.Object(snapshot, target.Address));
    }

    /// <summary>Opens a row's objects as a set with all its views (sprint 3.1): what it counted, or what it opens.</summary>
    [RelayCommand]
    private void OpenAsSet(CellRowItem? row)
    {
        if (row is { IsLoaded: true } && Snapshot is { } snapshot && SetOf(row) is { } set)
        {
            _navigator?.Navigate(NavigationTarget.ObjectSet(snapshot, set.Query, set.Title));
        }
    }

    /// <summary>
    /// A row of the selected inspection as a set (sprint 3.1): its inspection, its number and the baseline it ran with,
    /// titled by the inspection and the row's key.
    /// </summary>
    public (ObjectSetQuery Query, string Title)? SetOf(CellRowItem row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (Selected is not { Run: not null } tile)
        {
            return null;
        }

        var key = Columns.Count > 0 ? row.Display[Columns[0].Index] : string.Empty;
        var spec = ObjectSetSpec.OfInspectionRow(tile.Info.Name, row.Index);
        return (new ObjectSetQuery(spec, tile.RanWith), $"{tile.Title}: {key}");
    }

    /// <summary>A row's object (when it names one), or its type's group, in the dominator tree (sprint 2.4).</summary>
    [RelayCommand]
    private void ShowInRetainers(CellRowItem? row)
    {
        if (row is { IsLoaded: true } && Snapshot is { } snapshot && TargetOf(row) is { } target)
        {
            _navigator?.Navigate(target.Address != 0
                ? NavigationTarget.Retainers(snapshot, address: target.Address)
                : NavigationTarget.Retainers(snapshot, typeName: target.TypeName, module: target.Module));
        }
    }

    /// <summary>
    /// What a row's objects retain together (sprint 2.4): a whole type's row as that type; any other row (some of a type's
    /// objects, such as the empty collections, or a detector's counted objects) through its own set (sprint 3.1, the 2.4
    /// review M14), so it never gets the whole type's answer.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasObjects))]
    private void RetainedBySet(CellRowItem? row)
    {
        if (row is not { IsLoaded: true } || Snapshot is not { } snapshot)
        {
            return;
        }

        if (TargetOf(row) is { TypeName: { } typeName, Scope: InstanceScope.All, Address: 0 } target && Selected is not { IsDetector: true })
        {
            _navigator?.Navigate(NavigationTarget.RetainedBySet(snapshot, typeName, target.Module));
        }
        else if (SetOf(row) is { } set)
        {
            _navigator?.Navigate(NavigationTarget.RetainedBySet(snapshot, set.Query, set.Title));
        }
    }

    // A detector's row with no type or address still has members, as opening it does (3.10 review M26, M32).
    private bool HasObjects(CellRowItem? row) => row is { IsLoaded: true } && (TargetOf(row) is not null || Selected is { IsDetector: true });

    /// <summary>What a row opens: its hidden type, module and scope, or its address alone (ADR-020).</summary>
    public static InspectionTarget? TargetOf(CellRowItem row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var address = row[InspectionColumns.Address] is { } text ? Cells.Address(text) : 0;
        if (row[InspectionColumns.TypeName] is { Length: > 0 } typeName && Enum.TryParse<InstanceScope>(row[InspectionColumns.Scope], out var scope))
        {
            return new InspectionTarget(typeName, row[InspectionColumns.Module], scope, address);
        }

        return address != 0 ? new InspectionTarget(null, null, InstanceScope.All, address) : null;
    }

    private void Start()
    {
        if (Snapshot is not { } snapshot)
        {
            return;
        }

        _started = true;
        _running = new CancellationTokenSource();
        RunTask = _runAll = RunAllAsync(snapshot, _running.Token);
    }

    private async Task RunAllAsync(SnapshotHandle snapshot, CancellationToken cancellationToken)
    {
        try
        {
            Status = "Listing inspections...";
            var inspections = await _session.ListInspectionsAsync(snapshot, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // Detectors first, each group under its heading (sprint 2.6).
            string? group = null;
            foreach (var info in inspections.OrderBy(i => i.Kind == InspectionKinds.Leak ? 0 : 1))
            {
                var heading = info.Kind == InspectionKinds.Leak ? "Leak detectors" : "Memory waste";
                Tiles.Add(new InspectionTileViewModel(info)
                {
                    Status = info.Unavailable is null ? "Waiting" : null,
                    Summary = info.Unavailable,
                    GroupHeader = heading == group ? null : heading,
                });
                group = heading;
            }

            OnPropertyChanged(nameof(UsesBaseline));

            Status = null;
            foreach (var tile in Tiles.Where(t => t.IsAvailable).ToList())
            {
                await RunOneAsync(snapshot, tile, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                Selected ??= tile.HasFindings ? tile : null;
            }

            Selected ??= Tiles.FirstOrDefault(t => t.Run is not null);

            // A baseline chosen while the tiles ran (review H6): the growth tiles run with an older one run again, until
            // no other was chosen meanwhile.
            BaselineChoice? chosen;
            do
            {
                chosen = Baseline;
                await RerunAsync(snapshot, StaleTiles(), cancellationToken);
            }
            while (!cancellationToken.IsCancellationRequested && !ReferenceEquals(chosen, Baseline));
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Another snapshot (or none) is shown now.
        }
        catch (Exception ex) when (ex is WorkerException)
        {
            // What the user was shown, beside the worker's line with its id (sprint 3.12 review M17).
            _logger.LogInformation("{Section} shown as: {Message}", "Inspections", ex.Message);
            Status = ex.Message;
            CanTryAgain = true;
        }
        catch (Exception ex)
        {
            // The run is not awaited by anyone: its failure must be logged and shown, never lost (review of 1.6).
            _logger.LogError(ex, "Running the inspections failed");
            Status = "The inspections could not be run. Details are in the log; press Try again.";
            CanTryAgain = true;
        }
    }

    private async Task RunOneAsync(SnapshotHandle snapshot, InspectionTileViewModel tile, CancellationToken cancellationToken)
    {
        tile.Status = "Running...";
        try
        {
            var baseline = tile.Info.UsesBaseline ? Baseline?.Snapshot : null;
            _logger.LogInformation(
                "Inspections: running {Inspection} on {Snapshot}{Baseline}",
                tile.Info.Name,
                snapshot.Path,
                baseline is null ? string.Empty : " with the baseline " + baseline.Path);
            var run = await _session.RunInspectionAsync(snapshot, tile.Info.Name, null, cancellationToken, baseline);
            if (cancellationToken.IsCancellationRequested)
            {
                Release(run);
                return;
            }

            tile.Run = run;
            tile.RanWith = baseline;
            var result = run.Result;
            tile.Summary = string.Create(CultureInfo.CurrentCulture,
                $"{result.Count:N0} found, {Format.Bytes(result.Bytes)}{(result.WastedBytes is { } wasted ? $", {Format.Bytes(wasted)} wasted" : string.Empty)}{(baseline is not null && result.BaselineSnapshotId is not null ? $"; growth since {Path.GetFileName(baseline.Path)}" : string.Empty)}");
            tile.Note = result.Note;
            tile.HasFindings = result.Count > 0;
            var isFragmentation = tile.Info.Name == InspectionNames.Fragmentation;
            var first = await run.Rows.GetRowsAsync(0, isFragmentation ? run.Rows.Info.TotalCount : TileRows, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                // Another snapshot is shown now: nothing of this one may reach the shared bars or the grid.
                return;
            }

            var key = run.Rows.Info.Columns.Select((c, i) => (c, i)).FirstOrDefault(c => !c.c.IsHidden, (null!, -1)).i;
            if (key < 0)
            {
                throw new InvalidOperationException("The worker's result has no visible column. The worker and the app are from different builds.");
            }

            tile.FirstRows = [.. first.Rows.Take(TileRows).Select(r => r[key] ?? string.Empty)];
            if (isFragmentation)
            {
                ShowFragments(run.Rows.Info.Columns, first.Rows);
            }

            tile.Status = null;

            // A tile selected while it was still running shows its rows now.
            if (ReferenceEquals(Selected, tile))
            {
                OnSelectedChanged(tile);
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Not left "Running..." (review H6): a rerun or another snapshot replaces it.
            tile.Status = tile.Run is null ? "Waiting" : null;
            throw;
        }
        catch (Exception ex) when (ex is WorkerOperationException or WorkerCrashedException)
        {
            _logger.LogWarning(ex, "The {Inspection} inspection failed", tile.Info.Name);
            tile.Status = ex.Message;

            // A refusal is the same answer again (review of 4.6): only a failure is offered another try.
            CanTryAgain |= ex is WorkerCrashedException || (ex is WorkerOperationException { ErrorCode: var code } && code is not (HeapScopeErrorCode.InvalidArgument or HeapScopeErrorCode.CapabilityNotSupported));
        }
        catch (InvalidOperationException ex)
        {
            // A result this build cannot show (a column missing) fails its own tile only (sprint 3.12, B6): it used to end
            // the whole run, and every inspection after it reported as failed.
            _logger.LogError(ex, "The {Inspection} inspection's result could not be shown", tile.Info.Name);
            tile.Status = "This result could not be shown: " + ex.Message;
            CanTryAgain = true;
        }
    }

    private void ShowFragments(IReadOnlyList<ResultColumn> columns, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        int At(string name) => ResultColumns.Require(columns, name);
        var (heap, live, free, pinned, pinnedBytes) = (At(InspectionColumns.Heap), At(InspectionColumns.Live), At(InspectionColumns.Free), At(InspectionColumns.PinnedObjects), At(InspectionColumns.PinnedBytes));
        Fragments.Clear();
        foreach (var row in rows)
        {
            Fragments.Add(new FragmentItem(
                row[heap] ?? "?",
                Cells.Long(row, live),
                row[free] is null ? null : Cells.Long(row, free),
                Cells.Long(row, pinned),
                Cells.Long(row, pinnedBytes)));
        }

        OnPropertyChanged(nameof(HasFragments));
    }

    private void Release(InspectionRun? run) => run?.Rows.ReleaseInBackground(_logger);

    private static VirtualRowList<CellRowItem> Empty() =>
        CellRowItem.CreateList(null, new ResultSetInfo(Guid.Empty, 0, []));
}
