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
using HeapScope.UI.Services;
using HeapScope.UI.ViewModels;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>Each open snapshot keeps its own workspace on the Snapshots page (peer review 4.5 H13).</summary>
public sealed class WorkspaceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));
    private readonly FakeWorkerClient _worker = new();
    private readonly SnapshotsViewModel _snapshots;

    public WorkspaceTests()
    {
        Directory.CreateDirectory(_folder);
        var operations = new WorkerOperations(_worker, new JobTracker(TimeProvider.System));
        _snapshots = new SnapshotsViewModel(TestSessions.Create(_worker, operations.Jobs, () => new PhysicalMemory(32L << 30, 20L << 30)), operations, new ImmediateUiDispatcher(), new FakeUserInteraction());
        _worker.Service.Rows.Add(["System.String", "600", "36000"]);
        _worker.Service.Rows.Add(["System.Byte[]", "400", "12000"]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Switching_snapshots_shows_each_workspace_as_it_was_without_a_new_query()
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
    }

    [Fact]
    public async Task Closing_a_snapshot_drops_its_workspace_and_releases_its_results()
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
    }

    [Fact]
    public async Task With_nothing_selected_the_empty_workspace_is_shown()
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp"));
        await _snapshots.Types.QueryTask;

        _snapshots.Selected = null;

        _snapshots.Current.Snapshot.ShouldBeNull();
        _snapshots.Types.Rows.Count.ShouldBe(0);
        _snapshots.Workspaces.ShouldHaveSingleItem("a's workspace stays while a is open");
    }

    [Fact]
    public async Task A_link_into_a_snapshot_not_shown_shows_its_workspace_and_step()
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
    }

    [Fact]
    public async Task A_link_of_a_closed_snapshot_is_refused_with_why()
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
    }

    [Fact]
    public async Task The_worker_is_told_which_snapshot_is_shown()
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
    }

    public void Dispose()
    {
        _snapshots.Dispose();
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

    private string Dump(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, new byte[1024]);
        return path;
    }
}
