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
using HeapScope.Contracts;
using HeapScope.Application.Snapshots;
using HeapScope.UI.Services;
using HeapScope.UI.ViewModels;

namespace HeapScope.UI.ViewModels.Tests;

public sealed class ProcessesViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));
    private readonly FakeWorkerClient _worker = new();
    private readonly FakeUserInteraction _interaction = new();
    private readonly JobTracker _jobs = new(TimeProvider.System);
    private readonly FakeCaptureService _capture;
    private readonly SnapshotsViewModel _snapshots;
    private readonly ProcessesViewModel _processes;

    public ProcessesViewModelTests()
    {
        var operations = new WorkerOperations(_worker, _jobs);
        // A fixed amount of free memory: with the machine's own, a full test run that leaves little free makes opening a
        // capture ask the low memory question first (failed so on 2026-11-10 with 0.6 GB free).
        _snapshots = new SnapshotsViewModel(TestSessions.Create(_worker, _jobs), operations, new ImmediateUiDispatcher(), _interaction);
        _capture = new FakeCaptureService(_root, _jobs);
        _processes = new ProcessesViewModel(_capture, _snapshots, _interaction);
        _capture.Processes.Add(new ProcessEntry(10, "Api", "Api.exe --urls x", "10.0.13", ProcessElevation.NotElevated, false, 1000, 2000));
        _capture.Processes.Add(new ProcessEntry(20, "Service", null, null, ProcessElevation.Elevated, false, 3000, 4000));
    }

    [Fact]
    public async Task Refresh_hides_the_processes_that_need_more_rights_and_says_how_many()
    {
        // Sprint 4.1.1 (owner, 2026-11-20): what HeapScope cannot capture as it runs now is hidden by default.
        await _processes.RefreshCommand.ExecuteAsync(null);

        _processes.Processes.Select(p => p.Name).ShouldBe(["Api"]);
        _processes.Processes[0].Elevation.ShouldBeEmpty();
        _processes.ShowHiddenProcesses.ShouldBeFalse("off at each start");
        _processes.CanShowHiddenProcesses.ShouldBeTrue();
        _processes.HiddenNote.ShouldBe(HeapScope.Application.Sessions.RightsTexts.HiddenNote(1));
        _processes.ShowElevationHint.ShouldBeFalse("the note says it while they are hidden");
        _processes.Status.ShouldBeNull();
        _processes.CaptureCommand.CanExecute(null).ShouldBeFalse("nothing selected");
    }

    [Fact]
    public async Task The_check_box_lists_them_marked_with_the_banner_and_unchecking_hides_them_again()
    {
        await _processes.RefreshCommand.ExecuteAsync(null);

        _processes.ShowHiddenProcesses = true;

        _processes.Processes.Select(p => p.Name).ShouldBe(["Api", "Service"]);
        _processes.Processes[1].Elevation.ShouldBe(HeapScope.Application.Sessions.RightsTexts.NeedsMore);
        _processes.HiddenNote.ShouldBeNull();
        _processes.ShowElevationHint.ShouldBeTrue();

        _processes.Selected = _processes.Processes[1];
        await _processes.RefreshCommand.ExecuteAsync(null);
        _processes.Selected.ShouldNotBeNull().Name.ShouldBe("Service", "the box stays checked across a refresh");

        _processes.ShowHiddenProcesses = false;

        _processes.Processes.Select(p => p.Name).ShouldBe(["Api"]);
        _processes.Selected.ShouldBeNull("a hidden row is not selected");
        _processes.HiddenNote.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_page_logs_the_pids_it_hides_and_each_toggle()
    {
        // Review M5 of 4.1.1: the page's line, through the shared rule.
        var logger = new HeapScope.Application.Tests.RecordingLogger<ProcessesViewModel>();
        var processes = new ProcessesViewModel(_capture, _snapshots, _interaction, logger: logger);

        await processes.RefreshCommand.ExecuteAsync(null);
        processes.ShowHiddenProcesses = true;

        logger.Lines.Count(l => l.StartsWith("The Processes page lists", StringComparison.Ordinal)).ShouldBe(2);
        logger.Lines.ShouldContain(l => l.StartsWith("The Processes page lists 1 of 2 processes;", StringComparison.Ordinal) && l.EndsWith(": 20", StringComparison.Ordinal));
        logger.Lines[^1].ShouldContain("is checked (just changed)");
    }

    [Fact]
    public async Task When_every_process_is_hidden_the_page_says_so_rather_than_that_there_are_none()
    {
        _capture.Processes.RemoveAt(0);

        await _processes.RefreshCommand.ExecuteAsync(null);

        _processes.Processes.ShouldBeEmpty();
        _processes.Status.ShouldBeNull();
        _processes.HiddenNote.ShouldBe(HeapScope.Application.Sessions.RightsTexts.HiddenNote(1));

        _capture.Processes.Clear();
        await _processes.RefreshCommand.ExecuteAsync(null);
        _processes.Status.ShouldBe("No running .NET processes were found.");
        _processes.HiddenNote.ShouldBeNull();
        _processes.CanShowHiddenProcesses.ShouldBeFalse();
    }

    [Fact]
    public async Task Where_capture_is_not_available_the_page_says_why_and_offers_no_capture()
    {
        // Sprint 2.3.6: Linux and macOS front ends say so up front instead of failing on the first command.
        _capture.UnavailableReason = "Capturing from HeapScope runs on Windows for now.";

        await _processes.RefreshCommand.ExecuteAsync(null);
        _processes.Selected = new ProcessRow(10, "Api", "10.0.13", string.Empty, 1000, "Api.exe", false);

        _processes.Processes.ShouldBeEmpty("the list is not read");
        _processes.Status.ShouldBe(_capture.UnavailableReason);
        _processes.Unavailable.ShouldBe(_capture.UnavailableReason);
        _processes.CaptureCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task An_elevated_HeapScope_shows_no_hint()
    {
        _capture.IsHeapScopeElevated = true;

        await _processes.RefreshCommand.ExecuteAsync(null);

        // Sprint 4.1.1: elevated (or root), HeapScope can capture them all, so all are shown, still marked, with no box.
        _processes.ShowElevationHint.ShouldBeFalse();
        _processes.Processes[1].Elevation.ShouldBe(HeapScope.Application.Sessions.RightsTexts.Reachable);
        _processes.CanShowHiddenProcesses.ShouldBeFalse();
        _processes.HiddenNote.ShouldBeNull();
    }

    [Fact]
    public async Task Capture_writes_into_the_session_shows_the_secrets_note_and_offers_to_open()
    {
        await _processes.RefreshCommand.ExecuteAsync(null);
        _processes.Selected = _processes.Processes[0];

        await _processes.CaptureCommand.ExecuteAsync(null);

        File.Exists(_processes.LastCapturePath).ShouldBeTrue();
        _processes.LastCapturePath!.ShouldStartWith(_root);
        var question = _interaction.Questions.ShouldHaveSingleItem();
        question.ShouldContain("never uploads");
        question.ShouldContain("Open the dump now?");
        _worker.Service.Opened.ShouldHaveSingleItem().Path.ShouldBe(_processes.LastCapturePath);
    }

    [Fact]
    public async Task Capture_as_gcdump_asks_for_a_gcdump_and_says_what_it_holds()
    {
        await _processes.RefreshCommand.ExecuteAsync(null);
        _processes.Selected = _processes.Processes[0];
        _processes.CaptureGcDump = true;

        await _processes.CaptureCommand.ExecuteAsync(null);

        _capture.Kinds.ShouldBe([SnapshotKindDto.GcDump]);
        _processes.LastCapturePath!.ShouldEndWith(".gcdump");
        var question = _interaction.Questions.ShouldHaveSingleItem();
        question.ShouldContain("no field values or string text");
        question.ShouldContain("Open the gcdump now?");
    }

    [Fact]
    public async Task Capturing_an_elevated_process_explains_how_to_run_as_administrator()
    {
        await _processes.RefreshCommand.ExecuteAsync(null);
        _processes.ShowHiddenProcesses = true;
        _processes.Selected = _processes.Processes[1];

        await _processes.CaptureCommand.ExecuteAsync(null);

        _processes.Status!.ShouldContain("Run HeapScope as administrator");
        _processes.LastCapturePath.ShouldBeNull();
    }

    [Fact]
    public async Task Any_capture_failure_is_shown_as_the_status()
    {
        _capture.Failure = new CaptureFailedException(CaptureFailure.WriteFailed, "The disk is full.", null);
        await _processes.RefreshCommand.ExecuteAsync(null);
        _processes.Selected = _processes.Processes[0];

        await _processes.CaptureCommand.ExecuteAsync(null);

        _processes.Status.ShouldBe("The disk is full.");
        _processes.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task Home_lists_recent_sessions_and_opening_one_makes_it_current_and_opens_its_latest_snapshot()
    {
        await _processes.RefreshCommand.ExecuteAsync(null);
        _processes.Selected = _processes.Processes[0];
        _interaction.Answer = false;
        await _processes.CaptureCommand.ExecuteAsync(null);
        var library = new SessionLibrary(_root);
        var home = new HomeViewModel(new WorkerOperations(_worker, _jobs), _snapshots, library);

        await home.RefreshSessionsCommand.ExecuteAsync(null);
        await home.OpenSessionCommand.ExecuteAsync(home.RecentSessions.ShouldHaveSingleItem());

        _worker.Service.Opened.ShouldHaveSingleItem().Path.ShouldBe(_processes.LastCapturePath);
        library.Current.Folder.ShouldBe(Path.GetDirectoryName(_processes.LastCapturePath));
    }

    public void Dispose()
    {
        _snapshots.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
