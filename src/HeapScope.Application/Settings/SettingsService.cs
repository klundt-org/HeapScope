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

    /// <summary>The settings file this process follows, for its start line (sprint 3.12 review M19).</summary>
    public static string SettingsFileFollowed => LogPrivacy.Current.SettingsFile;

    /// <summary>Raised with the new value when this process sees the setting change (sprint 3.12 review M19).</summary>
    public static event Action<bool>? FilterTextInLogsChanged
    {
        add => LogPrivacy.Current.Changed += value;
        remove => LogPrivacy.Current.Changed -= value;
    }

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

    /// <summary>
    /// The log files Purge logs would delete: every HeapScope log but those a running HeapScope process writes now, this one's
    /// included (the review's M5: another window or a command line run keeps its log), which are listed as kept.
    /// </summary>
    public Deletion PlanPurgeLogs()
    {
        var (files, kept) = LogFiles();
        return new Deletion(files, files.Sum(Length), Kept: kept);
    }

    /// <summary>Deletes HeapScope's log files but those running HeapScope processes write now (kept until they close).</summary>
    public Deletion PurgeLogs()
    {
        var plan = PlanPurgeLogs();
        _logger.LogInformation(
            "Purge logs: deleting {Files} files ({Bytes} bytes) in {Folder}; kept, being written by running HeapScope processes: {Kept}",
            plan.Files.Count,
            plan.Bytes,
            LogFolder,
            Listed(plan.Kept ?? []));
        var deletion = Delete(plan.Files) with { Kept = plan.Kept };
        _logger.LogInformation(
            "Purge logs: {Deleted} files deleted ({Bytes} bytes), {Failed} could not be: {Failures}",
            deletion.Files.Count,
            deletion.Bytes,
            deletion.Failed?.Count ?? 0,
            Listed(deletion.Failed ?? []));
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

        var (logs, kept) = LogFiles();
        files.AddRange(logs);
        if (refused is not null)
        {
            // Logged where it is decided (the review's M13): the page shows it without asking.
            _logger.LogInformation("Purge all refused: {Reason}", refused);
        }

        return new PurgePlan(sessions.Count, new Deletion(files, files.Sum(Length), Kept: kept), refused);
    }

    /// <summary>
    /// Deletes everything HeapScope keeps (see the class): closes the snapshots and traces of the library first, so the worker
    /// lets go of their files, then the sessions, the bookmarks and the logs. Refused while anything runs.
    /// </summary>
    /// <exception cref="InvalidOperationException">A capture, recording or open runs, or a session is in use.</exception>
    /// <exception cref="IOException">A session could not be listed.</exception>
    /// <exception cref="Workers.WorkerException">A snapshot or trace could not be closed: nothing was deleted.</exception>
    public async Task<(PurgePlan Plan, Deletion Deleted)> PurgeAllAsync(CancellationToken cancellationToken)
    {
        // The library's listing and sizes off the caller's thread (the review's M9: the page awaited it on the UI thread).
        var plan = await Task.Run(PlanPurgeAll, cancellationToken).ConfigureAwait(false);
        if (plan.Refused is { } refused)
        {
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

        try
        {
            foreach (var handle in _snapshots.Snapshots.Where(h => IsInLibrary(h.Path)).ToList())
            {
                await _snapshots.CloseAsync(handle, cancellationToken).ConfigureAwait(false);
            }

            foreach (var trace in _allocations?.Traces.Where(t => IsInLibrary(t.Path)).ToList() ?? [])
            {
                await _allocations!.CloseAsync(trace, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The outcome of the start line above (the review's M10, M14): what was closed stays closed, nothing was deleted.
            _logger.LogWarning(ex, "Purge all stopped before deleting anything: a snapshot or trace could not be closed");
            throw;
        }

        return (plan, await Task.Run(() => DeleteEverything(), cancellationToken).ConfigureAwait(false));
    }

    private Deletion DeleteEverything()
    {
        var deleted = new List<string>();
        var failed = new List<string>();
        long bytes = 0;
        _library.ForgetCurrent();
        var logged = 0;
        var sessions = _library.ListRecent(int.MaxValue);
        foreach (var session in sessions)
        {
            try
            {
                var deletion = _library.DeleteSession(session.Folder, _cacheRoot, allowCurrent: true);
                deleted.AddRange(deletion.Files);
                bytes += deletion.Bytes;
                failed.AddRange(deletion.Failed ?? []);

                // Each session, which files it held and which dumps it recorded outside its folder were kept (the review's M14).
                if (logged++ < Listing)
                {
                    _logger.LogInformation(
                        "Purge all: session {Folder}: {Files} files ({Bytes} bytes) deleted, {Failed} could not be; kept outside it: {Kept}",
                        session.Folder,
                        deletion.Files.Count,
                        deletion.Bytes,
                        deletion.Failed?.Count ?? 0,
                        Listed(deletion.Kept ?? []));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                failed.Add($"{session.Folder} ({ex.Message})");
            }
        }

        if (sessions.Count > Listing)
        {
            _logger.LogInformation("Purge all: {More} more sessions deleted, not listed one by one", sessions.Count - Listing);
        }

        var (logs, kept) = LogFiles();
        var rest = Delete([.. File.Exists(_bookmarks.Path) ? [_bookmarks.Path] : Array.Empty<string>(), .. logs]);
        deleted.AddRange(rest.Files);
        bytes += rest.Bytes;
        failed.AddRange(rest.Failed ?? []);
        _logger.LogInformation(
            "Purge all: {Deleted} files deleted ({Bytes} bytes), {Failed} could not be: {Failures}; logs kept, being written by running HeapScope processes: {Kept}",
            deleted.Count,
            bytes,
            failed.Count,
            Listed(failed),
            Listed(kept));
        return new Deletion(deleted, bytes, failed.Count == 0 ? null : failed, kept);
    }

    // Anything that writes into the library or reads its files now.
    private string? Busy() =>
        _jobs.Jobs.FirstOrDefault(j => j.Status == JobStatus.Running) is { } job
            ? $"{job.Title} is running; wait for it or cancel it first."
            : null;

    private bool IsInLibrary(string path) => HeapScope.Contracts.PathIdentity.IsUnder(Path.GetFullPath(path), Path.GetFullPath(_library.Root));

    // The most entries a purge's log line lists one by one; the rest are counted (as inspections list rows, sprint 3.12, B7).
    private const int Listing = 50;

    private static string Listed(IReadOnlyCollection<string> items) => items.Count == 0
        ? "none"
        : string.Join("; ", items.Take(Listing)) + (items.Count > Listing ? $" and {items.Count - Listing} more" : string.Empty);

    /// <summary>
    /// The log files to delete, and those kept: this process's own, and any whose name (<c>heapscope-date-app-pid.log</c>) is
    /// a running process's, which still writes into it (the review's M5). A pid reused by another program keeps an old log
    /// until the next purge, which is the safe way to be wrong.
    /// </summary>
    private (List<string> Delete, List<string> Kept) LogFiles()
    {
        var delete = new List<string>();
        var kept = new List<string>();
        if (!Directory.Exists(LogFolder))
        {
            return (delete, kept);
        }

        var current = _logs?.CurrentPath;
        foreach (var file in Directory.EnumerateFiles(LogFolder, "heapscope-*.log"))
        {
            var mine = current is not null && HeapScope.Contracts.PathIdentity.Same(Path.GetFullPath(file), Path.GetFullPath(current));
            (mine || WrittenByRunningProcess(file) ? kept : delete).Add(file);
        }

        return (delete, kept);
    }

    /// <summary>Whether the pid in a log file's name is a running process (the name's fourth part, before any "-part").</summary>
    internal static bool WrittenByRunningProcess(string file)
    {
        var parts = Path.GetFileNameWithoutExtension(file).Split('-');
        if (parts.Length < 4 || !int.TryParse(parts[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var pid))
        {
            return false;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
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
