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

namespace HeapScope.Core;

/// <summary>
/// The words for a process HeapScope needs more rights to reach, per system (sprint 4.1): on Windows, run HeapScope as
/// administrator; on Linux, run it as the process's own user or as root (the capture layer's UnixElevationProbe). Each text has
/// an <c>On</c> form taking the system, so both are tested everywhere. Linux texts: drafts for the owner (info-text.md).
/// </summary>
public static class ElevationTexts
{
    private static readonly HostSystem s_system = HostSystems.Current;

    /// <summary>The Processes page's mark on a process HeapScope cannot reach as it runs now.</summary>
    public static string NeedsMore => NeedsMoreOn(s_system);

    /// <summary>The mark on a process that needs more rights, which HeapScope has (it runs as administrator or root).</summary>
    public static string Reachable => ReachableOn(s_system);

    /// <summary>The Processes page's banner when a process is marked <see cref="NeedsMore"/>.</summary>
    public static string Hint => HintOn(s_system);

    /// <summary>The tooltip of the Processes page's Elevation column.</summary>
    public static string ColumnTip => ColumnTipOn(s_system);

    /// <summary>The command line's note under its process list when a process is marked elevated.</summary>
    public static string CliNote => CliNoteOn(s_system);

    /// <summary>Why the tutorial's sample does not start while HeapScope runs with more rights.</summary>
    public static string SampleRefused => SampleRefusedOn(s_system);

    public static string NeedsMoreOn(HostSystem system) => HostSystems.RightsGoByUser(system) ? "Needs root or its user" : "Needs administrator";

    public static string ReachableOn(HostSystem system) => HostSystems.RightsGoByUser(system) ? "Another user's" : "Elevated";

    public static string HintOn(HostSystem system) => HostSystems.RightsGoByUser(system)
        ? "Processes marked 'Needs root or its user' run as another user. To capture them, run HeapScope as that user, or as root (sudo)."
        : "Processes marked 'Needs administrator' run elevated or as another user. To capture them, run HeapScope as administrator.";

    public static string ColumnTipOn(HostSystem system) => HostSystems.RightsGoByUser(system)
        ? "\"Needs root or its user\" means you must run HeapScope as that process's user, or as root, to capture it. \"Another user's\" means it runs as another user and HeapScope runs as root, so it can."
        : "\"Needs administrator\" means you must run HeapScope as administrator to capture this process. \"Elevated\" means it runs elevated and HeapScope does too.";

    public static string CliNoteOn(HostSystem system) => HostSystems.RightsGoByUser(system)
        ? "Processes marked elevated run as another user: run heapscope-cli as that user, or with sudo, to capture them."
        : "Processes marked elevated can only be captured when HeapScope runs as administrator.";

    public static string SampleRefusedOn(HostSystem system) => HostSystems.RightsGoByUser(system)
        ? "HeapScope runs as root, so the sample would too. Start HeapScope as your own user to run it."
        : "HeapScope runs as administrator, so the sample would too. Start HeapScope without administrator rights to run it.";

    /// <summary>The label of the check box that lists the processes hidden because they need more rights (sprint 4.1.1).</summary>
    public static string ShowHidden => ShowHiddenOn(s_system);

    /// <summary>The tooltip of <see cref="ShowHidden"/>.</summary>
    public static string ShowHiddenTip => ShowHiddenTipOn(s_system);

    public static string ShowHiddenOn(HostSystem system) => HostSystems.RightsGoByUser(system) ? "Show other users' processes" : "Show elevated processes";

    public static string ShowHiddenTipOn(HostSystem system) => HostSystems.RightsGoByUser(system)
        ? "Also lists the processes of other users. HeapScope can capture them only when it runs as that user or as root (sudo)."
        : "Also lists the processes that run elevated or as another user. HeapScope can capture them only when it runs as administrator.";

    /// <summary>The note in place of <see cref="Hint"/> while <paramref name="count"/> processes are hidden (sprint 4.1.1).</summary>
    public static string HiddenNote(int count) => HiddenNoteOn(count, s_system);

    public static string HiddenNoteOn(int count, HostSystem system)
    {
        var n = count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        var them = count == 1 ? "it" : "them";
        // The box's own label, so the two cannot drift; on Windows the set the banner names (review: elevated or another user's).
        var box = ShowHiddenOn(system);
        return HostSystems.RightsGoByUser(system)
            ? (count == 1 ? "1 process of another user is hidden" : $"{n} processes of other users are hidden")
                + $": HeapScope cannot capture {them} unless it runs as that user or as root (sudo). Check '{box}' to list {them}."
            : (count == 1 ? "1 process that runs elevated or as another user is hidden" : $"{n} processes that run elevated or as another user are hidden")
                + $": HeapScope cannot capture {them} unless it runs as administrator. Check '{box}' to list {them}.";
    }

    /// <summary>The Learn page's note on who can capture whom, where rights go by user (sprint 4.1 task 8); null elsewhere.</summary>
    public static string? LearnNote => LearnNoteOn(s_system);

    public static string? LearnNoteOn(HostSystem system) => system == HostSystem.Linux
        ? "On Linux, HeapScope reaches a process that runs as your own user, as the sample does. Another user's process needs HeapScope run as that user or as root, and some systems block it altogether (kernel.yama.ptrace_scope); when a capture is refused, HeapScope says what to change."
        : null;

    /// <summary>The refusal to reach a process that needs more rights: what HeapScope cannot do, and what to do instead.</summary>
    public static string Refusal(string? name, int processId, string cannot, string again, HostSystem system) => HostSystems.RightsGoByUser(system)
        ? $"{name ?? "The process"} (pid {processId}) runs as another user, so HeapScope cannot {cannot}. Run HeapScope as that user, or as root (sudo), and {again}."
        : $"{name ?? "The process"} (pid {processId}) runs elevated or as another user, so HeapScope cannot {cannot}. Run HeapScope as administrator (right-click, Run as administrator) and {again}.";
}
