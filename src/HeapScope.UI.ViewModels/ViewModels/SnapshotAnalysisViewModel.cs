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
using CommunityToolkit.Mvvm.ComponentModel;
using HeapScope.Application.Snapshots;
using HeapScope.UI.Services;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// The analysis of one open snapshot (ADR-045): its tabs, each a <see cref="SnapshotWorkspaceViewModel"/> with its own types grid,
/// path and retainers, and the inspections they share, which run once per snapshot. The Snapshots page keeps one per open
/// snapshot and shows its current tab. A snapshot has at least one tab and at most <see cref="MaxTabs"/>.
/// </summary>
public sealed partial class SnapshotAnalysisViewModel : ObservableObject, INavigator, IDisposable
{
    /// <summary>The most tabs a snapshot opens; each holds its grids' rows and its worker result sets.</summary>
    public const int MaxTabs = 8;

    private readonly SnapshotSession _session;
    private readonly IUiDispatcher _dispatcher;
    private readonly INavigator _page;
    private readonly Action<SnapshotWorkspaceViewModel, NavigationTarget> _navigateIn;
    private readonly ILoggerFactory? _loggers;
    private bool _shown;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCloseTab))]
    private SnapshotWorkspaceViewModel _current;

    /// <param name="snapshot">The snapshot, or null for the page's empty analysis while nothing is selected.</param>
    /// <param name="page">Where a link into another snapshot goes.</param>
    /// <param name="navigateIn">How the page opens a link of this snapshot in one of its tabs.</param>
    public SnapshotAnalysisViewModel(
        SnapshotHandle? snapshot,
        SnapshotSession session,
        IUiDispatcher dispatcher,
        INavigator page,
        Action<SnapshotWorkspaceViewModel, NavigationTarget> navigateIn,
        ILoggerFactory? loggers = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _page = page ?? throw new ArgumentNullException(nameof(page));
        _navigateIn = navigateIn ?? throw new ArgumentNullException(nameof(navigateIn));
        _loggers = loggers;
        Snapshot = snapshot;
        Inspections = new InspectionsViewModel(session, loggers?.CreateLogger<InspectionsViewModel>(), this);
        _current = AddTab();
    }

    /// <summary>The snapshot analyzed; null for the empty one.</summary>
    public SnapshotHandle? Snapshot { get; }

    /// <summary>The snapshot's inspections, shared by its tabs.</summary>
    public InspectionsViewModel Inspections { get; }

    /// <summary>The snapshot's tabs, in the order they were opened.</summary>
    public ObservableCollection<SnapshotWorkspaceViewModel> Tabs { get; } = [];

    /// <summary>Whether another tab may open.</summary>
    public bool CanAddTab => Tabs.Count < MaxTabs;

    /// <summary>Whether the current tab may close: never the last one (closing the snapshot closes it).</summary>
    public bool CanCloseTab => Tabs.Count > 1;

    /// <summary>Whether the analysis has been shown, and so its inspections pointed at the snapshot.</summary>
    public bool IsShown => _shown;

    /// <summary>Opens a tab at the types grid, after the others; the page shows it.</summary>
    public SnapshotWorkspaceViewModel AddTab()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!CanAddTab)
        {
            throw new InvalidOperationException($"A snapshot opens at most {MaxTabs} tabs.");
        }

        var tab = new SnapshotWorkspaceViewModel(Snapshot, Inspections, _session, _dispatcher, _page, _navigateIn, _loggers);
        Tabs.Add(tab);
        OnPropertyChanged(nameof(CanAddTab));
        OnPropertyChanged(nameof(CanCloseTab));
        return tab;
    }

    /// <summary>Closes a tab that is not shown, with its path and results; the page shows another one first.</summary>
    public void RemoveTab(SnapshotWorkspaceViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (tab == Current || !Tabs.Remove(tab))
        {
            throw new InvalidOperationException("Only a tab of this snapshot that is not shown closes.");
        }

        tab.Dispose();
        OnPropertyChanged(nameof(CanAddTab));
        OnPropertyChanged(nameof(CanCloseTab));
    }

    /// <summary>Points the inspections at the snapshot the first time the analysis is shown.</summary>
    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_shown)
        {
            return;
        }

        _shown = true;
        Inspections.Show(Snapshot);
    }

    /// <summary>
    /// The page shows another snapshot: the inspections query nothing while hidden, and a run that has not finished stops, to
    /// run again when shown.
    /// </summary>
    public void Hide()
    {
        Inspections.IsActive = false;
        Inspections.Suspend();
    }

    /// <summary>Which of the page's tabs is showing, so the inspections query only while theirs is.</summary>
    public void SetActiveTab(SnapshotTabs tab)
    {
        if (!_disposed)
        {
            Inspections.IsActive = tab == SnapshotTabs.Inspections;
        }
    }

    /// <summary>An inspection row's link lands in the tab shown; one into another snapshot goes to the page.</summary>
    public void Navigate(NavigationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Snapshot == Snapshot)
        {
            _navigateIn(Current, target);
        }
        else
        {
            _page.Navigate(target);
        }
    }

    /// <summary>Closes every tab and lets go of the inspections' results.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var tab in Tabs)
        {
            tab.Dispose();
        }

        Inspections.IsActive = false;
        Inspections.Show(null);
    }
}
