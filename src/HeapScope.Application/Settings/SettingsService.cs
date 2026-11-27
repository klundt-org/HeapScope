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
    private readonly string? _configuredDacFolder;
    private readonly bool _configuredSymbolServer;
    private readonly ILogger _logger;
    private long _dumpValuesOffAt;

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
        _configuredDacFolder = string.IsNullOrWhiteSpace(workerOptions?.Value.DacFolder) ? null : workerOptions.Value.DacFolder;
        _configuredSymbolServer = workerOptions?.Value.AllowMicrosoftSymbolServer ?? false;
        _logger = logger ?? NullLogger<SettingsService>.Instance;
    }

    /// <summary>
    /// Whether this process may write filter and search text into its log now (for the command line, which reads its
    /// settings through Application).
    /// </summary>
    public static bool FilterTextInLogs => LogPrivacy.Current.IncludeFilterText;

    /// <summary>
    /// Whether the saved settings allow filter text in the log, read from the file now: for a line written without a host (a failed start, a
    /// crash), and off when the file cannot be read (review of 4.6).
    /// </summary>
    public static bool FilterTextInLogsSaved => HeapScopeSettings.TryRead(HeapScopeFolders.Default, out var settings) && settings.IncludeFilterTextInLogs;

    /// <summary>What this process may write into its log now, for its start line: "filter text on, dump values off" (sprint 3.13.2).</summary>
    public static string LogPrivacyFollowed => LogPrivacy.Current.Describe();

    /// <summary>The settings file this process follows, for its start line (sprint 3.12 review M19).</summary>
    public static string SettingsFileFollowed => LogPrivacy.Current.SettingsFile;

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
        // Under the settings file's lock, so two processes that change a setting each keep the other's (peer review 4.5 M6).
        using (PrivateJsonFile.Lock(SettingsPath))
        {
            (ReadForSave() with { IncludeFilterTextInLogs = include }).Save(_folders);
        }

        _logger.LogInformation("Setting saved to {File}: include filter text in logs {State}", SettingsPath, include ? "on" : "off");
    }

    /// <summary>Include dump values in logs, as saved (off when nothing is saved; sprint 3.13.2).</summary>
    public bool IncludeDumpValuesInLogs => HeapScopeSettings.Read(_folders).IncludeDumpValuesInLogs;

    /// <summary>
    /// Saves Include dump values in logs; the app, the worker and the command line follow it within a second. The caller has
    /// asked the user first: the app's Settings page with a question, the command line with <c>--yes</c>.
    /// </summary>
    /// <exception cref="IOException">The settings file could not be written.</exception>
    public void SetIncludeDumpValuesInLogs(bool include)
    {
        using (PrivateJsonFile.Lock(SettingsPath))
        {
            (ReadForSave() with { IncludeDumpValuesInLogs = include }).Save(_folders);
        }

        _logger.LogInformation(
            "Setting saved to {File}: include dump values in logs {State}{Note}",
            SettingsPath,
            include ? "on" : "off",
            include ? ", confirmed by the user" : string.Empty);
        if (!include)
        {
            Volatile.Write(ref _dumpValuesOffAt, Environment.TickCount64);
        }

        if (!include && _logs?.StartNewPart() is { } part)
        {
            // The review's H1: the part that holds the values is closed, so Purge logs can delete it.
            _logger.LogInformation("This log goes on in {Part}; Purge logs deletes this process's earlier parts, which may hold values read from dumps", part);
        }
    }

    /// <summary>Whether this process runs with sudo, when saved DAC settings are ignored (the 4.8 review's H1).</summary>
    public static bool UnderSudo => InvokingUser.Current is not null;

    /// <summary>The DAC folder saved on the Settings page (sprint 4.8, H15), or null.</summary>
    public string? DacFolder => HeapScopeSettings.Read(_folders).DacFolder is { Length: > 0 } folder ? folder : null;

    /// <summary>
    /// The DAC folder this process's configuration gives (<c>Symbols:DacFolder</c> in appsettings, or <c>--dac-folder</c>), which
    /// wins over the saved one; null when there is none.
    /// </summary>
    public string? ConfiguredDacFolder => _configuredDacFolder;

    /// <summary>
    /// Saves the DAC folder, or clears it when <paramref name="folder"/> is empty; the worker uses it from the next open. The
    /// folder must be a full path to a folder that exists on this computer: a network folder is never searched (ADR-017).
    /// </summary>
    /// <returns>The folder saved, as a full path, or null when cleared.</returns>
    /// <exception cref="ArgumentException">The folder is not one HeapScope can use; the message says why and what to do.</exception>
    /// <exception cref="IOException">The settings file could not be written.</exception>
    public string? SetDacFolder(string? folder)
    {
        var saved = CheckDacFolder(folder);
        using (PrivateJsonFile.Lock(SettingsPath))
        {
            (ReadForSave() with { DacFolder = saved }).Save(_folders);
        }

        _logger.LogInformation("Setting saved to {File}: DAC folder {Folder}", SettingsPath, saved ?? "(none)");
        return saved;
    }

    /// <summary>The folder <see cref="SetDacFolder"/> would save, or why it refuses it.</summary>
    /// <exception cref="ArgumentException">The folder is not one HeapScope can use.</exception>
    public static string? CheckDacFolder(string? folder)
    {
        var text = folder?.Trim().Trim('"').Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (!Path.IsPathFullyQualified(text))
        {
            throw new ArgumentException($"{text} is not a full path: give the whole path of the folder.");
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(text));
        if (OperatingSystem.IsWindows() && NotOnALocalFixedDisk(full) is { } why)
        {
            // The DAC search's own rule (DacLocator.IsLocalFixedPath, sprint 4.8 review H2): a folder it would leave out is refused here.
            throw new ArgumentException($"{full} is {why}, which HeapScope never searches for a DAC. Copy the DAC into a folder on a local disk of this computer and choose that one.");
        }

        if (!Directory.Exists(full))
        {
            throw new ArgumentException($"{full} does not exist. Create it and copy the DAC into it, or choose another folder.");
        }

        return full;
    }

    // As DacLocator.IsLocalFixedPath decides on Windows: no \\ path (a share, or a device path), and a fixed drive.
    private static string? NotOnALocalFixedDisk(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        {
            return "a network or device path";
        }

        try
        {
            return Path.GetPathRoot(path) is { Length: > 0 } root && new DriveInfo(root).DriveType is var type && type != DriveType.Fixed
                ? type switch
                {
                    DriveType.Network => "on a network drive",
                    DriveType.Removable => "on a removable drive",
                    DriveType.CDRom => "on a CD or DVD",
                    _ => "not on a local fixed disk",
                }
                : null;
        }
        catch (ArgumentException)
        {
            return "not on a local fixed disk";
        }
    }

    /// <summary>Whether downloads from Microsoft's symbol server are on, as saved (off when nothing is saved; sprint 4.8).</summary>
    public bool AllowMicrosoftSymbolServer => HeapScopeSettings.Read(_folders).AllowMicrosoftSymbolServer;

    /// <summary>Whether this process's configuration turns downloads on whatever is saved (appsettings, or <c>--allow-symbol-server</c>).</summary>
    public bool ConfiguredSymbolServer => _configuredSymbolServer;

    /// <summary>
    /// Saves whether a missing DAC may be downloaded from Microsoft's symbol server; the worker follows it from the next open.
    /// The caller has asked the user first: the app's Settings page with a question, the command line with <c>--yes</c>.
    /// </summary>
    /// <exception cref="IOException">The settings file could not be written.</exception>
    public void SetAllowMicrosoftSymbolServer(bool allow)
    {
        using (PrivateJsonFile.Lock(SettingsPath))
        {
            (ReadForSave() with { AllowMicrosoftSymbolServer = allow }).Save(_folders);
        }

        _logger.Log(
            allow ? LogLevel.Warning : LogLevel.Information,
            "Setting saved to {File}: symbol server downloads {State}{Note}",
            SettingsPath,
            allow ? "on" : "off",
            allow ? ", confirmed by the user; a missing DAC may be downloaded from Microsoft's symbol server, which sends module identifiers (never dump contents)" : string.Empty);
    }

    /// <summary>The time a worker needs to see a settings change (it checks once a second) and its lines to reach this log.</summary>
    internal static readonly TimeSpan WorkerCatchUp = TimeSpan.FromSeconds(2);

    // Before a purge lists the logs: waits until the workers have seen dump values turned off, if that was just now, then closes
    // the part being written, so the purge deletes everything this process logged before it.
    private void StartNewLogPart()
    {
        if (_logs is null)
        {
            return;
        }

        var off = Volatile.Read(ref _dumpValuesOffAt);
        var wait = off == 0 ? TimeSpan.Zero : WorkerCatchUp - TimeSpan.FromMilliseconds(Environment.TickCount64 - off);
        if (wait > TimeSpan.Zero)
        {
            Thread.Sleep(wait);
        }

        // Logged in the new part, which the purge keeps (the 3.13.2 re-review: a purge must not delete its own record).
        if (_logs.StartNewPart() is { } part)
        {
            _logger.LogInformation(
                "This log goes on in {Part} before the purge{Waited}",
                part,
                wait > TimeSpan.Zero ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $", after waiting {(int)wait.TotalMilliseconds} ms for the workers to see dump values turned off") : string.Empty);
        }
    }

    // The sprint 3.13.2 review's H2: a file that is there but cannot be read is never replaced by the defaults.
    private HeapScopeSettings ReadForSave() => HeapScopeSettings.TryRead(_folders, out var settings)
        ? settings
        : throw new IOException($"{SettingsPath} could not be read, so nothing was saved. Close any program that has it open, or delete it to go back to the defaults.");

    /// <summary>
    /// The log files Purge logs would delete: every HeapScope log but those a running HeapScope process writes now, this one's
    /// included (the review's M5: another window or a command line run keeps its log), which are listed as kept.
    /// </summary>
    public Deletion PlanPurgeLogs()
    {
        var (files, kept) = LogFiles();
        return new Deletion(files, files.Sum(Length), Kept: kept);
    }

    /// <summary>
    /// Deletes HeapScope's log files but those running HeapScope processes write now (kept until they close). This process's
    /// own log goes on in a new part first, so all it wrote before the purge is deleted too (the 3.13.2 re-review): values a
    /// worker wrote in the moment before it saw dump values turned off, or while another process had them on, go with it.
    /// </summary>
    public Deletion PurgeLogs()
    {
        StartNewLogPart();
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
        string? refused = Busy() ?? OtherHeapScope();
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

        // Before its first line, so its record stays in the part it keeps (the 3.13.2 re-review); the question it answered
        // was logged in the earlier part, so the start line says it was confirmed.
        await Task.Run(StartNewLogPart, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Purge all, confirmed by the user: {Sessions} sessions in {Library}, bookmarks {Bookmarks}, logs in {Logs} (this process's earlier parts included); {Files} files ({Bytes} bytes). Kept: dumps outside the library and their caches, trusted DACs, the index cache key, the log part written now",
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
        var writer = current is null ? null : Writer(current);
        foreach (var file in Directory.EnumerateFiles(LogFolder, "heapscope-*.log"))
        {
            var mine = current is not null && HeapScope.Contracts.PathIdentity.Same(Path.GetFullPath(file), Path.GetFullPath(current));

            // An earlier part of this process's log (the review's H1): closed, so it goes, values and all.
            var earlierPartOfMine = !mine && writer is not null && Writer(file) == writer;
            (mine || (!earlierPartOfMine && WrittenByRunningProcess(file)) ? kept : delete).Add(file);
        }

        return (delete, kept);
    }

    /// <summary>
    /// The running HeapScope process that wrote a log file (pid from its name, the file's creation time), or null: the app, the
    /// command line, or the dotnet host running the command line's tool, started before the file was created. A pid reused
    /// since belongs to a later process, so an old log never blocks a purge (the review: a long-lived dotnet build server took
    /// one). Tests replace it.
    /// </summary>
    internal Func<int, DateTime, string?> RunningHeapScope { get; init; } = HeapScopeProcessName;

    private static readonly string[] s_processNames = ["HeapScope", "heapscope-cli", "dotnet"];

    private static string? HeapScopeProcessName(int pid, DateTime logCreatedUtc)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return s_processNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase)
                && process.StartTime.ToUniversalTime() <= logCreatedUtc.AddSeconds(1)
                ? process.ProcessName
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Whether the pid in a log file's name is a running HeapScope process (the name's fourth part, before any "-part").</summary>
    internal bool WrittenByRunningProcess(string file) => RunningWriter(file) is not null;

    /// <summary>The app and pid in a log file's name (<c>heapscope-date-app-pid[-part].log</c>); null for another name.</summary>
    private static (string App, int Pid)? Writer(string file)
    {
        var parts = Path.GetFileNameWithoutExtension(file).Split('-');
        return parts.Length >= 4 && int.TryParse(parts[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var pid)
            ? (parts[2], pid)
            : null;
    }

    /// <summary>The app part, pid and process name of a log file's writer when it still runs; null otherwise.</summary>
    private (string App, int Pid, string Process)? RunningWriter(string file)
    {
        var parts = Path.GetFileNameWithoutExtension(file).Split('-');
        if (parts.Length < 4 || !int.TryParse(parts[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var pid))
        {
            return null;
        }

        DateTime created;
        try
        {
            created = File.GetCreationTimeUtc(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return RunningHeapScope(pid, created) is { } name ? (parts[2], pid, name) : null;
    }

    /// <summary>
    /// Another running HeapScope (an app or a command line run, by the pid in its log's name), whose captures, recordings and
    /// open snapshots this process cannot see (the review's M20): Purge all waits for it to close.
    /// </summary>
    private string? OtherHeapScope()
    {
        if (!Directory.Exists(LogFolder))
        {
            return null;
        }

        foreach (var file in Directory.EnumerateFiles(LogFolder, "heapscope-*.log"))
        {
            if (RunningWriter(file) is { } writer && writer.Pid != Environment.ProcessId)
            {
                // Which log and which process, so a refusal can be checked (the review).
                _logger.LogInformation("Another HeapScope runs: {File} is written by process {Pid} ({Process})", file, writer.Pid, writer.Process);
                var name = writer.App == "cli" ? "a HeapScope command line run" : "another HeapScope window";
                return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{char.ToUpperInvariant(name[0])}{name[1..]} (pid {writer.Pid}) is running; close it first.");
            }
        }

        return null;
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
