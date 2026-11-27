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

using HeapScope.Application.Snapshots;
using HeapScope.UI.Services;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// The analysis of one open snapshot (peer review 4.5 H13): its types grid, its analysis path, its inspections and its retainers,
/// each pointed at that snapshot once. The Snapshots page keeps one per open snapshot and shows the current one, so switching
/// snapshots leaves every view as it was, filters, sort, steps and results included, and queries nothing again. Compare and the
/// bookmarks stay on the page, across workspaces. Analysis tabs (several workspaces of one snapshot) build on this.
/// </summary>
public sealed class SnapshotWorkspaceViewModel : IDisposable
{
    private bool _shown;
    private bool _disposed;

    /// <param name="snapshot">The snapshot, or null for the page's empty workspace while nothing is selected.</param>
    /// <param name="navigator">Where the views' links go: the page, which shows the target's workspace first.</param>
    public SnapshotWorkspaceViewModel(SnapshotHandle? snapshot, SnapshotSession session, IUiDispatcher dispatcher, INavigator navigator, ILoggerFactory? loggers = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(navigator);
        Snapshot = snapshot;
        Types = new TypeSummaryViewModel(session, dispatcher, loggers?.CreateLogger<TypeSummaryViewModel>(), navigator);
        Navigator = new NavigatorViewModel(session, Types, navigator, loggers);
        Inspections = new InspectionsViewModel(session, loggers?.CreateLogger<InspectionsViewModel>(), navigator);
        Retainers = new RetainersViewModel(session, dispatcher, loggers?.CreateLogger<RetainersViewModel>(), navigator, loggers?.CreateLogger<RetainedBySetViewModel>());
    }

    /// <summary>The snapshot this workspace analyzes; null for the empty one.</summary>
    public SnapshotHandle? Snapshot { get; }

    /// <summary>The types grid, the root of the analysis path.</summary>
    public TypeSummaryViewModel Types { get; }

    /// <summary>The Snapshot tab's subjects with back and forward: this snapshot's analysis path (ADR-029).</summary>
    public NavigatorViewModel Navigator { get; }

    public InspectionsViewModel Inspections { get; }

    public RetainersViewModel Retainers { get; }

    /// <summary>Whether the workspace has been shown, and so has queried its snapshot.</summary>
    public bool IsShown => _shown;

    /// <summary>
    /// Points every view at the snapshot the first time the workspace is shown; later calls change nothing, which is the point:
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
        Inspections.Show(Snapshot);
        Retainers.Show(Snapshot);
    }

    /// <summary>
    /// The page shows another workspace: no view of this one queries while hidden, and an inspection run that has not finished
    /// stops, to run again when shown; everything finished stays.
    /// </summary>
    public void Hide()
    {
        Inspections.IsActive = false;
        Retainers.IsActive = false;
        Inspections.Suspend();
    }

    /// <summary>Which of the page's tabs is showing, so a view queries only while its tab is.</summary>
    public void SetActiveTab(SnapshotTabs tab)
    {
        Inspections.IsActive = tab == SnapshotTabs.Inspections;
        Retainers.IsActive = tab == SnapshotTabs.Retainers;
    }

    /// <summary>Closes the path's steps and lets go of every view's results, so their result sets in the worker go too.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Inspections.IsActive = false;
        Retainers.IsActive = false;
        if (Snapshot is { } snapshot)
        {
            Navigator.Forget(snapshot);
        }

        Types.Show(null);
        Inspections.Show(null);
        Retainers.Show(null);
    }
}
