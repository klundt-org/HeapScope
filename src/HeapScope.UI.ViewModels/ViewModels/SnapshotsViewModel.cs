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
using HeapScope.Application;
using HeapScope.Application.Bookmarks;
using HeapScope.Application.Sessions;
using HeapScope.Application.Snapshots;
using HeapScope.Application.Workers;
using HeapScope.Contracts;
using HeapScope.UI.Services;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// The Snapshots page: open a dump, see the open snapshots, and the selected one's overview tiles, heap
/// kinds bar and types grid (dotMemory's types view).
/// </summary>
public sealed partial class SnapshotsViewModel : ObservableObject, INavigator, IDisposable
{
    private readonly SnapshotSession _session;
    private readonly WorkerOperations _operations;
    private readonly IUiDispatcher _dispatcher;
    private readonly IUserInteraction _interaction;

    [ObservableProperty]
    private SnapshotItemViewModel? _selected;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _isOpening;

    /// <summary>The detail tab's index, for the view's tab control; code uses <see cref="Tab"/>.</summary>
    [ObservableProperty]
    private int _selectedTab;

    private readonly SessionLibrary? _library;
    private readonly ILogger? _logger;
    private readonly ILoggerFactory? _loggers;

    // An analysis (tabs and inspections) per open snapshot, and the empty one shown while nothing is selected.
    private readonly Dictionary<SnapshotHandle, SnapshotAnalysisViewModel> _analyses = [];
    private readonly SnapshotAnalysisViewModel _empty;
    private SnapshotAnalysisViewModel _analysis;

    // Set while a link selects its snapshot, so the snapshot's tab is pointed at the link before its views query.
    private bool _navigating;

    public SnapshotsViewModel(
        SnapshotSession session,
        WorkerOperations operations,
        IUiDispatcher dispatcher,
        IUserInteraction interaction,
        ILoggerFactory? loggers = null,
        BookmarkStore? bookmarks = null,
        SessionLibrary? library = null,
        AllocationsViewModel? allocations = null)
    {
        _library = library;
        _logger = loggers?.CreateLogger<SnapshotsViewModel>();
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _loggers = loggers;
        _session.SnapshotsChanged += OnSnapshotsChanged;
        _empty = _analysis = new SnapshotAnalysisViewModel(null, session, dispatcher, this, NavigateIn, loggers);
        _empty.Show();
        _empty.Current.Show();
        _analysis.Current.Navigator.StepShown += OnStepShown;
        Bookmarks = new BookmarksViewModel(bookmarks, this, loggers?.CreateLogger<BookmarksViewModel>());
        Compare = new CompareViewModel(session, Snapshots, dispatcher, loggers?.CreateLogger<CompareViewModel>(), this, allocations);
    }

    /// <summary>The detail tab (named, not numbered).</summary>
    public SnapshotTabs Tab
    {
        get => (SnapshotTabs)SelectedTab;
        set => SelectedTab = (int)value;
    }

    /// <summary>
    /// Follows a link from anywhere on the page: shows the workspace of the target's snapshot, picks the tab and opens the
    /// subject there. A link of a snapshot that is no longer open is refused with why, rather than opened in another one.
    /// </summary>
    public void Navigate(NavigationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (Snapshots.FirstOrDefault(s => s.Handle == target.Snapshot) is not { } item || target.Snapshot.IsClosed)
        {
            Status = string.Format(CultureInfo.CurrentCulture, InfoTexts.LinkOfClosedSnapshot, System.IO.Path.GetFileName(target.Snapshot.Path));
            _logger?.LogInformation("A {Kind} link of {Snapshot} refused: the snapshot is no longer open", target.Kind, target.Snapshot.Descriptor.SnapshotId);
            return;
        }

        if (Selected != item)
        {
            _navigating = true;
            try
            {
                Selected = item;
            }
            finally
            {
                _navigating = false;
            }
        }

        // The snapshot's current tab, the one it shows.
        Open(target);
    }

    public ObservableCollection<SnapshotItemViewModel> Snapshots { get; } = [];

    /// <summary>The types grid of the selected snapshot (its workspace's).</summary>
    public TypeSummaryViewModel Types => _analysis.Current.Types;

    /// <summary>The Snapshot tab's subjects of the selected snapshot: the types grid, instances and objects, with back and forward.</summary>
    public NavigatorViewModel Navigator => _analysis.Current.Navigator;

    /// <summary>Bookmarked objects of every open snapshot.</summary>
    public BookmarksViewModel Bookmarks { get; }

    /// <summary>
    /// Bookmarks <paramref name="row"/>'s object when it opens one, else the object shown on the Snapshot tab, or removes
    /// its bookmark (Command+D); false when there was nothing to bookmark.
    /// </summary>
    public async Task<bool> ToggleBookmarkAsync(IObjectLink? row = null)
    {
        if (Selected is not { } selected)
        {
            return false;
        }

        if (row is { LinkAddress: not 0 and var address })
        {
            await Bookmarks.ToggleAsync(selected.Handle, address, row.LinkTypeName);
            return true;
        }

        if (Tab == SnapshotTabs.Snapshot && Navigator.Current is ObjectViewModel { Address: not 0 } shown)
        {
            await Bookmarks.ToggleAsync(shown.Snapshot, shown.Address, shown.TypeName);
            return true;
        }

        return false;
    }

    /// <summary>Bookmarks the object shown on the Snapshot tab, or removes its bookmark (the object view's button).</summary>
    [RelayCommand]
    private Task ToggleBookmark() => ToggleBookmarkAsync();

    /// <summary>The comparison of two open snapshots.</summary>
    public CompareViewModel Compare { get; }

    /// <summary>The selected snapshot's inspections; they run when their tab is shown.</summary>
    public InspectionsViewModel Inspections => _analysis.Inspections;

    /// <summary>The selected snapshot's dominator tree, icicle and set retention; queried when the tab is shown.</summary>
    public RetainersViewModel Retainers => _analysis.Current.Retainers;

    public void Dispose()
    {
        _session.SnapshotsChanged -= OnSnapshotsChanged;
        _analysis.Current.Navigator.StepShown -= OnStepShown;
        foreach (var analysis in _analyses.Values)
        {
            analysis.Dispose();
        }

        _analyses.Clear();
        _empty.Dispose();
    }

    /// <summary>
    /// Opens <paramref name="path"/>: asks first when a first load may not fit in available memory (Application decides,
    /// from the worker's look at the file and its index cache), then opens it as a tracked job and selects it. Nothing
    /// here touches the file system, so the dispatcher never waits on a slow disk.
    /// </summary>
    public async Task OpenPathAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        IsOpening = true;
        Status = $"Opening {System.IO.Path.GetFileName(path)}...";

        // The worker snapshot the dump has now, if it is open: an open that changes it was the user's way back from a refused
        // reopen. A silent reopen after a worker restart has already moved the handle on.
        var fullPath = System.IO.Path.GetFullPath(path);
        var before = _session.Snapshots.FirstOrDefault(h => PathIdentity.Same(h.Path, fullPath))?.Descriptor.SnapshotId;
        try
        {
            if (await _session.GetOpenWarningAsync(path, rebuild: false, CancellationToken.None) is { } warning
                && !await _interaction.ConfirmAsync("Not enough free memory", warning + "\n\nOpen the dump anyway?"))
            {
                Status = "Open canceled.";
                return;
            }

            SnapshotHandle handle;
            try
            {
                handle = await _session.StartOpen(path).Result;
            }
            catch (WorkerOperationException ex) when (ex is { ErrorCode: HeapScopeErrorCode.DacNotTrusted, DacTrust: { } dac } && _session.TrustedDacs is { } trusted)
            {
                // The only matching DAC came with the dump: load it only if the user says so.
                // The question and the answer are logged: "it keeps asking" must be answerable from the log.
                var yes = await _interaction.ConfirmAsync("Trust the DAC next to this dump?", TrustQuestion(dac));
                _logger?.LogInformation("Asked to trust the DAC {Sha256} at {DacPath} for {Path}; the user answered {Answer}", dac.Sha256, dac.Path, path, yes ? "yes" : "no");
                if (!yes)
                {
                    // From its index cache the graph needs no DAC: open it, and only field values and strings are refused.
                    // A dump that has to be loaded cannot be read without one.
                    try
                    {
                        handle = await _session.StartOpen(path, checkDac: false).Result;
                    }
                    catch (WorkerOperationException again) when (again.ErrorCode == HeapScopeErrorCode.DacNotTrusted)
                    {
                        Status = $"Not opened: the DAC next to the dump is not trusted. Install the .NET {dac.RuntimeVersion} runtime, put its DAC in the DAC folder on the Settings page, or open the dump again and trust it.";

                        // What the user was shown, for the log, with the worker's own text beside it.
                        _logger?.LogInformation("{Section} shown as: {Message} (the worker said: {Worker})", "Open", Status, again.Message);
                        return;
                    }

                    // The approved status, with Trust this DAC and reopen beside it.
                    ShowOpened(handle, before);
                    Status = OpenedStatus(handle);
                    return;
                }

                if (!await TrustAsync(trusted, dac, $"trusted by the user when opening {path}", "Not opened"))
                {
                    return;
                }

                handle = await _session.StartOpen(path).Result;
            }

            ShowOpened(handle, before);
            Status = OpenedStatus(handle);
        }
        catch (OperationCanceledException)
        {
            Status = "Open canceled.";
        }
        catch (Exception ex) when (ex is WorkerException)
        {
            // What the user was shown, beside the worker's line with its id.
            _logger?.LogInformation("{Section} shown as: {Message}", "Snapshot open", ex.Message);
            Status = ex.Message;
        }
        finally
        {
            IsOpening = false;
        }
    }

    /// <summary>
    /// Lists and selects what was just opened. Opening a dump again after a refused reopen keeps its handle, so the selection would
    /// not change and the page would keep the refusal and the old notes. When the open changed the handle's
    /// worker snapshot (<paramref name="before"/>), its item says so in place, so a pair picked on Compare and Trend's ticks stay
    /// and run again, and its analysis path starts again, since every step on it asked the earlier snapshot. A
    /// dump that was already open gets its item back unchanged.
    /// </summary>
    private void ShowOpened(SnapshotHandle handle, Guid? before)
    {
        Refresh();
        var item = Snapshots.FirstOrDefault(s => s.Handle == handle);
        if (item is not null && before is { } previous && previous != handle.Descriptor.SnapshotId)
        {
            _logger?.LogInformation(
                "Snapshot {Path} was opened again: worker snapshot {PreviousId} is now {Id}; its row is shown afresh and its workspace (types grid, path, inspections, retainers) starts again",
                handle.Path,
                previous,
                handle.Descriptor.SnapshotId);
            CloseAnalysis(handle);
            Compare.Reopened(item);
            if (Selected == item)
            {
                OnSelectedChanged(item);
                return;
            }
        }

        Selected = item;
    }

    /// <summary>
    /// The status after an open: a full dump whose DAC cannot be used opens without field values and strings (missing, declined, or an older cache in place of a rebuild); the notes say why, and an untrusted DAC offers Trust this
    /// DAC and reopen. Otherwise it says the snapshot is open and where to go next (owner 2026-11-19: after an
    /// open nothing said it had worked).
    /// </summary>
    private static string OpenedStatus(SnapshotHandle handle) =>
        handle.Descriptor.Kind == SnapshotKindDto.FullDump && !handle.Descriptor.IsAvailable(SnapshotFeatureIds.FieldValues)
            ? "Opened without the dump's DAC (Data Access Component): field values and strings are not shown. The snapshot's notes say why."
            : string.Format(CultureInfo.CurrentCulture, InfoTexts.OpenedStatus, System.IO.Path.GetFileName(handle.Path));

    [RelayCommand]
    private async Task OpenDumpAsync()
    {
        if (await _interaction.PickDumpFileAsync() is { } path)
        {
            await OpenPathAsync(path);
        }
    }

    [RelayCommand]
    private async Task CloseAsync(SnapshotItemViewModel? item)
    {
        item ??= Selected;
        if (item is null)
        {
            return;
        }

        try
        {
            await _session.CloseAsync(item.Handle, CancellationToken.None);
        }
        catch (Exception ex) when (ex is WorkerException)
        {
            _logger?.LogInformation("{Section} shown as: {Message}", "Snapshot close", ex.Message);
            Status = ex.Message;
        }

        Refresh();
    }

    /// <summary>
    /// Closes a snapshot and deletes its dump with its index caches, after the user confirms what
    /// goes and its size. Only a snapshot recorded in a HeapScope session is deleted: other dumps are the user's files.
    /// </summary>
    [RelayCommand]
    private async Task CloseAndDeleteAsync(SnapshotItemViewModel? item)
    {
        item ??= Selected;
        if (item is null || _library is null)
        {
            return;
        }

        var path = item.Handle.Path;
        var found = await Task.Run(() => _library.FindSnapshot(path));
        if (found is not { } entry)
        {
            _logger?.LogInformation("Deleting snapshot {Path} refused: it is not in a HeapScope session", path);
            Status = "This dump is not in a HeapScope session, so HeapScope does not delete it: close it and delete the file yourself.";
            return;
        }

        Deletion plan;
        try
        {
            plan = await Task.Run(() => _library.PlanSnapshotDeletion(entry.Session.Folder, entry.Snapshot.Id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _logger?.LogInformation("Deleting snapshot {Path} refused: {Reason}", path, ex.Message);
            Status = ex.Message;
            return;
        }

        var question = string.Create(System.Globalization.CultureInfo.CurrentCulture, $"Close {item.Title} and delete it with its index caches, {plan.Files.Count} files ({Format.Bytes(plan.Bytes)})? It is removed from the session {entry.Session.Document.Name}. This cannot be undone.");
        var yes = await _interaction.ConfirmAsync("Delete snapshot", question + Environment.NewLine + DeletionText.Files(plan));
        _logger?.LogInformation("Asked to delete snapshot {SnapshotId} ({Path}, {Files} files, {Bytes} bytes); the user answered {Answer}", entry.Snapshot.Id, path, plan.Files.Count, plan.Bytes, yes ? "yes" : "no");
        if (!yes)
        {
            return;
        }

        try
        {
            // Closed first: the worker unmaps the cache, so the files can go.
            await _session.CloseAsync(item.Handle, CancellationToken.None);
            Refresh();
            var deletion = await Task.Run(() => _library.DeleteSnapshot(entry.Session.Folder, entry.Snapshot.Id));
            DeletionText.Log(_logger, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"snapshot {entry.Snapshot.Id} of session {entry.Session.Folder}"), deletion);
            Status = deletion.Complete
                ? string.Create(System.Globalization.CultureInfo.CurrentCulture, $"Deleted {item.Title} and its caches: {Format.Bytes(deletion.Bytes)} freed.")
                : string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{deletion.Failed!.Count} files of {item.Title} could not be deleted, usually because another program has them open: {string.Join("; ", deletion.Failed)}. {(File.Exists(path) ? "The snapshot stays in its session: open it and use Close and delete again once they are free, or delete them yourself." : "The snapshot is removed from its session; delete these files yourself once they are free.")}");
        }
        catch (Exception ex) when (ex is WorkerException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _logger?.LogWarning(ex, "Deleting snapshot {Path} failed", path);
            Status = "The snapshot could not be deleted: " + ex.Message;
        }
    }

    partial void OnSelectedChanged(SnapshotItemViewModel? value)
    {
        RaiseCanTrustDac();

        // Each snapshot keeps its workspace, its analysis path included, while it is open (ADR-029).
        ShowAnalysis(value?.Handle, activate: !_navigating);
    }

    partial void OnSelectedTabChanged(int value)
    {
        OnPropertyChanged(nameof(Tab));
        OnPropertyChanged(nameof(ShowsTabStrip));
        NewTabCommand.NotifyCanExecuteChanged();
        CloseTabCommand.NotifyCanExecuteChanged();
        Activate();
        Compare.IsActive = value == (int)SnapshotTabs.Compare;
    }

    private void OnSnapshotsChanged(object? sender, EventArgs e) => _dispatcher.Post(Refresh);

    // Back, Forward or the sidebar showed a step: show its tab, replaying a Retainers link.
    private void OnStepShown(object? sender, AnalysisStep step)
    {
        if (step.Target is { } target)
        {
            Retainers.Navigate(target);
            Tab = SnapshotTabs.Retainers;
        }
        else
        {
            Tab = SnapshotTabs.Snapshot;
        }
    }

    private void Refresh()
    {
        var handles = _session.Snapshots;
        for (var i = Snapshots.Count - 1; i >= 0; i--)
        {
            if (!handles.Contains(Snapshots[i].Handle))
            {
                CloseAnalysis(Snapshots[i].Handle);
                Bookmarks.Forget(Snapshots[i].Handle);
                Snapshots.RemoveAt(i);
            }
        }

        foreach (var handle in handles)
        {
            if (Snapshots.FirstOrDefault(s => s.Handle == handle) is { } shown)
            {
                // Reopened after a worker restart, or opened again: the row shows the snapshot's new load.
                shown.Reload();
            }
            else
            {
                Snapshots.Add(new SnapshotItemViewModel(handle));
                _ = Bookmarks.LoadAsync(handle);
            }
        }

        // A reopen after a worker restart can change what the selected snapshot offers.
        RaiseCanTrustDac();
        if (Selected is not null && !Snapshots.Contains(Selected))
        {
            Selected = Snapshots.FirstOrDefault();
        }

        // Another snapshot opened or closed: the growth detectors' baseline choices follow, in every workspace.
        foreach (var analysis in _analyses.Values)
        {
            analysis.Inspections.RefreshBaselines();
        }
    }
}

