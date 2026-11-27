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

using CommunityToolkit.Mvvm.ComponentModel;
using HeapScope.Application.Snapshots;
using HeapScope.UI.Services;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// One analysis tab of an open snapshot (ADR-044, ADR-045): its types grid, its analysis path and its retainers, pointed at that
/// snapshot once, with the snapshot's inspections, which its tabs share. Switching tabs or snapshots leaves every view as it
/// was, filters, sort, steps and results included, and queries nothing again. The tab is the navigator of its own views: a link
/// of its snapshot lands in this tab, one into another snapshot goes to the page.
/// </summary>
public sealed partial class SnapshotWorkspaceViewModel : ObservableObject, INavigator, IDisposable
{
    private readonly INavigator _page;
    private readonly Action<SnapshotWorkspaceViewModel, NavigationTarget> _navigateIn;
    private bool _shown;
    private bool _disposed;

    /// <param name="snapshot">The snapshot, or null for the page's empty workspace while nothing is selected.</param>
    /// <param name="inspections">The snapshot's inspections, shared by its tabs.</param>
    /// <param name="page">Where a link into another snapshot goes: the page, which shows that snapshot's current tab.</param>
    /// <param name="navigateIn">How the page opens a link of this snapshot in a tab: the tab, the Snapshot or Retainers tab, the step.</param>
    public SnapshotWorkspaceViewModel(
        SnapshotHandle? snapshot,
        InspectionsViewModel inspections,
        SnapshotSession session,
        IUiDispatcher dispatcher,
        INavigator page,
        Action<SnapshotWorkspaceViewModel, NavigationTarget> navigateIn,
        ILoggerFactory? loggers = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dispatcher);
        Snapshot = snapshot;
        Inspections = inspections ?? throw new ArgumentNullException(nameof(inspections));
        _page = page ?? throw new ArgumentNullException(nameof(page));
        _navigateIn = navigateIn ?? throw new ArgumentNullException(nameof(navigateIn));
        Types = new TypeSummaryViewModel(session, dispatcher, loggers?.CreateLogger<TypeSummaryViewModel>(), this);
        Navigator = new NavigatorViewModel(session, Types, this, loggers);
        Retainers = new RetainersViewModel(session, dispatcher, loggers?.CreateLogger<RetainersViewModel>(), this, loggers?.CreateLogger<RetainedBySetViewModel>());
        Navigator.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NavigatorViewModel.CurrentStep))
            {
                OnPropertyChanged(nameof(Title));
            }
        };
    }

    /// <summary>Tells tabs apart in the log and to the worker, which keeps the shown tab's result sets first.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>The snapshot this tab analyzes; null for the empty one.</summary>
    public SnapshotHandle? Snapshot { get; }

    /// <summary>The tab's title: the step it shows.</summary>
    public string Title => Navigator.CurrentStep?.Title ?? "Types";

    /// <summary>The types grid, the root of the analysis path.</summary>
    public TypeSummaryViewModel Types { get; }

    /// <summary>The Snapshot tab's subjects with back and forward: this tab's analysis path (ADR-029).</summary>
    public NavigatorViewModel Navigator { get; }

    /// <summary>The snapshot's inspections, shared by its tabs (one run per snapshot).</summary>
    public InspectionsViewModel Inspections { get; }

    /// <summary>This tab's dominator tree, icicle and set retention.</summary>
    public RetainersViewModel Retainers { get; }

    /// <summary>Whether the tab has been shown, and so has queried its snapshot.</summary>
    public bool IsShown => _shown;

    /// <summary>
    /// Points every view at the snapshot the first time the tab is shown; later calls change nothing, which is the point:
    /// what was there is still there.
    /// </summary>
    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_shown)
        {
            return;
        }

        _shown = true;
        Navigator.Show(Snapshot);
        Types.Show(Snapshot);
        Retainers.Show(Snapshot);
    }

    /// <summary>The page shows another tab: no view of this one queries while hidden.</summary>
    public void Hide() => Retainers.IsActive = false;

    /// <summary>Which of the page's tabs is showing, so a view queries only while its tab is.</summary>
    public void SetActiveTab(SnapshotTabs tab)
    {
        if (!_disposed)
        {
            Retainers.IsActive = tab == SnapshotTabs.Retainers;
        }
    }

    /// <summary>A link of this snapshot lands in this tab; one into another snapshot goes to the page.</summary>
    public void Navigate(NavigationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Snapshot == Snapshot)
        {
            _navigateIn(this, target);
        }
        else
        {
            _page.Navigate(target);
        }
    }

    /// <summary>Closes the path's steps and lets go of the tab's results, so their result sets in the worker go too.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Retainers.IsActive = false;
        if (Snapshot is { } snapshot)
        {
            Navigator.Forget(snapshot);
        }

        Types.Show(null);
        Retainers.Show(null);
    }
}
