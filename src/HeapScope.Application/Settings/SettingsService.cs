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

using HeapScope.Application.Allocations;
using HeapScope.Application.Bookmarks;
using HeapScope.Application.Jobs;
using HeapScope.Application.Logging;
using HeapScope.Application.Sessions;
using HeapScope.Application.Snapshots;
using HeapScope.Application.Workers;
using HeapScope.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HeapScope.Application.Settings;

/// <summary>
/// The Settings page's work (sprint 3.12, B1; owner, 2026-11-12): the user's settings (<see cref="HeapScopeSettings"/>, shared
/// by the app, the worker and the command line), and deleting HeapScope's logs or everything it keeps. Purge all deletes the
/// logs, every session of the library (the current one too) with HeapScope's own caches of their dumps, and the bookmarks.
/// It never touches a dump outside the library or that dump's caches, and keeps the trusted DACs and the index cache key.
/// </summary>
public sealed class SettingsService
{
    private readonly SessionLibrary _library;
    private readonly BookmarkStore _bookmarks;
    private readonly SnapshotSession _snapshots;
    private readonly AllocationSession? _allocations;
    private readonly JobTracker _jobs;
    private readonly FileLoggerProvider? _logs;
    private readonly HeapScopeFolders _folders;
    private readonly string _cacheRoot;
    private readonly ILogger _logger;

    /// <param name="folders">HeapScope's data folder (settings and logs); null for <see cref="HeapScopeFolders.Default"/>.</param>
    /// <param name="workerOptions">The worker's options, whose data folder holds the caches of dumps it cannot write beside.</param>
    public SettingsService(
        SessionLibrary library,
        BookmarkStore bookmarks,
        SnapshotSession snapshots,
        JobTracker jobs,
        AllocationSession? allocations = null,
        FileLoggerProvider? logs = null,
        HeapScopeFolders? folders = null,
        IOptions<WorkerHostOptions>? workerOptions = null,
        ILogger<SettingsService>? logger = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _bookmarks = bookmarks ?? throw new ArgumentNullException(nameof(bookmarks));
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _allocations = allocations;
        _logs = logs;
        _folders = folders ?? HeapScopeFolders.Default;
        _cacheRoot = (workerOptions?.Value.DataFolder is { Length: > 0 } data ? new HeapScopeFolders(data) : _folders).Cache;
        _logger = logger ?? NullLogger<SettingsService>.Instance;
    }

    /// <summary>
    /// Whether this process may write filter and search text into its log now (for the command line, which reads its
    /// settings through Application).
    /// </summary>
    public static bool FilterTextInLogs => LogPrivacy.Current.IncludeFilterText;

    /// <summary>The settings file, for the log and the page.</summary>
    public string SettingsPath => HeapScopeSettings.PathIn(_folders);

    /// <summary>The log folder.</summary>
    public string LogFolder => _logs?.Directory ?? _folders.Logs;

    /// <summary>Include filter text in logs, as saved (on when nothing is saved).</summary>
    public bool IncludeFilterTextInLogs => HeapScopeSettings.Read(_folders).IncludeFilterTextInLogs;

    /// <summary>Saves Include filter text in logs; the app, the worker and the command line follow it within a second.</summary>
    /// <exception cref="IOException">The settings file could not be written.</exception>
    public void SetIncludeFilterTextInLogs(bool include)
    {
        var settings = HeapScopeSettings.Read(_folders) with { IncludeFilterTextInLogs = include };
        settings.Save(_folders);
        _logger.LogInformation("Setting saved to {File}: include filter text in logs {State}", SettingsPath, include ? "on" : "off");
    }

    /// <summary>The log files Purge logs would delete: every HeapScope log but the one this process writes now.</summary>
    public Deletion PlanPurgeLogs()
    {
        var files = LogFiles().ToList();
        return new Deletion(files, files.Sum(Length));
    }

    /// <summary>Deletes HeapScope's log files but the one this process writes now (kept until HeapScope closes).</summary>
    public Deletion PurgeLogs()
    {
        var plan = PlanPurgeLogs();
        _logger.LogInformation("Purge logs: deleting {Files} files ({Bytes} bytes) in {Folder}; the log written now, {Current}, is kept", plan.Files.Count, plan.Bytes, LogFolder, _logs?.CurrentPath ?? "none");
        var deletion = Delete(plan.Files);
        _logger.LogInformation("Purge logs: {Deleted} files deleted ({Bytes} bytes), {Failed} could not be", deletion.Files.Count, deletion.Bytes, deletion.Failed?.Count ?? 0);
        return deletion;
    }

    /// <summary>What Purge all would delete, or why it cannot run now.</summary>
    public PurgePlan PlanPurgeAll()
    {
        var sessions = _library.ListRecent(int.MaxValue);
        var files = new List<string>();
        string? refused = Busy();
        foreach (var session in sessions)
        {
            if (_library.InUseBy(session.Folder) is { } user)
            {
                refused ??= $"{char.ToUpperInvariant(user[0])}{user[1..]} writes into a session; stop it first.";
                continue;
            }

            try
            {
                files.AddRange(_library.PlanSessionDeletion(session.Folder, _cacheRoot, allowCurrent: true).Files);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                refused ??= ex.Message;
            }
        }

        if (File.Exists(_bookmarks.Path))
        {
            files.Add(_bookmarks.Path);
        }

        files.AddRange(LogFiles());
        return new PurgePlan(sessions.Count, new Deletion(files, files.Sum(Length)), refused);
    }

    /// <summary>
    /// Deletes everything HeapScope keeps (see the class): closes the snapshots and traces of the library first, so the worker
    /// lets go of their files, then the sessions, the bookmarks and the logs. Refused while anything runs.
    /// </summary>
    /// <exception cref="InvalidOperationException">A capture, recording or open runs, or a session is in use.</exception>
    public async Task<(PurgePlan Plan, Deletion Deleted)> PurgeAllAsync(CancellationToken cancellationToken)
    {
        var plan = PlanPurgeAll();
        if (plan.Refused is { } refused)
        {
            _logger.LogInformation("Purge all refused: {Reason}", refused);
            throw new InvalidOperationException(refused);
        }

        _logger.LogInformation(
            "Purge all: {Sessions} sessions in {Library}, bookmarks {Bookmarks}, logs in {Logs}; {Files} files ({Bytes} bytes). Kept: dumps outside the library and their caches, trusted DACs, the index cache key, the log written now",
            plan.Sessions,
            _library.Root,
            _bookmarks.Path,
            LogFolder,
            plan.Files.Files.Count,
            plan.Files.Bytes);

        foreach (var handle in _snapshots.Snapshots.Where(h => IsInLibrary(h.Path)))
        {
            await _snapshots.CloseAsync(handle, cancellationToken).ConfigureAwait(false);
        }

        foreach (var trace in _allocations?.Traces.Where(t => IsInLibrary(t.Path)) ?? [])
        {
            await _allocations!.CloseAsync(trace, cancellationToken).ConfigureAwait(false);
        }

        return (plan, await Task.Run(() => DeleteEverything(), cancellationToken).ConfigureAwait(false));
    }

    private Deletion DeleteEverything()
    {
        var deleted = new List<string>();
        var failed = new List<string>();
        long bytes = 0;
        _library.ForgetCurrent();
        foreach (var session in _library.ListRecent(int.MaxValue))
        {
            try
            {
                var deletion = _library.DeleteSession(session.Folder, _cacheRoot, allowCurrent: true);
                deleted.AddRange(deletion.Files);
                bytes += deletion.Bytes;
                failed.AddRange(deletion.Failed ?? []);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                failed.Add($"{session.Folder} ({ex.Message})");
            }
        }

        var rest = Delete([.. File.Exists(_bookmarks.Path) ? [_bookmarks.Path] : Array.Empty<string>(), .. LogFiles()]);
        deleted.AddRange(rest.Files);
        bytes += rest.Bytes;
        failed.AddRange(rest.Failed ?? []);
        _logger.LogInformation("Purge all: {Deleted} files deleted ({Bytes} bytes), {Failed} could not be: {Failures}", deleted.Count, bytes, failed.Count, failed.Count == 0 ? "none" : string.Join("; ", failed));
        return new Deletion(deleted, bytes, failed.Count == 0 ? null : failed);
    }

    // Anything that writes into the library or reads its files now.
    private string? Busy() =>
        _jobs.Jobs.FirstOrDefault(j => j.Status == JobStatus.Running) is { } job
            ? $"{job.Title} is running; wait for it or cancel it first."
            : null;

    private bool IsInLibrary(string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_library.Root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, HeapScope.Contracts.PathIdentity.Comparison());
    }

    private IEnumerable<string> LogFiles()
    {
        if (!Directory.Exists(LogFolder))
        {
            return [];
        }

        var current = _logs?.CurrentPath;
        return Directory.EnumerateFiles(LogFolder, "heapscope-*.log")
            .Where(f => current is null || !HeapScope.Contracts.PathIdentity.Same(Path.GetFullPath(f), Path.GetFullPath(current)))
            .ToList();
    }

    private static long Length(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static Deletion Delete(IEnumerable<string> files)
    {
        var deleted = new List<string>();
        var failed = new List<string>();
        long bytes = 0;
        foreach (var file in files)
        {
            try
            {
                var size = Length(file);
                File.Delete(file);
                deleted.Add(file);
                bytes += size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add($"{file} ({ex.Message})");
            }
        }

        return new Deletion(deleted, bytes, failed.Count == 0 ? null : failed);
    }
}
