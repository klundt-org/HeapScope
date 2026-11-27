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
using HeapScope.Contracts;
using HeapScope.UI.Services;
using HeapScope.UI.ViewModels;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>
/// Each open snapshot keeps its own workspace on the Snapshots page (peer review 4.5 H13). The tests run on a UI thread of
/// their own (M15): every change of the page and of its workspaces' views must be raised there.
/// </summary>
public sealed class WorkspaceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));
    private readonly FakeWorkerClient _worker = new();
    private readonly SingleThreadUiDispatcher _ui = new();
    private readonly SnapshotsViewModel _snapshots;

    public WorkspaceTests()
    {
        Directory.CreateDirectory(_folder);
        var operations = new WorkerOperations(_worker, new JobTracker(TimeProvider.System));
        _snapshots = _ui.Invoke(() => new SnapshotsViewModel(TestSessions.Create(_worker, operations.Jobs, () => new PhysicalMemory(32L << 30, 20L << 30)), operations, _ui, new FakeUserInteraction()));
        _ui.WatchPage(_snapshots);
        _worker.Service.Rows.Add(["System.String", "600", "36000"]);
        _worker.Service.Rows.Add(["System.Byte[]", "400", "12000"]);
    }

    [Fact]
    public async Task A_change_off_the_UI_thread_is_caught()
    {
        await _ui.RunAsync(() => _snapshots.OpenPathAsync(Dump("a.dmp")));

        await Task.Run(() => _snapshots.Status = "set elsewhere", Ct);

        _ui.Violations.ShouldContain(v => v.StartsWith("page.Status changed on thread", StringComparison.Ordinal));
        var failed = false;
        try
        {
            await _ui.RunCheckedAsync(() => Task.CompletedTask);
        }
        catch (ShouldAssertException)
        {
            failed = true;
        }

        failed.ShouldBeTrue("a checked run fails on a change made off the UI thread");
        _ui.ForgetViolations();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public Task Switching_snapshots_shows_each_workspace_as_it_was_without_a_new_query() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        await _snapshots.Types.QueryTask;
        var a = _snapshots.Selected!;
        var aTypes = _snapshots.Types;
        aTypes.Filter = "String";
        await WaitUntilAsync(() => _worker.Service.SummaryQueries.Count == 2);
        await aTypes.QueryTask;
        await Task.Delay(TypeSummaryViewModel.FilterDelay * 2, Ct);
        _snapshots.Tab = SnapshotTabs.Inspections;
        await _snapshots.Inspections.RunTask;
        var aInspections = _snapshots.Inspections;
        var aRuns = aInspections.Tiles.Select(t => t.Run).ToList();
        aRuns.ShouldContain(r => r != null);
        var runs = _worker.Service.InspectionRuns.Count;

        await _snapshots.OpenPathAsync(Dump("b.dmp"));
        await _snapshots.Types.QueryTask;
        await _snapshots.Inspections.RunTask;
        _snapshots.Types.ShouldNotBeSameAs(aTypes);
        _snapshots.Types.Filter.ShouldBeNullOrEmpty("b's grid has its own filter");
        var queries = _worker.Service.SummaryQueries.Count;
        var allRuns = _worker.Service.InspectionRuns.Count;
        allRuns.ShouldBeGreaterThan(runs, "b runs its own inspections");

        _snapshots.Selected = a;

        _snapshots.Current.Snapshot.ShouldBe(a.Handle);
        _snapshots.Types.ShouldBeSameAs(aTypes);
        _snapshots.Inspections.ShouldBeSameAs(aInspections);
        aTypes.Filter.ShouldBe("String");
        aInspections.Tiles.Select(t => t.Run).ShouldBe(aRuns);
        _worker.Service.SummaryQueries.Count.ShouldBe(queries, "a's grid is shown as it was");
        _worker.Service.InspectionRuns.Count.ShouldBe(allRuns, "a's inspections are shown as they were");
        _snapshots.Workspaces.Count.ShouldBe(2);
    });

    [Fact]
    public Task Closing_a_snapshot_drops_its_workspace_and_releases_its_results() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        await _snapshots.Types.QueryTask;
        var a = _snapshots.Selected!;
        _snapshots.Tab = SnapshotTabs.Inspections;
        await _snapshots.Inspections.RunTask;
        var kept = _snapshots.Inspections.Tiles.Where(t => t.Run is not null).Select(t => t.Run!.Rows.Info.Id).ToList();
        kept.ShouldNotBeEmpty();

        await _snapshots.OpenPathAsync(Dump("b.dmp"));
        await _snapshots.Inspections.RunTask;
        kept.ShouldAllBe(id => _worker.Service.LiveSets.Contains(id), "a hidden workspace keeps its results");

        await _snapshots.CloseCommand.ExecuteAsync(a);

        _snapshots.Workspaces.ShouldHaveSingleItem().Snapshot!.Path.ShouldEndWith("b.dmp", Case.Sensitive);
        await WaitUntilAsync(() => kept.All(id => !_worker.Service.LiveSets.Contains(id)));
    });

    [Fact]
    public Task With_nothing_selected_the_empty_workspace_is_shown() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        await _snapshots.Types.QueryTask;

        _snapshots.Selected = null;

        _snapshots.Current.Snapshot.ShouldBeNull();
        _snapshots.Types.Rows.Count.ShouldBe(0);
        _snapshots.Workspaces.ShouldHaveSingleItem("a's workspace stays while a is open");
    });

    [Fact]
    public Task A_link_into_a_snapshot_not_shown_shows_its_workspace_and_step() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        var a = _snapshots.Selected!;
        var aWorkspace = _snapshots.Current;
        await _snapshots.OpenPathAsync(Dump("b.dmp"));
        var bWorkspace = _snapshots.Current;
        _snapshots.Tab = SnapshotTabs.Inspections;

        _snapshots.Navigate(NavigationTarget.Instances(a.Handle, "App.Order", "App.dll"));

        _snapshots.Selected.ShouldBeSameAs(a);
        _snapshots.Current.ShouldBeSameAs(aWorkspace);
        _snapshots.Tab.ShouldBe(SnapshotTabs.Snapshot);
        aWorkspace.Navigator.Current.ShouldBeOfType<InstancesViewModel>().TypeName.ShouldBe("App.Order");
        bWorkspace.Navigator.IsTypesShown.ShouldBeTrue("b's path is untouched");
    });

    [Fact]
    public Task A_link_of_a_closed_snapshot_is_refused_with_why() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        var a = _snapshots.Selected!.Handle;
        await _snapshots.OpenPathAsync(Dump("b.dmp"));
        var b = _snapshots.Current;
        await _snapshots.CloseCommand.ExecuteAsync(_snapshots.Snapshots.Single(s => s.Handle == a));
        _snapshots.Selected = _snapshots.Snapshots.Single();

        _snapshots.Navigate(NavigationTarget.Instances(a, "App.Order", "App.dll"));

        _snapshots.Current.ShouldBeSameAs(b);
        b.Navigator.IsTypesShown.ShouldBeTrue("nothing of a lands in b's workspace");
        _snapshots.Status.ShouldBe(string.Format(CultureInfo.CurrentCulture, InfoTexts.LinkOfClosedSnapshot, "a.dmp"));
    });

    [Fact]
    public Task The_worker_is_told_which_snapshot_is_shown() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        var a = _snapshots.Selected!;
        await _snapshots.OpenPathAsync(Dump("b.dmp"));
        var b = _snapshots.Selected!;
        await WaitUntilAsync(() => Shown().LastOrDefault() == b.Handle.Descriptor.SnapshotId);

        _snapshots.Selected = a;
        await WaitUntilAsync(() => Shown().LastOrDefault() == a.Handle.Descriptor.SnapshotId);

        _snapshots.Selected = null;
        await WaitUntilAsync(() => Shown() is [.., null]);
    });

    [Fact]
    public Task Switching_keeps_the_sort_the_path_and_the_retainers_shown() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        await _snapshots.Types.QueryTask;
        var a = _snapshots.Selected!;
        var workspace = _snapshots.Current;
        workspace.Types.SortByCommand.Execute(TypeSummaryColumns.Name);
        await workspace.Types.QueryTask;
        var sort = workspace.Types.SortState;
        _snapshots.Navigate(NavigationTarget.Instances(a.Handle, "App.Order", "App.dll"));
        _snapshots.Navigate(NavigationTarget.Retainers(a.Handle, address: 0x1000));
        var steps = workspace.Navigator.Trail.Select(s => s.Title).ToList();
        workspace.Retainers.FocusAddress.ShouldBe(0x1000UL);

        await _snapshots.OpenPathAsync(Dump("b.dmp"));
        await _snapshots.Types.QueryTask;
        var queries = _worker.Service.SummaryQueries.Count;
        _snapshots.Selected = a;

        _snapshots.Current.ShouldBeSameAs(workspace);
        workspace.Types.SortState.ShouldBe(sort);
        workspace.Navigator.Trail.Select(s => s.Title).ShouldBe(steps);
        workspace.Retainers.FocusAddress.ShouldBe(0x1000UL);
        _worker.Service.SummaryQueries.Count.ShouldBe(queries);
    });

    [Fact]
    public Task A_retainers_link_into_another_snapshot_lands_in_its_workspace() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        var a = _snapshots.Selected!;
        var aWorkspace = _snapshots.Current;
        await _snapshots.OpenPathAsync(Dump("b.dmp"));
        var bWorkspace = _snapshots.Current;
        var bSteps = bWorkspace.Navigator.Trail.Count;

        _snapshots.Navigate(NavigationTarget.Retainers(a.Handle, typeName: "App.Order", module: "App.dll"));

        _snapshots.Current.ShouldBeSameAs(aWorkspace);
        _snapshots.Tab.ShouldBe(SnapshotTabs.Retainers);
        aWorkspace.Navigator.CurrentStep!.Target.ShouldNotBeNull().Kind.ShouldBe(NavigationKind.Retainers);
        bWorkspace.Navigator.Trail.Count.ShouldBe(bSteps, "b's path is untouched");
    });

    [Fact]
    public Task A_workspace_shown_again_runs_only_what_the_worker_evicted() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        var a = _snapshots.Selected!;
        _snapshots.Tab = SnapshotTabs.Inspections;
        var inspections = _snapshots.Inspections;
        await inspections.RunTask;
        var duplicates = inspections.Tiles.Single(t => t.Info.Name == "duplicate-strings");
        await _snapshots.OpenPathAsync(Dump("b.dmp"));
        await _snapshots.Inspections.RunTask;
        var before = _worker.Service.InspectionRequests.Count(r => r.SnapshotId == a.Handle.Descriptor.SnapshotId);

        // The worker evicted one of a's sets while b was shown: showing a and its rows runs that inspection alone again.
        _worker.Service.Evict(duplicates.Run!.Rows.Info.Id);
        _snapshots.Selected = a;
        inspections.Selected = inspections.Tiles.Single(t => t.Info.Name == "loh");
        inspections.Selected = duplicates;
        inspections.Rows.GetRow(0);
        await inspections.Rows.WhenLoadedAsync();

        inspections.Rows.GetRow(0).Display[0].ShouldBe("alpha");
        var again = _worker.Service.InspectionRequests.Where(r => r.SnapshotId == a.Handle.Descriptor.SnapshotId).Skip(before).Select(r => r.Name).ToList();
        again.ShouldBe(["duplicate-strings"]);
    });

    [Fact]
    public Task A_new_worker_is_told_the_shown_snapshot_once_it_is_reopened_there() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        await _snapshots.Types.QueryTask;
        var a = _snapshots.Selected!;
        var first = a.Handle.Descriptor.SnapshotId;
        await WaitUntilAsync(() => Shown().LastOrDefault() == first);
        var told = Shown().Count;

        // The worker restarted: nothing is told until a's reopen gives it an id in the new worker.
        _worker.NewGeneration();
        _snapshots.Selected = null;
        _snapshots.Selected = a;
        await WaitUntilAsync(() => Shown().Count == told + 1);
        Shown()[^1].ShouldBeNull();

        _snapshots.Types.Show(a.Handle);
        await _snapshots.Types.QueryTask;
        var reopened = a.Handle.Descriptor.SnapshotId;
        reopened.ShouldNotBe(first);
        await WaitUntilAsync(() => Shown().LastOrDefault() == reopened);
    });

    [Fact]
    public Task A_hidden_workspace_reruns_its_growth_detectors_only_when_shown_again() => _ui.RunCheckedAsync(async () =>
    {
        WithGrowthDetector();
        await _snapshots.OpenPathAsync(Dump("b.dmp"));
        var b = _snapshots.Selected!;
        _snapshots.Tab = SnapshotTabs.Inspections;
        await _snapshots.Inspections.RunTask;
        await _snapshots.OpenPathAsync(Dump("x.dmp"));
        await _snapshots.Inspections.RunTask;

        // An earlier capture of b's process opens while b is hidden: b's default baseline changes, and nothing runs for b.
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        var a = _snapshots.Selected!;
        await _snapshots.Inspections.RunTask;
        GrowthRuns(b, a).ShouldBe(0, "a hidden workspace queries nothing");

        _snapshots.Selected = b;
        await WaitUntilAsync(() => GrowthRuns(b, a) == 1);
    });

    [Fact]
    public Task A_growth_rerun_stopped_by_a_switch_keeps_the_other_results() => _ui.RunCheckedAsync(async () =>
    {
        WithGrowthDetector();
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        var a = _snapshots.Selected!;
        await _snapshots.OpenPathAsync(Dump("b.dmp"));
        var b = _snapshots.Selected!;
        _snapshots.Tab = SnapshotTabs.Inspections;
        var inspections = _snapshots.Inspections;
        await inspections.RunTask;
        var others = inspections.Tiles.Where(t => !t.Info.UsesBaseline).Select(t => t.Run).ToList();
        var othersRun = OtherRuns(b);

        // None chosen reruns the growth tile; a switch to a stops that rerun alone.
        var gate = new TaskCompletionSource();
        _worker.Service.InspectionGate = gate;
        inspections.Baseline = inspections.BaselineChoices[0];
        await WaitUntilAsync(() => GrowthRuns(b, null) == 1);
        _snapshots.Selected = a;
        await inspections.RunTask;
        inspections.Tiles.Where(t => !t.Info.UsesBaseline).Select(t => t.Run).ShouldBe(others, "finished results stay");

        gate.SetResult();
        _worker.Service.InspectionGate = null;
        await _snapshots.Inspections.RunTask;
        _snapshots.Selected = b;
        await WaitUntilAsync(() => GrowthRuns(b, null) == 2);
        await inspections.RunTask;
        OtherRuns(b).ShouldBe(othersRun, "only the growth tile ran again");
        inspections.Baseline!.Snapshot.ShouldBeNull();
    });

    [Fact]
    public Task A_chosen_None_baseline_stays_when_a_hidden_run_starts_again() => _ui.RunCheckedAsync(async () =>
    {
        WithGrowthDetector();
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        var a = _snapshots.Selected!;
        await _snapshots.OpenPathAsync(Dump("b.dmp"));
        var b = _snapshots.Selected!;

        // b's first run is held; None is chosen during it, then b is hidden before it ends.
        var gate = new TaskCompletionSource();
        _worker.Service.InspectionGate = gate;
        _snapshots.Tab = SnapshotTabs.Inspections;
        var inspections = _snapshots.Inspections;
        inspections.Baseline = inspections.BaselineChoices[0];
        _snapshots.Selected = a;
        gate.SetResult();
        _worker.Service.InspectionGate = null;
        await _snapshots.Inspections.RunTask;

        _snapshots.Selected = b;
        await inspections.RunTask;
        inspections.Baseline!.Snapshot.ShouldBeNull("the chosen None stays");
        _worker.Service.InspectionRequests.Last(r => r.Name == Growth && r.SnapshotId == b.Handle.Descriptor.SnapshotId).BaselineSnapshotId.ShouldBeNull();
    });

    public void Dispose()
    {
        try
        {
            // Work that ended after the test body is checked too.
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

    private List<Guid?> Shown()
    {
        lock (_worker.Service.Shown)
        {
            return [.. _worker.Service.Shown];
        }
    }

    private const string Growth = "growing-static-collections";

    // A detector that uses a baseline, and capture times of one process: a, then b, then x.
    private void WithGrowthDetector()
    {
        _worker.Service.Inspections.Insert(0, new InspectionInfo(Growth, "Growing static collections", "Grows.", "Bound it.", null, InspectionKinds.Leak, UsesBaseline: true));
        var first = DateTimeOffset.UtcNow.AddMinutes(-10);
        _worker.Service.CapturedAt = path => Path.GetFileName(path) switch
        {
            "a.dmp" => first,
            "b.dmp" => first.AddMinutes(2),
            _ => first.AddMinutes(4),
        };
    }

    private int GrowthRuns(SnapshotItemViewModel snapshot, SnapshotItemViewModel? baseline) =>
        _worker.Service.InspectionRequests.Count(r => r.Name == Growth && r.SnapshotId == snapshot.Handle.Descriptor.SnapshotId && r.BaselineSnapshotId == baseline?.Handle.Descriptor.SnapshotId);

    private int OtherRuns(SnapshotItemViewModel snapshot) =>
        _worker.Service.InspectionRequests.Count(r => r.Name != Growth && r.SnapshotId == snapshot.Handle.Descriptor.SnapshotId);

    private string Dump(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, new byte[1024]);
        return path;
    }
}
