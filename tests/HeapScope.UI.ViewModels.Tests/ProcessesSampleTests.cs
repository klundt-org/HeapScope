// HeapScope
// Copyright (C) 2026 Ray Klundt
// https://github.com/rklundt/HeapScope
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
using HeapScope.UI.Services;
using HeapScope.UI.ViewModels;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>
/// The tutorial's sample on the Processes page (sprint 3.13, task 6): its scenarios from the sample's catalog, started with the
/// ones chosen, its row marked "Sample" and selected, stopped from the page, and a refusal shown with what to do.
/// </summary>
public sealed class ProcessesSampleTests : IDisposable
{
    private const int SamplePid = 4242;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));
    private readonly FakeWorkerClient _worker = new();
    private readonly FakeUserInteraction _interaction = new();
    private readonly JobTracker _jobs = new(TimeProvider.System);
    private readonly FakeCaptureService _capture;
    private readonly Starter _starter = new();
    private readonly ProcessesViewModel _processes;

    public ProcessesSampleTests()
    {
        var operations = new WorkerOperations(_worker, _jobs);
        var snapshots = new SnapshotsViewModel(TestSessions.Create(_worker, _jobs), operations, new ImmediateUiDispatcher(), _interaction);
        _capture = new FakeCaptureService(_root, _jobs);
        _capture.Processes.Add(new ProcessEntry(10, "Api", "Api.exe", "10.0.13", ProcessElevation.NotElevated, false, 1000, 2000));
        var samples = new SampleService(_starter, () => false, () => new SampleLaunch("heapscope-sample.exe", [], "heapscope-sample.exe"));
        _processes = new ProcessesViewModel(_capture, snapshots, _interaction, samples: samples);
    }

    [Fact]
    public async Task A_sample_starts_with_the_chosen_scenarios_marked_and_selected_and_stops_from_the_page()
    {
        await _processes.RefreshCommand.ExecuteAsync(null);
        _processes.HasSample.ShouldBeTrue();
        _processes.SampleScenarios.Select(s => s.Title).ShouldBe(["A static cache", "Timers"]);

        _processes.SampleScenarios[1].IsChosen = true;
        _processes.KeepGrowing = false;
        _capture.Processes.Add(new ProcessEntry(SamplePid, "heapscope-sample", "heapscope-sample.exe --scenarios Timer", "10.0.14", ProcessElevation.NotElevated, false, 1000, 2000));
        await _processes.StartSampleCommand.ExecuteAsync(null);

        _starter.Arguments.Last().ShouldContain("Timer");
        _starter.Arguments.Last().ShouldNotContain("--grow-every");
        _processes.Selected.ShouldNotBeNull().ProcessId.ShouldBe(SamplePid);
        _processes.Selected.Kind.ShouldBe("Sample");
        _processes.Processes.Single(p => p.ProcessId == 10).Kind.ShouldBeEmpty();
        _processes.Status.ShouldBe($"Sample {SamplePid} started: Timer.");
        _processes.StopSampleCommand.CanExecute(null).ShouldBeTrue();

        await _processes.StopSampleCommand.ExecuteAsync(null);

        _starter.Killed.ShouldBeTrue();
        _processes.Status.ShouldBe($"Sample {SamplePid} stopped.");
        _processes.Processes.Single(p => p.ProcessId == SamplePid).Kind.ShouldBeEmpty("no longer this HeapScope's running sample");
    }

    [Fact]
    public async Task A_refusal_is_shown_with_what_to_do()
    {
        await _processes.RefreshCommand.ExecuteAsync(null);

        await _processes.StartSampleCommand.ExecuteAsync(null);

        _processes.Status.ShouldBe("Choose at least one scenario.");
        _processes.StopSampleCommand.CanExecute(null).ShouldBeFalse("no sample selected");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class Starter : ISampleProcessStarter
    {
        public List<IReadOnlyList<string>> Arguments { get; } = [];

        public bool Killed { get; private set; }

        public ISampleProcess Start(string fileName, IReadOnlyList<string> arguments)
        {
            Arguments.Add(arguments);
            return arguments.Contains("--list")
                ? new Process(this, 1, ["""[{"Name":"StaticCache","Title":"A static cache","Plants":"p","KnownAnswer":"StaticCache","AnswerType":"T","CannotRunWith":[],"Grows":true},{"Name":"Timer","Title":"Timers","Plants":"p","KnownAnswer":"Timer","AnswerType":"T","CannotRunWith":[],"Grows":true}]"""])
                : new Process(this, SamplePid, [$"READY pid={SamplePid}"]);
        }

        private sealed class Process(Starter owner, int id, IReadOnlyList<string> lines) : ISampleProcess
        {
            private int _next;

            public int Id { get; } = id;

            public bool HasExited => owner.Killed;

            public string Error => string.Empty;

            public Task<string?> ReadLineAsync(CancellationToken cancellationToken) => Task.FromResult(_next < lines.Count ? lines[_next++] : null);

            public void Kill() => owner.Killed = true;

            public void Dispose()
            {
            }
        }
    }
}
