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

using System.Globalization;
using System.IO;
using HeapScope.Application;
using HeapScope.Application.Jobs;
using HeapScope.Application.Snapshots;
using HeapScope.UI.ViewModels;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>
/// A snapshot's analysis tabs (ADR-045): each with its own types grid, path and retainers, the inspections shared, links landing
/// in their tab, and the worker told the tab shown. On a UI thread of their own, as the page and workspace tests.
/// </summary>
public sealed class AnalysisTabsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));
    private readonly FakeWorkerClient _worker = new();
    private readonly SingleThreadUiDispatcher _ui = new();
    private readonly SnapshotsViewModel _snapshots;

    public AnalysisTabsTests()
    {
        Directory.CreateDirectory(_folder);
        var operations = new WorkerOperations(_worker, new JobTracker(TimeProvider.System));
        _snapshots = _ui.Invoke(() => new SnapshotsViewModel(TestSessions.Create(_worker, operations.Jobs, () => new PhysicalMemory(32L << 30, 20L << 30)), operations, _ui, new FakeUserInteraction()));
        _ui.WatchPage(_snapshots);
        _worker.Service.Rows.Add(["System.String", "600", "36000"]);
        _worker.Service.Rows.Add(["System.Byte[]", "400", "12000"]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public Task A_second_tab_keeps_its_own_filter_and_path() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        await _snapshots.Types.QueryTask;
        var a = _snapshots.Selected!;
        var first = _snapshots.Current;
        first.Types.Filter = "String";
        await WaitUntilAsync(() => _worker.Service.SummaryQueries.Count == 2);
        await first.Types.QueryTask;

        _snapshots.NewTabCommand.Execute(null);
        var second = _snapshots.Current;
        await second.Types.QueryTask;
        second.ShouldNotBeSameAs(first);
        _snapshots.Tabs.ShouldBe([first, second]);
        second.Types.Filter.ShouldBeNullOrEmpty("a new tab starts at the types grid");
        _snapshots.Navigate(NavigationTarget.Instances(a.Handle, "App.Order", "App.dll"));
        second.Navigator.Current.ShouldBeOfType<InstancesViewModel>();
        second.Title.ShouldNotBe("Types");

        var queries = _worker.Service.SummaryQueries.Count;
        _snapshots.CurrentTab = first;
        _snapshots.Current.ShouldBeSameAs(first);
        first.Types.Filter.ShouldBe("String");
        first.Navigator.IsTypesShown.ShouldBeTrue("the first tab's path is its own");
        _worker.Service.SummaryQueries.Count.ShouldBe(queries, "a tab shown again queries nothing");
    });

    [Fact]
    public Task The_inspections_run_once_for_the_tabs_of_a_snapshot() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        _snapshots.Tab = SnapshotTabs.Inspections;
        var inspections = _snapshots.Inspections;
        await inspections.RunTask;
        var runs = _worker.Service.InspectionRuns.Count;

        _snapshots.NewTabCommand.Execute(null);

        _snapshots.Inspections.ShouldBeSameAs(inspections);
        await inspections.RunTask;
        _worker.Service.InspectionRuns.Count.ShouldBe(runs);
    });

    [Fact]
    public Task Closing_a_tab_releases_its_sets_and_the_last_tab_stays() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        await _snapshots.Types.QueryTask;
        var first = _snapshots.Current;
        _snapshots.CloseTabCommand.Execute(null);
        _snapshots.Tabs.ShouldBe([first], "the last tab of a snapshot stays");

        _snapshots.NewTabCommand.Execute(null);
        var second = _snapshots.Current;
        await second.Types.QueryTask;
        var released = _worker.Service.Released.Count;

        _snapshots.CloseTabCommand.Execute(null);

        _snapshots.Tabs.ShouldBe([first]);
        _snapshots.Current.ShouldBeSameAs(first);
        await WaitUntilAsync(() => _worker.Service.Released.Count > released);
    });

    [Fact]
    public Task A_snapshot_opens_at_most_eight_tabs_and_says_so() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        for (var i = 1; i < SnapshotAnalysisViewModel.MaxTabs; i++)
        {
            _snapshots.NewTabCommand.Execute(null);
        }

        _snapshots.Tabs.Count.ShouldBe(8);
        _snapshots.NewTabCommand.Execute(null);

        _snapshots.Tabs.Count.ShouldBe(8);
        _snapshots.Status.ShouldBe(string.Format(CultureInfo.CurrentCulture, InfoTexts.TabLimit, 8));
    });

    [Fact]
    public Task A_link_lands_in_the_tab_it_came_from_and_one_into_another_snapshot_in_that_snapshots_tab() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        var a = _snapshots.Selected!;
        var first = _snapshots.Current;
        _snapshots.NewTabCommand.Execute(null);
        var second = _snapshots.Current;

        // From the second tab's own view: it stays in the second tab.
        second.Navigate(NavigationTarget.Instances(a.Handle, "App.Order", "App.dll"));
        _snapshots.Current.ShouldBeSameAs(second);
        second.Navigator.Current.ShouldBeOfType<InstancesViewModel>();
        first.Navigator.IsTypesShown.ShouldBeTrue();

        // Into another snapshot: that snapshot's current tab.
        await _snapshots.OpenPathAsync(Dump("b.dmp"));
        var b = _snapshots.Selected!;
        var bTab = _snapshots.Current;
        _snapshots.Selected = a;
        _snapshots.Current.ShouldBeSameAs(second, "a snapshot shows the tab it showed last");
        second.Navigate(NavigationTarget.Instances(b.Handle, "App.Order", "App.dll"));
        _snapshots.Selected.ShouldBeSameAs(b);
        _snapshots.Current.ShouldBeSameAs(bTab);
        bTab.Navigator.Current.ShouldBeOfType<InstancesViewModel>();
    });

    [Fact]
    public Task A_retainers_link_into_a_snapshot_points_its_tab_before_it_queries() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("b.dmp"));
        var b = _snapshots.Selected!;
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        _snapshots.Tab = SnapshotTabs.Retainers;
        await WaitUntilAsync(() => _worker.Service.IcicleQueries.Count > 0);

        _snapshots.Navigate(NavigationTarget.Retainers(b.Handle, address: 0x1000));

        _snapshots.Selected.ShouldBeSameAs(b);
        _snapshots.Tab.ShouldBe(SnapshotTabs.Retainers);
        await WaitUntilAsync(() => _worker.Service.IcicleQueries.Any(q => q.SnapshotId == b.Handle.Descriptor.SnapshotId));
        _worker.Service.IcicleQueries.First(q => q.SnapshotId == b.Handle.Descriptor.SnapshotId).FocusAddress.ShouldBe(0x1000UL, "the link's object, not the biggest retainers");
    });

    [Fact]
    public Task The_worker_is_told_the_tab_shown() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        var first = _snapshots.Current;
        await WaitUntilAsync(() => Scopes().LastOrDefault() == first.Id);

        _snapshots.NewTabCommand.Execute(null);
        var second = _snapshots.Current;
        await WaitUntilAsync(() => Scopes().LastOrDefault() == second.Id);

        _snapshots.CurrentTab = first;
        await WaitUntilAsync(() => Scopes().LastOrDefault() == first.Id);
    });

    public void Dispose()
    {
        try
        {
            _ui.Check();
        }
        finally
        {
            _ui.Invoke(() =>
            {
                _snapshots.Dispose();
                return true;
            });
            _ui.Dispose();
        }

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10, Ct);
        }

        condition().ShouldBeTrue();
    }

    private List<Guid?> Scopes()
    {
        lock (_worker.Service.Shown)
        {
            return [.. _worker.Service.ShownScopes];
        }
    }

    private string Dump(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, new byte[1024]);
        return path;
    }
}
