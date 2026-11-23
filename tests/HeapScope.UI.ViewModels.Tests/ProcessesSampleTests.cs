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

using System.IO;
using HeapScope.Application;
using HeapScope.Application.Jobs;
using HeapScope.Application.Sessions;
using HeapScope.Application.Tutorial;
using HeapScope.Contracts;
using HeapScope.Core;
using HeapScope.Tests.Shared;
using HeapScope.UI.Services;
using HeapScope.UI.ViewModels;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>
/// The tutorial's sample on the Processes page (sprint 3.13, task 6 and its review): its scenarios from the sample's catalog,
/// started with the ones chosen, its row marked "Sample" and selected, stopped from the page, its mark gone when it exits
/// elsewhere, a refusal shown with what to do, and a sample that cannot run never empties the process list.
/// </summary>
public sealed class ProcessesSampleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));
    private readonly FakeWorkerClient _worker = new();
    private readonly FakeUserInteraction _interaction = new();
    private readonly JobTracker _jobs = new(TimeProvider.System);
    private readonly FakeCaptureService _capture;
    private readonly FakeSampleStarter _starter = new();
    private readonly SampleService _samples;
    private readonly ProcessesViewModel _processes;

    public ProcessesSampleTests()
    {
        var operations = new WorkerOperations(_worker, _jobs);
        var snapshots = new SnapshotsViewModel(TestSessions.Create(_worker, _jobs), operations, new ImmediateUiDispatcher(), _interaction);
        _capture = new FakeCaptureService(_root, _jobs);
        _capture.Processes.Add(new ProcessEntry(10, "Api", "Api.exe", "10.0.13", ProcessElevation.NotElevated, false, 1000, 2000));
        _samples = new SampleService(_starter, () => false, () => new SampleLaunch("heapscope-sample.exe", [], "heapscope-sample.exe"), folders: new HeapScopeFolders(_root));
        _processes = new ProcessesViewModel(_capture, snapshots, _interaction, samples: _samples, dispatcher: new ImmediateUiDispatcher());
    }

    [Fact]
    public async Task A_sample_starts_with_the_chosen_scenarios_marked_and_selected_and_stops_from_the_page()
    {
        await _processes.RefreshCommand.ExecuteAsync(null);
        _processes.HasSample.ShouldBeTrue();
        _processes.SampleScenarios.Select(s => s.Title).ShouldBe(["A static cache", "Timers", "Waste", "Backlog"]);

        _processes.SampleScenarios[1].IsChosen = true;
        _processes.KeepGrowing = false;
        var pid = NextSamplePid();
        _capture.Processes.Add(new ProcessEntry(pid, "heapscope-sample", "heapscope-sample.exe --scenarios Timer", "10.0.14", ProcessElevation.NotElevated, false, 1000, 2000));
        await _processes.StartSampleCommand.ExecuteAsync(null);

        _starter.Started.Last().Arguments.ShouldContain("Timer");
        _starter.Started.Last().Arguments.ShouldNotContain("--grow-every");
        _processes.Selected.ShouldNotBeNull().ProcessId.ShouldBe(pid);
        _processes.Selected.Kind.ShouldBe("Sample");
        _processes.Processes.Single(p => p.ProcessId == 10).Kind.ShouldBeEmpty();
        _processes.Status.ShouldBe($"Sample {pid} started: Timer.");
        _processes.StopSampleCommand.CanExecute(null).ShouldBeTrue();

        await _processes.StopSampleCommand.ExecuteAsync(null);

        _starter.Last.Killed.ShouldBeTrue();
        _processes.Status.ShouldBe($"Sample {pid} stopped.");
        _processes.Processes.Single(p => p.ProcessId == pid).Kind.ShouldBeEmpty("no longer this HeapScope's running sample");
    }

    [Fact]
    public async Task A_sample_that_exits_elsewhere_loses_its_mark_without_a_refresh()
    {
        await _processes.RefreshCommand.ExecuteAsync(null);
        _processes.SampleScenarios[0].IsChosen = true;
        var pid = NextSamplePid();
        _capture.Processes.Add(new ProcessEntry(pid, "heapscope-sample", "heapscope-sample.exe --scenarios StaticCache", "10.0.14", ProcessElevation.NotElevated, false, 1000, 2000));
        await _processes.StartSampleCommand.ExecuteAsync(null);
        _processes.Selected.ShouldNotBeNull().IsSample.ShouldBeTrue();

        _starter.Last.Exit(code: 1);

        _processes.Processes.Single(p => p.ProcessId == pid).IsSample.ShouldBeFalse();
        _processes.Selected.ShouldNotBeNull().IsSample.ShouldBeFalse("the selection follows its row");
        _processes.StopSampleCommand.CanExecute(null).ShouldBeFalse();

        // Review M4 of 4.1.1: the hidden rows' rule keeps its own copy of the list; a toggle rebuilds from it.
        _processes.ShowHiddenProcesses = true;
        _processes.ShowHiddenProcesses = false;
        _processes.Processes.Single(p => p.ProcessId == pid).IsSample.ShouldBeFalse("the mark does not come back");
        _processes.Selected.ShouldNotBeNull().ProcessId.ShouldBe(pid);
        _processes.StopSampleCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task A_refusal_is_shown_with_what_to_do()
    {
        await _processes.RefreshCommand.ExecuteAsync(null);

        await _processes.StartSampleCommand.ExecuteAsync(null);

        _processes.Status.ShouldBe("Choose at least one scenario.");
        _processes.StopSampleCommand.CanExecute(null).ShouldBeFalse("no sample selected");
    }

    [Fact]
    public async Task A_sample_that_cannot_run_never_empties_the_process_list()
    {
        _starter.NextThrows = new System.ComponentModel.Win32Exception(5, "Access is denied");

        await _processes.RefreshCommand.ExecuteAsync(null);

        _processes.Processes.ShouldHaveSingleItem().Name.ShouldBe("Api");
        _processes.Status.ShouldBeNull();
        _processes.SampleStatus.ShouldNotBeNull().ShouldStartWith("The sample at heapscope-sample.exe could not be started (Access is denied).");
        _processes.SampleScenarios.ShouldBeEmpty();
    }

    [Fact]
    public void Where_capture_is_not_available_the_sample_section_is_not_offered()
    {
        _capture.UnavailableReason = "Capturing from HeapScope runs on Windows for now.";

        _processes.HasSample.ShouldBeFalse();
    }

    public void Dispose()
    {
        _samples.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // The fake numbers its processes in order: the catalog read has taken one already when the page has scenarios.
    private int NextSamplePid() => _starter.Started.Max(s => s.Process.Id) + 1;
}
