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

namespace HeapScope.Capture;

/// <summary>
/// Why Linux refuses a capture, and what to do (sprint 4.1 task 5). HeapScope never changes system settings such as
/// <c>ptrace_scope</c> or a container's capabilities; it says what to change. The rules take what was read, so they are
/// tested on every system; the readers below read <c>/proc</c>.
/// </summary>
internal static class LinuxCaptureRules
{
    /// <summary>The variables that turn a process's diagnostics port off (DOTNET_ and the older COMPlus_ spellings).</summary>
    private static readonly string[] s_diagnosticsOff =
        ["DOTNET_EnableDiagnostics", "COMPlus_EnableDiagnostics", "DOTNET_EnableDiagnostics_IPC", "COMPlus_EnableDiagnostics_IPC"];

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
    /// Why a running process has no diagnostics socket where HeapScope looks, from its environment (<c>environ</c>, readable
    /// by its own user and root) and its mappings. Null when it is not running or nothing explains it.
    /// </summary>
    /// <param name="ownTempFolder">The folder this HeapScope looks in (its <c>TMPDIR</c>, or <c>/tmp</c>).</param>
    public static string? WhyNoDiagnostics(int processId, bool running, string? environ, string? maps, string ownTempFolder)
    {
        if (!running)
        {
            return null;
        }

        var pid = processId.ToString(CultureInfo.InvariantCulture);
        if (environ is null)
        {
            return $"Process {pid} runs as another user, and HeapScope finds a process by the diagnostics socket it creates "
                + "in its own temp folder. Run HeapScope as that user, or as root (sudo) when both use the same temp folder.";
        }

        var variables = Variables(environ);
        foreach (var name in s_diagnosticsOff)
        {
            if (variables.TryGetValue(name, out var value) && value.Trim() == "0")
            {
                return $"Process {pid} turned its diagnostics off ({name}=0), so no tool can capture it. Start it without that "
                    + "setting, then capture it again.";
            }
        }

        var theirs = Folder(variables.TryGetValue("TMPDIR", out var tmp) && tmp.Length > 0 ? tmp : "/tmp");
        if (!string.Equals(theirs, Folder(ownTempFolder), StringComparison.Ordinal))
        {
            return $"Process {pid} keeps its diagnostics socket in {theirs} (its TMPDIR), and HeapScope looks in "
                + $"{Folder(ownTempFolder)}. Start HeapScope with TMPDIR={theirs}, then capture it again.";
        }

        if (maps is not null && ProcText.Runtime(maps) is null && !maps.Contains("libclrjit.so", StringComparison.Ordinal))
        {
            return $"Process {pid} is not a .NET process: no .NET runtime is loaded in it.";
        }

        return null;
    }

    /// <summary>The value of <c>kernel.yama.ptrace_scope</c>, or null where Yama is not built in.</summary>
    public static int? PtraceScope() =>
        Read("/proc/sys/kernel/yama/ptrace_scope") is { } text && int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var scope)
            ? scope
            : null;

    /// <summary>Whether HeapScope runs in a container (Docker's or Podman's marker file).</summary>
    public static bool InContainer() => File.Exists("/.dockerenv") || File.Exists("/run/.containerenv");

    /// <summary><see cref="WhyNoDiagnostics(int, bool, string?, string?, string)"/> with what <c>/proc</c> holds now.</summary>
    public static string? WhyNoDiagnostics(int processId)
    {
        var folder = string.Create(CultureInfo.InvariantCulture, $"/proc/{processId}");
        return WhyNoDiagnostics(processId, Directory.Exists(folder), Read(folder + "/environ"), Read(folder + "/maps"), Path.GetTempPath());
    }

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
