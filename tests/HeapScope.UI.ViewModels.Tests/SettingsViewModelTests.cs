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
using HeapScope.Application.Bookmarks;
using HeapScope.Application.Snapshots;
using HeapScope.Application.Tests;
using HeapScope.Application.Workers;
using HeapScope.Application.Jobs;
using HeapScope.Application.Logging;
using HeapScope.Application.Sessions;
using HeapScope.Application.Settings;
using HeapScope.Core;
using HeapScope.UI.ViewModels;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>
/// Sprint 3.12 (B1; owner, 2026-11-12): the Settings page. Include filter text in logs is saved where every HeapScope process
/// reads it; Purge logs keeps the log being written; Purge all asks first, then deletes the logs, every session of the
/// library and the bookmarks, and leaves a dump of the user's own folders, its cache, the trusted DACs and the cache key alone.
/// </summary>
public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));
    private readonly FakeWorkerClient _worker = new();
    private readonly FakeUserInteraction _interaction = new();
    private readonly JobTracker _jobs = new(TimeProvider.System);
    private readonly SessionLibrary _library;
    private readonly BookmarkStore _bookmarks;
    private readonly FileLoggerProvider _log;
    private readonly SnapshotSession _snapshots;
    private readonly RecordingLogger<SettingsService> _serviceLog = new();
    private readonly SettingsService _service;

    public SettingsViewModelTests()
    {
        Directory.CreateDirectory(_root);
        _library = new SessionLibrary(Path.Combine(_root, "sessions"));
        _bookmarks = new BookmarkStore(_root);
        _log = new FileLoggerProvider(Folders.Logs, app: "ui");
        _snapshots = TestSessions.Create(_worker, _jobs);
        _service = new SettingsService(_library, _bookmarks, _snapshots, _jobs, logs: _log, folders: Folders, logger: _serviceLog)
        {
            // Running HeapScope processes, as the log names say: this test process and the "other HeapScope" below.
            RunningHeapScope = (pid, _) => pid == Environment.ProcessId || pid == OtherHeapScope ? "HeapScope" : null,
        };
    }

    private const int OtherHeapScope = 123_456_789;

    private HeapScopeFolders Folders => new(_root);

    private string Write(string relative, int bytes = 100)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    /// <summary>Two sessions (the current one with a dump), bookmarks, two old logs and the log being written now.</summary>
    private (string Current, string Older, string WrittenNow) Library()
    {
        var current = _library.Current.Folder;
        File.WriteAllBytes(Path.Combine(current, "app.dmp"), new byte[1000]);
        var older = _library.CreateSession("older").Folder;
        Write("bookmarks.json");
        Write("logs/heapscope-ui-old.log");
        Write("logs/heapscope-cli-old.log");
        _log.CreateLogger("test").LogInformation("A line, so a log is being written now.");
        return (current, older, _log.CurrentPath.ShouldNotBeNull());
    }

    [Fact]
    public void Include_filter_text_in_logs_is_on_by_default_and_saved_where_every_process_reads_it()
    {
        var page = new SettingsViewModel(_service, _interaction);
        page.IncludeFilterTextInLogs.ShouldBeTrue();

        page.IncludeFilterTextInLogs = false;

        HeapScopeSettings.Read(Folders).IncludeFilterTextInLogs.ShouldBeFalse();
        new LogPrivacy(Folders).IncludeFilterText.ShouldBeFalse();
        new SettingsViewModel(_service, _interaction).IncludeFilterTextInLogs.ShouldBeFalse("a new run shows it");
        page.Status.ShouldBeNull();
    }

    // Sprint 3.13.2 (owner, 2026-11-19): off by default, and turned on only after the user confirms.
    [Fact]
    public async Task Include_dump_values_in_logs_is_off_by_default_and_turned_on_only_after_a_yes()
    {
        var page = new SettingsViewModel(_service, _interaction);
        page.IncludeDumpValuesInLogs.ShouldBeFalse();

        _interaction.Answer = true;
        page.IncludeDumpValuesInLogs = true;
        await page.DumpValuesConfirmation;

        _interaction.Questions.ShouldHaveSingleItem().ShouldBe(InfoTexts.DumpValuesQuestion);
        HeapScopeSettings.Read(Folders).IncludeDumpValuesInLogs.ShouldBeTrue();
        new LogPrivacy(Folders).IncludeDumpValues.ShouldBeTrue();
        new SettingsViewModel(_service, _interaction).IncludeDumpValuesInLogs.ShouldBeTrue("a new run shows it");
        page.Status.ShouldBe(InfoTexts.DumpValuesOn);
        _serviceLog.Lines.ShouldContain(l => l.Contains("include dump values in logs on, confirmed by the user", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_no_leaves_dump_values_off_and_saves_nothing()
    {
        var page = new SettingsViewModel(_service, _interaction);
        _interaction.Answer = false;

        page.IncludeDumpValuesInLogs = true;
        await page.DumpValuesConfirmation;

        page.IncludeDumpValuesInLogs.ShouldBeFalse();
        File.Exists(HeapScopeSettings.PathIn(Folders)).ShouldBeFalse();
        page.Status.ShouldBeNull();
    }

    [Fact]
    public async Task Turning_dump_values_off_asks_nothing_and_points_to_purge_logs()
    {
        _interaction.Answer = true;
        var page = new SettingsViewModel(_service, _interaction);
        page.IncludeDumpValuesInLogs = true;
        await page.DumpValuesConfirmation;
        _interaction.Questions.Clear();

        page.IncludeDumpValuesInLogs = false;

        _interaction.Questions.ShouldBeEmpty();
        HeapScopeSettings.Read(Folders).IncludeDumpValuesInLogs.ShouldBeFalse();
        page.Status.ShouldBe(InfoTexts.DumpValuesOff);
        HeapScopeSettings.Read(Folders).IncludeFilterTextInLogs.ShouldBeTrue("the other setting is kept");
    }

    [Fact]
    public async Task A_dump_values_setting_that_cannot_be_saved_is_turned_back_off()
    {
        // The settings file's place is taken by a folder, so it cannot be written.
        Directory.CreateDirectory(HeapScopeSettings.PathIn(Folders));
        var page = new SettingsViewModel(_service, _interaction);

        page.IncludeDumpValuesInLogs = true;
        await page.DumpValuesConfirmation;

        page.IncludeDumpValuesInLogs.ShouldBeFalse();
        page.Status.ShouldNotBeNull().ShouldStartWith("The setting could not be saved to ");
    }

    // Sprint 4.8 (H15): a missing DAC is not a dead end; the folder is saved where the worker reads it.
    [Fact]
    public async Task A_dac_folder_chosen_or_typed_is_saved_as_a_full_path_and_an_empty_box_clears_it()
    {
        var dacs = Directory.CreateDirectory(Path.Combine(_root, "dacs")).FullName;
        var page = new SettingsViewModel(_service, _interaction);
        page.DacFolder.ShouldBeNull();

        _interaction.NextFolder = dacs + Path.DirectorySeparatorChar;
        await page.PickDacFolderCommand.ExecuteAsync(null);

        HeapScopeSettings.Read(Folders).DacFolder.ShouldBe(dacs);
        page.DacFolder.ShouldBe(dacs);
        page.Status.ShouldBe(string.Format(System.Globalization.CultureInfo.CurrentCulture, InfoTexts.DacFolderSaved, dacs));
        new SettingsViewModel(_service, _interaction).DacFolder.ShouldBe(dacs, "a new run shows it");

        page.DacFolder = "  ";
        await page.SaveDacFolderCommand.ExecuteAsync(null);

        HeapScopeSettings.Read(Folders).DacFolder.ShouldBeNull();
        page.Status.ShouldBe(InfoTexts.DacFolderCleared);
        HeapScopeSettings.Read(Folders).IncludeFilterTextInLogs.ShouldBeTrue("the other settings are kept");
    }

    [Theory]
    [InlineData("relative", "is not a full path")]
    [InlineData("missing", "does not exist")]
    [InlineData("network", "is a network or device path")]
    [InlineData("device", "is a network or device path")]
    public async Task A_dac_folder_heapscope_cannot_use_is_refused_with_why_and_nothing_is_saved(string kind, string why)
    {
        Assert.SkipWhen(kind is "network" or "device" && !OperatingSystem.IsWindows(), "A network or device path is told apart by its form on Windows only.");
        var typed = kind switch
        {
            "relative" => "dacs",
            "missing" => Path.Combine(_root, "no such folder"),
            "device" => @"\\?\" + Directory.CreateDirectory(Path.Combine(_root, "dacs")).FullName,
            _ => @"\\server\share\dacs",
        };
        var page = new SettingsViewModel(_service, _interaction) { DacFolder = typed };

        await page.SaveDacFolderCommand.ExecuteAsync(null);

        page.Status.ShouldNotBeNull().ShouldStartWith("Not saved: ");
        page.Status.ShouldContain(why);
        page.DacFolder.ShouldBe(typed, "the box keeps what was typed, to correct it");
        File.Exists(HeapScopeSettings.PathIn(Folders)).ShouldBeFalse();
    }

    // ADR-017 amended (owner, 2026-11-26): per user, off by default, on only after a yes.
    [Fact]
    public async Task Downloads_from_microsoft_are_off_by_default_and_turned_on_only_after_a_yes()
    {
        var page = new SettingsViewModel(_service, _interaction);
        page.AllowMicrosoftSymbolServer.ShouldBeFalse();

        _interaction.Answer = false;
        page.AllowMicrosoftSymbolServer = true;
        await page.SymbolServerConfirmation;

        page.AllowMicrosoftSymbolServer.ShouldBeFalse();
        File.Exists(HeapScopeSettings.PathIn(Folders)).ShouldBeFalse("a no saves nothing");
        _interaction.Questions.ShouldHaveSingleItem().ShouldBe(InfoTexts.SymbolServerQuestion);

        _interaction.Answer = true;
        page.AllowMicrosoftSymbolServer = true;
        await page.SymbolServerConfirmation;

        HeapScopeSettings.Read(Folders).AllowMicrosoftSymbolServer.ShouldBeTrue();
        page.Status.ShouldBe(InfoTexts.SymbolServerOn);
        _serviceLog.Lines.ShouldContain(l => l.Contains("symbol server downloads on, confirmed by the user", StringComparison.Ordinal));

        _interaction.Questions.Clear();
        page.AllowMicrosoftSymbolServer = false;

        _interaction.Questions.ShouldBeEmpty("turning it off asks nothing");
        HeapScopeSettings.Read(Folders).AllowMicrosoftSymbolServer.ShouldBeFalse();
        page.Status.ShouldBe(InfoTexts.SymbolServerOff);
    }

    [Fact]
    public void The_page_says_when_appsettings_decide_the_dac_lookup_instead()
    {
        new SettingsViewModel(_service, _interaction).DacConfiguredNote.ShouldBeNull();
        var configured = new SettingsService(
            _library,
            _bookmarks,
            _snapshots,
            _jobs,
            folders: Folders,
            workerOptions: Microsoft.Extensions.Options.Options.Create(new WorkerHostOptions { DacFolder = @"C:\dacs", AllowMicrosoftSymbolServer = true }));

        var note = new SettingsViewModel(configured, _interaction).DacConfiguredNote.ShouldNotBeNull();

        note.ShouldContain(@"C:\dacs");
        note.ShouldContain(InfoTexts.SymbolServerConfigured);
    }

    // The sprint 3.13.2 review (M8): the box shows on only while the question is open, and never on without an answer.
    [Fact]
    public async Task A_second_click_while_the_question_is_open_is_ignored_and_a_question_that_fails_leaves_it_off()
    {
        var question = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _interaction.Pending = question.Task;
        var page = new SettingsViewModel(_service, _interaction);

        page.IncludeDumpValuesInLogs = true;
        page.IncludeDumpValuesInLogs = false;
        page.IncludeDumpValuesInLogs.ShouldBeTrue("still asking");
        question.SetException(new InvalidOperationException("the window is closing"));
        await page.DumpValuesConfirmation;

        page.IncludeDumpValuesInLogs.ShouldBeFalse();
        File.Exists(HeapScopeSettings.PathIn(Folders)).ShouldBeFalse("nothing saved");
    }

    // The review's M3: the command line may change the file while the app runs.
    [Fact]
    public void The_page_shows_the_settings_saved_since_it_was_built()
    {
        var page = new SettingsViewModel(_service, _interaction);
        new HeapScopeSettings(IncludeFilterTextInLogs: false, IncludeDumpValuesInLogs: true).Save(Folders);

        page.Reload();

        (page.IncludeFilterTextInLogs, page.IncludeDumpValuesInLogs).ShouldBe((false, true));
        _interaction.Questions.ShouldBeEmpty("a reload asks nothing");
    }

    // The review's H2: a file that cannot be read is never replaced by the defaults, which would turn filter text back on.
    [Fact]
    public async Task A_settings_file_that_cannot_be_read_is_not_overwritten()
    {
        var path = HeapScopeSettings.PathIn(Folders);
        new HeapScopeSettings(IncludeFilterTextInLogs: false).Save(Folders);
        var page = new SettingsViewModel(_service, _interaction);
        await File.WriteAllTextAsync(path, "{ damaged", TestContext.Current.CancellationToken);
        _interaction.Answer = true;

        page.IncludeDumpValuesInLogs = true;
        await page.DumpValuesConfirmation;

        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ShouldBe("{ damaged");
        page.IncludeDumpValuesInLogs.ShouldBeFalse();
        page.Status.ShouldNotBeNull().ShouldContain("could not be read, so nothing was saved");
    }

    [Fact]
    public async Task Turning_dump_values_off_when_the_file_cannot_be_written_keeps_the_box_on()
    {
        var page = new SettingsViewModel(_service, _interaction);
        page.IncludeDumpValuesInLogs = true;
        await page.DumpValuesConfirmation;
        File.Delete(HeapScopeSettings.PathIn(Folders));
        Directory.CreateDirectory(HeapScopeSettings.PathIn(Folders));

        page.IncludeDumpValuesInLogs = false;

        page.IncludeDumpValuesInLogs.ShouldBeTrue();
        page.Status.ShouldNotBeNull().ShouldStartWith("The setting could not be saved to ");
    }

    // The review's H1: turning dump values off closes the log part that holds them, and Purge logs deletes it.
    [Fact]
    public async Task After_dump_values_are_turned_off_purge_logs_deletes_the_part_that_held_them()
    {
        _log.CreateLogger("test").LogInformation("A line written while dump values were on.");
        var held = _log.CurrentPath.ShouldNotBeNull();
        _interaction.Answer = true;
        var page = new SettingsViewModel(_service, _interaction);
        page.IncludeDumpValuesInLogs = true;
        await page.DumpValuesConfirmation;

        page.IncludeDumpValuesInLogs = false;
        var afterOff = _log.CurrentPath.ShouldNotBeNull();
        await page.PurgeLogsCommand.ExecuteAsync(null);
        var now = _log.CurrentPath.ShouldNotBeNull();

        afterOff.ShouldNotBe(held);
        File.Exists(held).ShouldBeFalse();
        File.Exists(afterOff).ShouldBeFalse("a purge closes the part being written first");
        Directory.GetFiles(Folders.Logs).ShouldBe([now]);
        await using var reader = new FileStream(now, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var text = new StreamReader(reader);
        (await text.ReadToEndAsync(TestContext.Current.CancellationToken)).ShouldContain("; logs hold filter text ", customMessage: "every part says what it holds");
    }

    [Fact]
    public async Task Purge_logs_deletes_every_log_but_the_one_being_written()
    {
        var (_, _, writtenNow) = Library();
        var page = new SettingsViewModel(_service, _interaction);

        await page.PurgeLogsCommand.ExecuteAsync(null);

        // The part being written was closed first (the 3.13.2 re-review), so it goes too and a new part is kept.
        Directory.GetFiles(Folders.Logs).ShouldBe([_log.CurrentPath.ShouldNotBeNull()]);
        File.Exists(writtenNow).ShouldBeFalse();
        page.Status.ShouldNotBeNull().ShouldStartWith("Deleted 3 log files");
        _interaction.Questions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Purge_all_asks_first_and_a_no_deletes_nothing()
    {
        var (current, older, _) = Library();
        _interaction.Answer = false;
        var page = new SettingsViewModel(_service, _interaction);

        await page.PurgeAllCommand.ExecuteAsync(null);

        _interaction.Titles.ShouldBe(["Delete everything HeapScope keeps?"]);
        _interaction.Questions[0].ShouldStartWith("This deletes HeapScope's logs and its whole library: 2 sessions, ");
        _interaction.Questions[0].ShouldEndWith(" Dumps you opened from your own folders stay. This cannot be undone.");
        Directory.Exists(current).ShouldBeTrue();
        Directory.Exists(older).ShouldBeTrue();
        File.Exists(_bookmarks.Path).ShouldBeTrue();
    }

    [Fact]
    public async Task Purge_alls_question_can_be_shown_without_asking_or_deleting()
    {
        // Sprint 4.1: the screenshot run shows the question itself, since a smoke run answers every question yes.
        var (current, older, _) = Library();
        _interaction.Answer = true;
        var page = new SettingsViewModel(_service, _interaction);

        var question = await page.PurgeAllQuestionAsync();

        question.ShouldNotBeNull().ShouldStartWith("This deletes HeapScope's logs and its whole library: 2 sessions, ");
        _interaction.Titles.ShouldBeEmpty();
        Directory.Exists(current).ShouldBeTrue();
        Directory.Exists(older).ShouldBeTrue();
        File.Exists(_bookmarks.Path).ShouldBeTrue();
    }

    [Fact]
    public async Task Purge_all_deletes_the_library_the_bookmarks_and_the_logs_and_leaves_the_users_own_dumps_and_keys()
    {
        var (current, older, writtenNow) = Library();
        var outside = Write("outside/mine.dmp");
        var outsideCache = Write("cache/mine.dmp.hsidx");
        var trusted = Write("trusted-dacs.json");
        var key = Write("index-cache.key");
        var page = new SettingsViewModel(_service, _interaction);
        var purged = 0;
        page.Purged += (_, _) => purged++;

        await page.PurgeAllCommand.ExecuteAsync(null);

        purged.ShouldBe(1, "so the bookmarks on screen go too (sprint 4.8, M13)");
        Directory.Exists(current).ShouldBeFalse("the current session goes too");
        Directory.Exists(older).ShouldBeFalse();
        File.Exists(_bookmarks.Path).ShouldBeFalse();
        Directory.GetFiles(Folders.Logs).ShouldBe([_log.CurrentPath.ShouldNotBeNull()], "only the part started for the purge");
        File.Exists(writtenNow).ShouldBeFalse();
        new[] { outside, outsideCache, trusted, key }.ShouldAllBe(f => File.Exists(f));
        _library.HasCurrent.ShouldBeFalse("the next capture starts a new session");
        page.Status.ShouldNotBeNull().ShouldStartWith("Deleted everything HeapScope keeps: 2 sessions, ");
    }

    [Fact]
    public async Task Purge_all_is_refused_while_a_job_runs_and_asks_nothing()
    {
        var (current, _, _) = Library();
        var running = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = _jobs.Start("Capture app", (_, _) => running.Task);
        var page = new SettingsViewModel(_service, _interaction);

        await page.PurgeAllCommand.ExecuteAsync(null);

        page.Status.ShouldBe("Nothing was deleted: Capture app is running; wait for it or cancel it first.");
        _interaction.Questions.ShouldBeEmpty();
        Directory.Exists(current).ShouldBeTrue();
        running.SetResult(0);
        await job.Result;
    }

    // The review's M11: the purge's safety rule, proved on what a session records. A dump captured to the user's own folder is
    // kept with its cache in the fallback root; a library dump's cache there goes with it; only the library's snapshots are
    // closed.
    [Fact]
    public async Task Purge_all_keeps_a_dump_a_session_records_outside_its_folder_and_closes_only_the_librarys_snapshots()
    {
        var (current, older, _) = Library();
        var inLibrary = Path.Combine(current, "app.dmp");
        var outside = Write("outside/mine.dmp");
        _library.AddTo(current, Snapshot(inLibrary));
        _library.AddTo(older, Snapshot(outside));
        var libraryCache = Write(Path.GetRelativePath(_root, IndexCachePaths.InFallback(inLibrary, Folders.Cache)));
        var outsideCache = Write(Path.GetRelativePath(_root, IndexCachePaths.InFallback(outside, Folders.Cache)));
        var libraryOpen = await _snapshots.StartOpen(inLibrary).Result;
        var outsideOpen = await _snapshots.StartOpen(outside).Result;
        var page = new SettingsViewModel(_service, _interaction);

        await page.PurgeAllCommand.ExecuteAsync(null);

        Directory.Exists(current).ShouldBeFalse();
        File.Exists(libraryCache).ShouldBeFalse("a library dump's cache in the fallback root goes with it");
        File.Exists(outside).ShouldBeTrue("a dump of the user's own folder stays");
        File.Exists(outsideCache).ShouldBeTrue("and its cache");
        _worker.Service.Closed.ShouldBe([libraryOpen.Descriptor.SnapshotId], "only the library's snapshot is closed");
        _snapshots.Snapshots.ShouldContain(outsideOpen);
        _serviceLog.Lines.ShouldContain(l => l.StartsWith("Purge all: session ", StringComparison.Ordinal) && l.Contains(Path.GetFullPath(outside), StringComparison.Ordinal),
            "each session's line names what was kept outside it (review M14)");
    }

    [Fact]
    public async Task Purge_all_is_refused_while_something_writes_into_a_session_and_the_refusal_is_logged()
    {
        var (_, older, _) = Library();
        using var writing = _library.MarkInUse(older, "live monitoring");
        var page = new SettingsViewModel(_service, _interaction);

        await page.PurgeAllCommand.ExecuteAsync(null);

        page.Status.ShouldBe("Nothing was deleted: Live monitoring writes into a session; stop it first.");
        _interaction.Questions.ShouldBeEmpty();
        Directory.Exists(older).ShouldBeTrue();
        _serviceLog.Lines.ShouldContain("Purge all refused: Live monitoring writes into a session; stop it first.", "the review's M13");
    }

    // The review's M10: a snapshot the worker cannot close stops the purge with a next step, not the last-chance dialog.
    [Fact]
    public async Task A_snapshot_the_worker_cannot_close_stops_purge_all_before_it_deletes_anything()
    {
        var (current, _, _) = Library();
        var inLibrary = Path.Combine(current, "app.dmp");
        _library.AddTo(current, Snapshot(inLibrary));
        await _snapshots.StartOpen(inLibrary).Result;
        _worker.Service.CloseFailure = new WorkerOperationException("The worker stopped.");
        var page = new SettingsViewModel(_service, _interaction);

        await page.PurgeAllCommand.ExecuteAsync(null);

        page.Status.ShouldBe("Stopped before deleting anything more: The worker stopped. Try again; HeapScope's log has the details.");
        Directory.Exists(current).ShouldBeTrue();
        _serviceLog.Lines.ShouldContain(l => l.StartsWith("Purge all stopped before deleting anything", StringComparison.Ordinal));
        page.IsBusy.ShouldBeFalse();
    }

    // The review's M20: another HeapScope's captures and open snapshots are its own; Purge all waits for it to close.
    [Fact]
    public async Task Purge_all_is_refused_while_another_heapscope_runs()
    {
        var (current, _, _) = Library();
        Write($"logs/heapscope-20261115-ui-{OtherHeapScope}.log");
        var page = new SettingsViewModel(_service, _interaction);

        await page.PurgeAllCommand.ExecuteAsync(null);

        page.Status.ShouldBe($"Nothing was deleted: Another HeapScope window (pid {OtherHeapScope}) is running; close it first.");
        _serviceLog.Lines.ShouldContain(l => l.StartsWith("Another HeapScope runs: ", StringComparison.Ordinal) && l.Contains($"process {OtherHeapScope} (HeapScope)", StringComparison.Ordinal));
        _interaction.Questions.ShouldBeEmpty();
        Directory.Exists(current).ShouldBeTrue();
    }

    // The review's M5: another HeapScope process (a second window, a command line run) still writes its log.
    [Fact]
    public async Task Purge_logs_keeps_the_log_a_running_process_writes()
    {
        Library();
        var running = Write($"logs/heapscope-20261114-cli-{Environment.ProcessId}.log");
        var gone = Write($"logs/heapscope-20261114-cli-{int.MaxValue}.log");
        var page = new SettingsViewModel(_service, _interaction);

        await page.PurgeLogsCommand.ExecuteAsync(null);

        File.Exists(running).ShouldBeTrue();
        File.Exists(gone).ShouldBeFalse();
        page.Status.ShouldNotBeNull().ShouldStartWith("Deleted 4 log files", customMessage: "the two old ones without a pid, the gone process's, and this process's part closed for the purge");
    }

    private static SessionSnapshot Snapshot(string path) =>
        new(Guid.NewGuid(), path, "app", 42, "10.0.0", DateTimeOffset.UtcNow, 1000);

    public void Dispose()
    {
        _log.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // The temp folder is the system's to clean.
        }
    }
}
