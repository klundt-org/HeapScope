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

/// <summary>
/// What <c>/proc</c> says about a process that was asked for by pid and not found (sprint 4.1, review H3, H6 and M1), as
/// read: null where a file could not be read.
/// </summary>
/// <param name="Running">Whether its <c>/proc</c> folder and <c>status</c> could be read (false once it exited).</param>
/// <param name="UserId">Its effective user id, from <c>status</c>, which anyone may read.</param>
/// <param name="OwnUserId">HeapScope's effective user id.</param>
/// <param name="Environ">Its environment, readable by its own user and root only.</param>
/// <param name="Maps">Its mappings, readable by its own user and root only.</param>
/// <param name="SameMountNamespace">Whether it sees the same files (and <c>/tmp</c>) as HeapScope; null when unknown.</param>
/// <param name="ExecutableCarriesRuntime">Whether its executable holds the runtime (a single-file app); null when not read.</param>
/// <param name="OwnTempFolder">The folder this HeapScope looks in (its <c>TMPDIR</c>, or <c>/tmp</c>).</param>
internal sealed record LinuxTarget(
    int ProcessId,
    bool Running,
    int? UserId,
    int? OwnUserId,
    string? Environ,
    string? Maps,
    bool? SameMountNamespace,
    bool? ExecutableCarriesRuntime,
    string OwnTempFolder);

/// <summary>
/// Why Linux refuses a capture, and what to do (sprint 4.1 task 5). HeapScope never changes system settings such as
/// <c>ptrace_scope</c> or a container's capabilities; it says what to change. The rules take what was read, so they are
/// tested on every system; the readers below read <c>/proc</c>. Each decision comes with the evidence it rests on, which
/// the log keeps (review H6), and never with a variable's value other than <c>TMPDIR</c>.
/// </summary>
internal static class LinuxCaptureRules
{
    /// <summary>The variables that turn a process's diagnostics port off (DOTNET_ and the older COMPlus_ spellings).</summary>
    private static readonly string[] s_diagnosticsOff =
        ["DOTNET_EnableDiagnostics", "COMPlus_EnableDiagnostics", "DOTNET_EnableDiagnostics_IPC", "COMPlus_EnableDiagnostics_IPC"];

    /// <summary>A runtime library's version string, also in a single-file app's executable, which links the runtime in.</summary>
    private static readonly byte[] s_runtimeMarker = "@(#)Version "u8.ToArray();

    /// <summary>
    /// Why the runtime could not write a dump, from the system's ptrace rule (<c>kernel.yama.ptrace_scope</c>) and whether
    /// HeapScope runs in a container: the runtime's createdump examines the target with ptrace. Null when neither explains it.
    /// </summary>
    public static string? WriteFailureReason(int? ptraceScope, bool inContainer) => ptraceScope switch
    {
        >= 3 => "this system lets no process examine another (kernel.yama.ptrace_scope is 3), and the runtime needs to "
            + "examine the process to write its dump. Only restarting the system with a lower setting allows it",
        2 => "this system lets only administrators' tools examine another process (kernel.yama.ptrace_scope is 2), and the "
            + "runtime writes the dump as the process's own user. An administrator can allow it until the next restart with "
            + "'sudo sysctl kernel.yama.ptrace_scope=1'",
        _ when inContainer => "HeapScope runs in a container, which may not let the runtime examine the process (the "
            + "SYS_PTRACE capability). Start the container with --cap-add=SYS_PTRACE",
        _ => null,
    };

    /// <summary>
    /// Why the runtime's createdump could not write a dump on Linux (review H2), from what it reported, before the system's
    /// settings: a full disk and a folder the target's account cannot write are named as such, and the ptrace rule or the
    /// container only when createdump reported that it could not examine the process, or when nothing can (scope 3). Null
    /// when nothing explains it. <paramref name="rule"/> names the rule that decided, for the log.
    /// </summary>
    public static string? WriteFailure(string message, int? ptraceScope, bool inContainer, string account, string directory, out string rule)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (Has(message, "0x80070070") || Has(message, "No space left"))
        {
            rule = "no space left";
            return "the disk is full";
        }

        var ptrace = Has(message, "ptrace") || Has(message, "Operation not permitted");
        if (Has(message, "0x80070005") || (!ptrace && (Has(message, "Permission denied") || Has(message, "Could not open"))))
        {
            rule = "the folder is not writable by the target's account";
            return $"the account {account} runs as cannot write to {directory}";
        }

        if (ptrace || ptraceScope >= 3)
        {
            var reason = WriteFailureReason(ptraceScope, inContainer);
            rule = reason is null ? "ptrace refused, no setting explains it" : ptrace ? "ptrace refused" : "ptrace_scope 3";
            return reason;
        }

        rule = "none matched";
        return null;
    }

    /// <summary>The settings a write failure is judged by, for the log: they explain the reason given, or its absence.</summary>
    public static string WriteEvidence(int? ptraceScope, bool inContainer, string rule) => string.Create(
        CultureInfo.InvariantCulture,
        $"ptrace_scope {(ptraceScope is { } scope ? scope.ToString(CultureInfo.InvariantCulture) : "not present")}; container {(inContainer ? "yes" : "no")}; rule: {rule}");

    /// <summary>
    /// Why a full dump of another user's process cannot go into HeapScope's own folders while HeapScope runs as root (review
    /// H1): the runtime writes the dump as the process's own user, and HeapScope's folders are root's alone.
    /// </summary>
    public static string RootLibraryRefusal(string name, int processId) => string.Create(
        CultureInfo.InvariantCulture,
        $"{name} (pid {processId}) runs as another user, and the runtime writes its full dump as that user, who cannot write to HeapScope's folders while HeapScope runs as root. Run HeapScope as that user to capture a full dump, or capture a gcdump; on the command line, --out can name a folder that user can write.");

    /// <summary>
    /// Why a running process has no diagnostics socket where HeapScope looks. Null when it is not running or nothing
    /// explains it (the generic not-found text follows).
    /// </summary>
    public static string? WhyNoDiagnostics(LinuxTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.Running || target.UserId is not { } user)
        {
            return null;
        }

        var pid = target.ProcessId.ToString(CultureInfo.InvariantCulture);
        // As root, another user's process is reachable when both use the same temp folder: the checks below say why not.
        if (target.OwnUserId is { } own && user != own && own != 0)
        {
            return $"Process {pid} runs as another user, and HeapScope finds a process by the diagnostics socket it creates "
                + "in its own temp folder. Run HeapScope as that user, or as root (sudo) when both use the same temp folder.";
        }

        if (target.Environ is not { } environ)
        {
            return null;
        }

        var variables = Variables(environ);
        if (DiagnosticsOff(variables) is { } off)
        {
            return $"Process {pid} turned its diagnostics off ({off}=0), so no tool can capture it. Start it without that "
                + "setting, then capture it again.";
        }

        if (target.SameMountNamespace == false)
        {
            return $"Process {pid} probably has its own /tmp (a service with PrivateTmp, or a container), so HeapScope cannot "
                + "see its diagnostics socket. Run HeapScope where the process runs (in its container, or with the service's settings), "
                + "then capture it again.";
        }

        var theirs = TheirTempFolder(variables);
        if (!string.Equals(theirs, Folder(target.OwnTempFolder), StringComparison.Ordinal))
        {
            return $"Process {pid} keeps its diagnostics socket in {theirs} (its TMPDIR), and HeapScope looks in "
                + $"{Folder(target.OwnTempFolder)}. Start HeapScope with TMPDIR={theirs}, then capture it again.";
        }

        if (target.Maps is { } maps && !LoadsRuntime(maps) && target.ExecutableCarriesRuntime == false)
        {
            return $"Process {pid} is not a .NET process: no .NET runtime is loaded in it.";
        }

        return null;
    }

    /// <summary>What a not-found decision rests on, for the log (review H6): ids, readable files, folders and the runtime.</summary>
    public static string Evidence(LinuxTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var variables = target.Environ is { } environ ? Variables(environ) : null;
        var runtime = target.Maps is not { } maps ? "maps not readable"
            : ProcText.Runtime(maps) is { } found ? "libcoreclr.so in " + found.Directory
            : maps.Contains("libclrjit.so", StringComparison.Ordinal) ? "libclrjit.so"
            : target.ExecutableCarriesRuntime == true ? "in its executable (single-file)"
            : target.ExecutableCarriesRuntime is null ? "none in maps, executable not read"
            : "none";
        var line = new StringBuilder();
        line.Append(CultureInfo.InvariantCulture, $"Linux check of pid {target.ProcessId}: running {(target.Running ? "yes" : "no")}; ");
        line.Append(CultureInfo.InvariantCulture, $"effective user {Id(target.UserId)}, HeapScope's {Id(target.OwnUserId)}; ");
        line.Append(CultureInfo.InvariantCulture, $"environ {(variables is null ? "not readable" : "readable")}; ");
        line.Append(CultureInfo.InvariantCulture, $"diagnostics off: {(variables is null ? "unknown" : DiagnosticsOff(variables) is { } off ? off + "=0" : "none")}; ");
        line.Append(CultureInfo.InvariantCulture, $"TMPDIR {(variables is null ? "unknown" : TheirTempFolder(variables))}, HeapScope's {Folder(target.OwnTempFolder)}; ");
        line.Append(CultureInfo.InvariantCulture, $"mount namespace {(target.SameMountNamespace switch { true => "same", false => "another", null => "unknown" })}; ");
        line.Append(CultureInfo.InvariantCulture, $"runtime {runtime}");
        return line.ToString();
    }

    /// <summary>
    /// The failure for a process that did not answer on its diagnostics port (Live and recording, review M2 and H6): on
    /// Linux the reason a capture would give, with its evidence for the log; <paramref name="otherwise"/> when none applies.
    /// </summary>
    public static TargetUnavailableException NotAnswering(int processId, string otherwise, Exception inner)
    {
        if (!OperatingSystem.IsLinux())
        {
            return new TargetUnavailableException(otherwise, inner);
        }

        var target = Read(processId);
        return new TargetUnavailableException(WhyNoDiagnostics(target) ?? otherwise, inner) { Evidence = Evidence(target) };
    }

    /// <summary>The value of <c>kernel.yama.ptrace_scope</c>, or null where Yama is not built in.</summary>
    public static int? PtraceScope() =>
        Read("/proc/sys/kernel/yama/ptrace_scope") is { } text && int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var scope)
            ? scope
            : null;

    /// <summary>Whether HeapScope runs in a container (Docker's or Podman's marker file).</summary>
    public static bool InContainer() => File.Exists("/.dockerenv") || File.Exists("/run/.containerenv");

    /// <summary>What <c>/proc</c> holds now about <paramref name="processId"/>.</summary>
    public static LinuxTarget Read(int processId)
    {
        var folder = string.Create(CultureInfo.InvariantCulture, $"/proc/{processId}");
        var user = Directory.Exists(folder) ? LinuxProcessFacts.EffectiveUserId(processId) : null;
        var maps = user is null ? null : Read(folder + "/maps");
        return new LinuxTarget(
            processId,
            Running: user is not null,
            user,
            LinuxProcessFacts.EffectiveUserId(Environment.ProcessId),
            user is null ? null : Read(folder + "/environ"),
            maps,
            SameMountNamespace(folder),
            // Only read when the mappings hold no runtime: it decides "not .NET" against a single-file app.
            maps is not null && !LoadsRuntime(maps) ? CarriesRuntime(folder + "/exe") : null,
            Path.GetTempPath());
    }

    /// <summary>Whether <paramref name="executable"/> holds the runtime's version string, read in pieces (it can be large).</summary>
    internal static bool? CarriesRuntime(string executable)
    {
        try
        {
            using var stream = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, FileOptions.SequentialScan);
            return Contains(stream, s_runtimeMarker);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Whether <paramref name="stream"/> holds <paramref name="marker"/>, searched in 64 KB pieces that overlap by its length.</summary>
    internal static bool Contains(Stream stream, ReadOnlySpan<byte> marker)
    {
        var buffer = new byte[(64 * 1024) + marker.Length];
        var kept = 0;
        int read;
        while ((read = stream.Read(buffer, kept, buffer.Length - kept)) > 0)
        {
            var filled = kept + read;
            if (buffer.AsSpan(0, filled).IndexOf(marker) >= 0)
            {
                return true;
            }

            kept = Math.Min(marker.Length - 1, filled);
            buffer.AsSpan(filled - kept, kept).CopyTo(buffer);
        }

        return false;
    }

    private static bool LoadsRuntime(string maps) => ProcText.Runtime(maps) is not null || maps.Contains("libclrjit.so", StringComparison.Ordinal);

    private static bool? SameMountNamespace(string folder)
    {
        try
        {
            var own = new FileInfo("/proc/self/ns/mnt").LinkTarget;
            var theirs = new FileInfo(folder + "/ns/mnt").LinkTarget;
            return own is null || theirs is null ? null : string.Equals(own, theirs, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? DiagnosticsOff(Dictionary<string, string> variables)
    {
        foreach (var name in s_diagnosticsOff)
        {
            if (variables.TryGetValue(name, out var value) && value.Trim() == "0")
            {
                return name;
            }
        }

        return null;
    }

    private static string TheirTempFolder(Dictionary<string, string> variables) =>
        Folder(variables.TryGetValue("TMPDIR", out var tmp) && tmp.Length > 0 ? tmp : "/tmp");

    private static Dictionary<string, string> Variables(string environ)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in environ.Split('\0'))
        {
            var equals = entry.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                variables.TryAdd(entry[..equals], entry[(equals + 1)..]);
            }
        }

        return variables;
    }

    private static bool Has(string message, string text) => message.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static string Id(int? id) => id is { } value ? value.ToString(CultureInfo.InvariantCulture) : "unknown";

    private static string Folder(string path) => path.Length > 1 ? path.TrimEnd('/') : path;

    private static string? Read(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
