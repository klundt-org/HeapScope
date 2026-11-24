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
using System.Text;

namespace HeapScope.Capture;

/// <summary>What macOS says about a process that was asked for by pid and not found (sprint 4.2); null where it could not be read.</summary>
/// <param name="UserId">Its effective user id, which anyone may read; null once it exited.</param>
/// <param name="OwnUserId">HeapScope's effective user id.</param>
/// <param name="Environment">Its environment, which macOS gives its own user and root, and for a protected process nobody.</param>
/// <param name="RegionsRead">Whether its memory regions could be read.</param>
/// <param name="RuntimeFolder">The folder of the <c>libcoreclr.dylib</c> it maps.</param>
/// <param name="ExecutableCarriesRuntime">Whether its executable holds the runtime (a single-file app); null when not read.</param>
/// <param name="OwnTempFolder">The folder this HeapScope looks in (its <c>TMPDIR</c>, or <c>/tmp</c>).</param>
internal sealed record MacTarget(
    int ProcessId,
    int? UserId,
    int? OwnUserId,
    TargetEnvironment? Environment,
    bool RegionsRead,
    string? RuntimeFolder,
    bool? ExecutableCarriesRuntime,
    string OwnTempFolder);

/// <summary>
/// Why macOS refuses a capture, and what to do (sprint 4.2, untested). HeapScope never signs a target or changes its
/// entitlements; it says what the runtime reported. The rules take what was read, so they are tested on every system.
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
    /// Why the runtime's createdump could not write a dump, from what it reported. Its <c>task_for_pid</c> failure is the same
    /// whatever the cause, and it names signing and entitlements itself, so the reason says no more than that. Null when
    /// nothing explains it. <paramref name="rule"/> names the rule that decided, for the log.
    /// </summary>
    public static string? WriteFailure(string message, string account, string directory, out string rule)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (Has(message, "0x80070070") || Has(message, "No space left"))
        {
            rule = "no space left";
            return "the disk is full";
        }

        if (Has(message, "task_for_pid") || Has(message, "signed and entitled"))
        {
            rule = "task_for_pid refused";
            return "macOS did not let the runtime examine the process, which can mean that the application or the runtime is "
                + "not signed with the entitlements that allow it. A gcdump does not need this: capture one instead";
        }

        if (Has(message, "0x80070005") || Has(message, "Permission denied") || Has(message, "Could not open"))
        {
            rule = "the folder is not writable by the target's account";
            return $"the account {account} runs as cannot write to {directory}";
        }

        rule = "none matched";
        return null;
    }

    /// <summary>
    /// Why a running process has no diagnostics socket where HeapScope looks. Null when it is not running or nothing
    /// explains it (the generic not-found text follows).
    /// </summary>
    public static string? WhyNoDiagnostics(MacTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.UserId is not { } user)
        {
            return null;
        }

        var pid = target.ProcessId.ToString(CultureInfo.InvariantCulture);
        if (target.OwnUserId is { } own && user != own && own != 0)
        {
            return $"Process {pid} runs as another user, and HeapScope finds a process by the diagnostics socket it creates "
                + "in its own temp folder, which on macOS differs per user. Run HeapScope as that user.";
        }

        if (target.Environment is { } environment)
        {
            if (environment.DiagnosticsOff is { } off)
            {
                return $"Process {pid} turned its diagnostics off ({off}=0), so no tool can capture it. Start it without that "
                    + "setting, then capture it again.";
            }

            if (!string.Equals(Real(environment.TempFolder), Real(target.OwnTempFolder), StringComparison.Ordinal))
            {
                return $"Process {pid} keeps its diagnostics socket in {environment.TempFolder} (its TMPDIR), and HeapScope looks in "
                    + $"{TargetEnvironment.Folder(target.OwnTempFolder)}. Start HeapScope with TMPDIR={environment.TempFolder}, then capture it again.";
            }
        }

        return target.RegionsRead && target.RuntimeFolder is null && target.ExecutableCarriesRuntime == false
            ? $"Process {pid} is not a .NET process: no .NET runtime is loaded in it."
            : null;
    }

    /// <summary>What a not-found decision rests on, for the log: ids, what was readable, folders and the runtime.</summary>
    public static string Evidence(MacTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var runtime = !target.RegionsRead ? "regions not readable"
            : target.RuntimeFolder is { } folder ? "libcoreclr.dylib in " + folder
            : target.ExecutableCarriesRuntime == true ? "in its executable (single-file)"
            : target.ExecutableCarriesRuntime is null ? "none in its regions, executable not read"
            : "none";
        var environment = target.Environment;
        var line = new StringBuilder();
        line.Append(CultureInfo.InvariantCulture, $"macOS check of pid {target.ProcessId}: running {(target.UserId is null ? "no" : "yes")}; ");
        line.Append(CultureInfo.InvariantCulture, $"effective user {Id(target.UserId)}, HeapScope's {Id(target.OwnUserId)}; ");
        line.Append(CultureInfo.InvariantCulture, $"environment {(environment is null ? "not given" : "readable")}; ");
        line.Append(CultureInfo.InvariantCulture, $"diagnostics off: {(environment is null ? "unknown" : environment.DiagnosticsOff is { } off ? off + "=0" : "none")}; ");
        line.Append(CultureInfo.InvariantCulture, $"TMPDIR {environment?.TempFolder ?? "unknown"}, HeapScope's {TargetEnvironment.Folder(target.OwnTempFolder)}; ");
        line.Append(CultureInfo.InvariantCulture, $"runtime {runtime}");
        return line.ToString();
    }

    /// <summary>What macOS says now about <paramref name="processId"/>.</summary>
    public static MacTarget Read(int processId)
    {
        var facts = MacProcessFacts.Instance;
        var own = facts.EffectiveUserId(System.Environment.ProcessId);
        if (facts.EffectiveUserId(processId) is not { } user)
        {
            return new MacTarget(processId, null, own, null, false, null, null, Path.GetTempPath());
        }

        // One walk: whether any region was read, and the runtime among them.
        var read = false;
        var runtime = MacProcData.Runtime(MacProcessFacts.Regions(processId).Select(region =>
        {
            read = true;
            return region;
        }));
        return new MacTarget(
            processId,
            user,
            own,
            facts.Environment(processId),
            read,
            runtime?.Directory,

            // Only read when the regions hold no runtime: it decides "not .NET" against a single-file app.
            read && runtime is null && facts.ImagePath(processId) is { } image ? RuntimeLibrary.IsCarriedBy(image) : null,
            Path.GetTempPath());
    }

    /// <summary><paramref name="folder"/> as macOS stores it: <c>/tmp</c> and <c>/var</c> are links into <c>/private</c>.</summary>
    private static string Real(string folder)
    {
        folder = TargetEnvironment.Folder(folder);
        return folder == "/tmp" || folder.StartsWith("/tmp/", StringComparison.Ordinal) || folder.StartsWith("/var/", StringComparison.Ordinal)
            ? "/private" + folder
            : folder;
    }

    private static bool Has(string message, string text) => message.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static string Id(int? id) => id is { } value ? value.ToString(CultureInfo.InvariantCulture) : "unknown";
}
