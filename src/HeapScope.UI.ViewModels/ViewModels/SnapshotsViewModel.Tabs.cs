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
using CommunityToolkit.Mvvm.Input;
using HeapScope.Application.Snapshots;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>The Snapshots page's analyses and their tabs (ADR-044, ADR-045): which is shown, opening and closing tabs, and where links land.</summary>
public sealed partial class SnapshotsViewModel
{
    /// <summary>The selected snapshot's analysis: its tabs and its inspections; the empty one while nothing is selected.</summary>
    public SnapshotAnalysisViewModel CurrentAnalysis => _analysis;

    /// <summary>The tab shown: the selected snapshot's current tab, or the empty one.</summary>
    public SnapshotWorkspaceViewModel Current => _analysis.Current;

    /// <summary>The selected snapshot's tabs, for the tab strip.</summary>
    public ObservableCollection<SnapshotWorkspaceViewModel> Tabs => _analysis.Tabs;

    /// <summary>The tab strip's selection: setting it shows that tab.</summary>
    public SnapshotWorkspaceViewModel CurrentTab
    {
        get => _analysis.Current;
        set
        {
            if (value is not null && value != _analysis.Current && _analysis.Tabs.Contains(value))
            {
                ShowTab(value, "chosen");
            }
        }
    }

    /// <summary>The tabs of every open snapshot that has been shown, for tests and the log.</summary>
    public IReadOnlyCollection<SnapshotWorkspaceViewModel> Workspaces => [.. _analyses.Values.SelectMany(a => a.Tabs)];

    /// <summary>
    /// Whether the tab strip shows: a snapshot is selected and the page shows a view of one tab (Snapshot or Retainers). Compare
    /// spans snapshots and the inspections are shared by the tabs, so neither has tabs of its own.
    /// </summary>
    public bool ShowsTabStrip => _analysis.Snapshot is not null && Tab is SnapshotTabs.Snapshot or SnapshotTabs.Retainers;

    /// <summary>The analyses of the open snapshots that have been shown.</summary>
    public IReadOnlyCollection<SnapshotAnalysisViewModel> Analyses => _analyses.Values;

    /// <summary>Opens a tab of the selected snapshot at its types grid and shows it; the ninth is refused with why.</summary>
    [RelayCommand(CanExecute = nameof(ShowsTabStrip))]
    private void NewTab()
    {
        if (_analysis.Snapshot is not { } snapshot || !ShowsTabStrip)
        {
            return;
        }

        if (!_analysis.CanAddTab)
        {
            Status = string.Format(CultureInfo.CurrentCulture, InfoTexts.TabLimit, SnapshotAnalysisViewModel.MaxTabs);
            _logger?.LogInformation("New tab of {Path} refused: {Max} tabs open", snapshot.Path, SnapshotAnalysisViewModel.MaxTabs);
            return;
        }

        var tab = _analysis.AddTab();
        ShowTab(tab, "new");
    }

    /// <summary>Closes a tab of the selected snapshot (the current one without a parameter), with its path and results; never the last one.</summary>
    [RelayCommand(CanExecute = nameof(CanCloseTab))]
    private void CloseTab(SnapshotWorkspaceViewModel? tab)
    {
        tab ??= _analysis.Current;
        if (_analysis.Snapshot is not { } snapshot || !_analysis.Tabs.Contains(tab) || !_analysis.CanCloseTab)
        {
            return;
        }

        var index = _analysis.Tabs.IndexOf(tab);
        var count = _analysis.Tabs.Count;
        if (tab == _analysis.Current)
        {
            ShowTab(_analysis.Tabs[index == _analysis.Tabs.Count - 1 ? index - 1 : index + 1], "the tab shown closed");
        }

        _analysis.RemoveTab(tab);
        _logger?.LogInformation("Tab {Tab} of {Path} closed ({Index} of {Count}): its types grid, path and retainers, and their result sets, released ({Open} tabs open)", tab.Id, snapshot.Path, index + 1, count, _analysis.Tabs.Count);
        RaiseTabs();
    }

    private bool CanCloseTab(SnapshotWorkspaceViewModel? tab) => ShowsTabStrip && _analysis.CanCloseTab;

    /// <summary>
    /// Shows the analysis of <paramref name="snapshot"/>, made the first time, at its current tab: the views it had are shown as
    /// they were, and nothing is queried again; null shows the empty one. Without <paramref name="activate"/> the page's tab is
    /// not applied yet, so a link can point the tab's view first.
    /// </summary>
    private void ShowAnalysis(SnapshotHandle? snapshot, bool activate = true)
    {
        var analysis = _empty;
        if (snapshot is not null && !_analyses.TryGetValue(snapshot, out analysis!))
        {
            _analyses[snapshot] = analysis = new SnapshotAnalysisViewModel(snapshot, _session, _dispatcher, this, NavigateIn, _loggers);
        }

        if (analysis == _analysis && analysis.IsShown)
        {
            return;
        }

        var kept = analysis.IsShown;
        _analysis.Current.Navigator.StepShown -= OnStepShown;
        _analysis.Current.Hide();
        _analysis.Hide();
        _analysis = analysis;

        // The worker first, so the sets the tab's views add are its.
        _session.ShowInBackground(snapshot, _analysis.Current.Id);
        _analysis.Show();
        _analysis.Current.Navigator.StepShown += OnStepShown;
        _analysis.Current.Show();
        _logger?.LogInformation(
            "Workspace shown: {Path} (snapshot {Snapshot}), {How}, analysis tab {AnalysisTab} ({Index} of {Tabs}), page tab {Tab}, {Open} workspaces open",
            snapshot?.Path ?? "none",
            snapshot?.Descriptor.SnapshotId.ToString() ?? "none",
            kept
                ? "kept as it was; a stopped inspection run or stale growth tiles run if its Inspections tab is shown, and evicted result sets run again when paged"
                : "new, its views query the snapshot",
            _analysis.Current.Id,
            _analysis.Tabs.IndexOf(_analysis.Current) + 1,
            _analysis.Tabs.Count,
            Tab,
            _analyses.Count);
        if (activate)
        {
            Activate();
        }

        RaiseTabs();
    }

    /// <summary>
    /// Shows another tab of the selected snapshot as it was; the inspections, shared, stay as they are. Without
    /// <paramref name="activate"/> the page's tab is not applied yet, so a link can point the tab's view first.
    /// </summary>
    private void ShowTab(SnapshotWorkspaceViewModel tab, string why, bool activate = true)
    {
        _analysis.Current.Navigator.StepShown -= OnStepShown;
        _analysis.Current.Hide();
        var kept = tab.IsShown;
        _analysis.Current = tab;

        // The worker first, so the sets the tab's views add are its.
        _session.ShowInBackground(tab.Snapshot, tab.Id);
        tab.Navigator.StepShown += OnStepShown;
        tab.Show();
        _logger?.LogInformation(
            "Tab shown ({Why}): tab {Tab} of {Path}, {How}, {Index} of {Open}, page tab {PageTab}",
            why,
            tab.Id,
            tab.Snapshot?.Path ?? "none",
            kept ? "kept as it was" : "new, at the types grid",
            _analysis.Tabs.IndexOf(tab) + 1,
            _analysis.Tabs.Count,
            Tab);
        if (activate)
        {
            Activate();
        }

        RaiseTabs();
    }

    // The page's tab decides which views query: the shown tab's and the analysis's inspections.
    private void Activate()
    {
        _analysis.Current.SetActiveTab(Tab);
        _analysis.SetActiveTab(Tab);
    }

    /// <summary>Opens a link of a snapshot in one of its tabs: that tab shown, the target's page tab, and the step.</summary>
    private void NavigateIn(SnapshotWorkspaceViewModel tab, NavigationTarget target)
    {
        // A link of a snapshot not shown, or of one closed meanwhile, goes the page's way, which refuses a closed one with why.
        if (!_analyses.TryGetValue(target.Snapshot, out var analysis) || analysis != _analysis || target.Snapshot.IsClosed)
        {
            Navigate(target);
            return;
        }

        if (tab != _analysis.Current && _analysis.Tabs.Contains(tab))
        {
            ShowTab(tab, "a link of it", activate: false);
        }

        Open(target);
    }

    // The target first, so showing the Retainers tab queries what it asks for rather than the biggest retainers. The link is
    // a step of the analysis path, replayed when it is chosen again.
    private void Open(NavigationTarget target)
    {
        var tab = _analysis.Current;
        if (target.Kind is NavigationKind.Retainers or NavigationKind.RetainedBySet)
        {
            tab.Retainers.Navigate(target);
            tab.Navigator.Record(target);
            Tab = SnapshotTabs.Retainers;
            Activate();
            return;
        }

        Tab = SnapshotTabs.Snapshot;
        Activate();
        tab.Navigator.Navigate(target);
    }

    /// <summary>Closes the analysis of a snapshot that was closed or opened again, with its tabs, paths and results.</summary>
    private void CloseAnalysis(SnapshotHandle snapshot)
    {
        if (_analyses.Remove(snapshot, out var analysis))
        {
            if (analysis == _analysis)
            {
                ShowAnalysis(null);
            }

            var tabs = analysis.Tabs.Count;
            analysis.Dispose();
            _logger?.LogInformation("Workspace of {Path} closed: its {Tabs} tabs (types grid, path, retainers), its inspections, and their result sets, released", snapshot.Path, tabs);
        }
    }

    private void RaiseTabs()
    {
        OnPropertyChanged(nameof(CurrentAnalysis));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(Tabs));
        OnPropertyChanged(nameof(CurrentTab));
        OnPropertyChanged(nameof(Types));
        OnPropertyChanged(nameof(Navigator));
        OnPropertyChanged(nameof(Inspections));
        OnPropertyChanged(nameof(Retainers));
        OnPropertyChanged(nameof(ShowsTabStrip));
        NewTabCommand.NotifyCanExecuteChanged();
        CloseTabCommand.NotifyCanExecuteChanged();
    }
}
