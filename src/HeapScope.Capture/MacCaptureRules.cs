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

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace HeapScope.Capture;

/// <summary>What macOS says about a process that was asked for by pid and not found (sprint 4.2), with how each read ended.</summary>
/// <param name="Running">Whether a process has the id, whatever could be read about it.</param>
/// <param name="UserId">Its effective user id, which anyone may read; null when the read failed.</param>
/// <param name="UserOutcome">Why <paramref name="UserId"/> is null.</param>
/// <param name="OwnUserId">HeapScope's effective user id.</param>
/// <param name="Environment">Its environment, which macOS gives its own user and root, and for a protected process nobody.</param>
/// <param name="EnvironmentOutcome">Why <paramref name="Environment"/> is null.</param>
/// <param name="Walk">Its memory regions, for the log.</param>
/// <param name="OwnTempFolder">The folder this HeapScope looks in (its <c>TMPDIR</c>, or <c>/tmp</c>).</param>
internal sealed record MacTarget(
    int ProcessId,
    bool Running,
    int? UserId,
    string UserOutcome,
    int? OwnUserId,
    TargetEnvironment? Environment,
    string EnvironmentOutcome,
    MacWalk? Walk,
    string OwnTempFolder);

/// <summary>
/// Why macOS refuses a capture, and what to do (sprint 4.2, untested). HeapScope never signs a target or changes its
/// entitlements; it says what the runtime reported. The rules take what was read, so they are tested on every system. No
/// rule says that a process is not .NET: a region walk that ended early cannot be told from a whole one (4.2 review).
/// </summary>
internal sealed class MacCaptureRules : ICaptureRules
{
    public static MacCaptureRules Instance { get; } = new();

    public NotFoundExplanation WhyNotFound(int processId)
    {
        var target = Read(processId);
        return new NotFoundExplanation(WhyNoDiagnostics(target), Evidence(target));
    }

    public WriteFailureExplanation WhyNotWritten(string message, string account, string directory)
    {
        var reason = WriteFailure(message, account, directory, out var rule);
        return new WriteFailureExplanation(reason, "macOS; rule: " + rule);
    }

    public string? LibraryRefusal(string name, int processId) => LinuxCaptureRules.RootLibraryRefusal(name, processId);

    /// <summary>
    /// Why the runtime's createdump could not write a dump, from what it reported (the texts of .NET 10's macOS createdump).
    /// Its <c>task_for_pid</c> failure is the same whatever the cause, and it names signing and entitlements itself, so the
    /// reason says no more than that. Null when nothing explains it. <paramref name="rule"/> names the rule that decided and
    /// the text it matched, for the log.
    /// </summary>
    public static string? WriteFailure(string message, string account, string directory, out string rule)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (Matched(message, "0x80070070", "No space left") is { } full)
        {
            rule = $"no space left ('{full}')";
            return "the disk is full";
        }

        if (Matched(message, "task_for_pid", "signed and entitled") is { } task)
        {
            rule = $"task_for_pid refused ('{task}')";
            return "macOS did not let the runtime examine the process, which can mean that the application or the runtime is "
                + "not signed with the entitlements that allow it. A gcdump does not need this: capture one instead";
        }

        if (Matched(message, "Operation not permitted") is { } privacy)
        {
            rule = $"macOS refused the write ('{privacy}')";
            return $"macOS did not let the account {account} runs as write to {directory}. macOS protects some folders (Desktop, "
                + "Documents, Downloads) from applications that were not allowed to use them";
        }

        if (Matched(message, "No such file or directory") is { } gone)
        {
            // The message itself names the file; nothing was measured that says more.
            rule = $"the folder is not there for the target ('{gone}')";
            return null;
        }

        if (Matched(message, "0x80070005", "Permission denied", "Could not create output file", "Read-only file system") is { } folder)
        {
            rule = $"the folder is not writable by the target's account ('{folder}')";
            return $"the account {account} runs as cannot write to {directory}";
        }

        rule = "none matched";
        return null;
    }

    /// <summary>
    /// Why a running process has no diagnostics socket where HeapScope looks. Null when it is not running or nothing
    /// explains it (the generic not-found text follows). A user id that could not be read decides nothing.
    /// </summary>
    public static string? WhyNoDiagnostics(MacTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.Running)
        {
            return null;
        }

        var pid = target.ProcessId.ToString(CultureInfo.InvariantCulture);
        if (target.UserId is { } user && target.OwnUserId is { } own && user != own && own != 0)
        {
            return $"Process {pid} runs as another user, and HeapScope finds a process by the diagnostics socket it creates "
                + "in its own temp folder, which on macOS differs per user. Run HeapScope as that user.";
        }

        if (target.Environment is not { } environment)
        {
            return null;
        }

        if (environment.DiagnosticsOff is { } off)
        {
            return $"Process {pid} turned its diagnostics off ({off}=0), so no tool can capture it. Start it without that "
                + "setting, then capture it again.";
        }

        return string.Equals(Real(environment.TempFolder), Real(target.OwnTempFolder), StringComparison.OrdinalIgnoreCase)
            ? null
            : $"Process {pid} keeps its diagnostics socket in {environment.TempFolder} (its {(environment.TempFolderSet ? "TMPDIR" : "TMPDIR is not set")}), and HeapScope looks in "
                + $"{TargetEnvironment.Folder(target.OwnTempFolder)}. Start HeapScope with TMPDIR={environment.TempFolder}, then capture it again.";
    }

    /// <summary>What a not-found decision rests on, for the log: ids, how each read ended, folders and the regions.</summary>
    public static string Evidence(MacTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var environment = target.Environment;
        var line = new StringBuilder();
        line.Append(CultureInfo.InvariantCulture, $"macOS check of pid {target.ProcessId}: running {(target.Running ? "yes" : "no")}; ");
        line.Append(CultureInfo.InvariantCulture, $"effective user {(target.UserId is null ? $"not readable ({target.UserOutcome})" : Id(target.UserId))}, HeapScope's {Id(target.OwnUserId)}; ");
        line.Append(CultureInfo.InvariantCulture, $"environment {(environment is null ? $"not readable ({target.EnvironmentOutcome})" : "readable")}; ");
        line.Append(CultureInfo.InvariantCulture, $"diagnostics off: {(environment is null ? "unknown" : environment.DiagnosticsOff is { } off ? off + "=0" : "none")}; ");
        line.Append(CultureInfo.InvariantCulture, $"TMPDIR {(environment is null ? "unknown" : environment.TempFolderSet ? environment.TempFolder : "not set, so " + environment.TempFolder)}, HeapScope's {TargetEnvironment.Folder(target.OwnTempFolder)}; ");
        line.Append(CultureInfo.InvariantCulture, $"regions {target.Walk?.ToString() ?? "not read"}");
        return line.ToString();
    }

    /// <summary>What macOS says now about <paramref name="processId"/>.</summary>
    public static MacTarget Read(int processId)
    {
        var facts = MacProcessFacts.Instance;
        var running = IsRunning(processId);
        var own = facts.EffectiveUserId(System.Environment.ProcessId);
        var user = facts.EffectiveUserId(processId, out var userOutcome);
        var environment = facts.Environment(processId, out var environmentOutcome);
        return new MacTarget(
            processId,
            running,
            user,
            userOutcome,
            own,
            environment,
            environmentOutcome,
            running ? MacProcessFacts.Walk(processId, stopAtRuntime: true) : null,
            Path.GetTempPath());
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary><paramref name="folder"/> as macOS stores it: <c>/tmp</c> and <c>/var</c> are links into <c>/private</c>.</summary>
    private static string Real(string folder)
    {
        folder = TargetEnvironment.Folder(folder);
        return folder.Equals("/tmp", StringComparison.OrdinalIgnoreCase) || folder.StartsWith("/tmp/", StringComparison.OrdinalIgnoreCase) || folder.StartsWith("/var/", StringComparison.OrdinalIgnoreCase)
            ? "/private" + folder
            : folder;
    }

    private static string? Matched(string message, params ReadOnlySpan<string> texts)
    {
        foreach (var text in texts)
        {
            if (message.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }
        }

        return null;
    }

    private static string Id(int? id) => id is { } value ? ((uint)value).ToString(CultureInfo.InvariantCulture) : "unknown";
}
