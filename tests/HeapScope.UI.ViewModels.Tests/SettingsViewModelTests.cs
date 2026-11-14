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
using HeapScope.Application.Bookmarks;
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
    private readonly SettingsService _service;

    public SettingsViewModelTests()
    {
        Directory.CreateDirectory(_root);
        _library = new SessionLibrary(Path.Combine(_root, "sessions"));
        _bookmarks = new BookmarkStore(_root);
        _log = new FileLoggerProvider(Folders.Logs, app: "ui");
        _service = new SettingsService(_library, _bookmarks, TestSessions.Create(_worker, _jobs), _jobs, logs: _log, folders: Folders);
    }

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

    [Fact]
    public async Task Purge_logs_deletes_every_log_but_the_one_being_written()
    {
        var (_, _, writtenNow) = Library();
        var page = new SettingsViewModel(_service, _interaction);

        await page.PurgeLogsCommand.ExecuteAsync(null);

        Directory.GetFiles(Folders.Logs).ShouldBe([writtenNow]);
        page.Status.ShouldNotBeNull().ShouldStartWith("Deleted 2 log files");
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
    public async Task Purge_all_deletes_the_library_the_bookmarks_and_the_logs_and_leaves_the_users_own_dumps_and_keys()
    {
        var (current, older, writtenNow) = Library();
        var outside = Write("outside/mine.dmp");
        var outsideCache = Write("cache/mine.dmp.hsidx");
        var trusted = Write("trusted-dacs.json");
        var key = Write("index-cache.key");
        var page = new SettingsViewModel(_service, _interaction);

        await page.PurgeAllCommand.ExecuteAsync(null);

        Directory.Exists(current).ShouldBeFalse("the current session goes too");
        Directory.Exists(older).ShouldBeFalse();
        File.Exists(_bookmarks.Path).ShouldBeFalse();
        Directory.GetFiles(Folders.Logs).ShouldBe([writtenNow]);
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
