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
    private static readonly bool s_linux = OperatingSystem.IsLinux();

    /// <summary>The Processes page's mark on a process HeapScope cannot reach as it runs now.</summary>
    public static string NeedsMore => NeedsMoreOn(s_linux);

    /// <summary>The mark on a process that needs more rights, which HeapScope has (it runs as administrator or root).</summary>
    public static string Reachable => ReachableOn(s_linux);

    /// <summary>The Processes page's banner when a process is marked <see cref="NeedsMore"/>.</summary>
    public static string Hint => HintOn(s_linux);

    /// <summary>The tooltip of the Processes page's Elevation column.</summary>
    public static string ColumnTip => ColumnTipOn(s_linux);

    /// <summary>The command line's note under its process list when a process is marked elevated.</summary>
    public static string CliNote => CliNoteOn(s_linux);

    /// <summary>Why the tutorial's sample does not start while HeapScope runs with more rights.</summary>
    public static string SampleRefused => SampleRefusedOn(s_linux);

    public static string NeedsMoreOn(bool linux) => linux ? "Needs root or its user" : "Needs administrator";

    public static string ReachableOn(bool linux) => linux ? "Another user's" : "Elevated";

    public static string HintOn(bool linux) => linux
        ? "Processes marked 'Needs root or its user' run as another user. To capture them, run HeapScope as that user, or as root (sudo)."
        : "Processes marked 'Needs administrator' run elevated or as another user. To capture them, run HeapScope as administrator.";

    public static string ColumnTipOn(bool linux) => linux
        ? "\"Needs root or its user\" means you must run HeapScope as that process's user, or as root, to capture it. \"Another user's\" means it runs as another user and HeapScope runs as root, so it can."
        : "\"Needs administrator\" means you must run HeapScope as administrator to capture this process. \"Elevated\" means it runs elevated and HeapScope does too.";

    public static string CliNoteOn(bool linux) => linux
        ? "Processes marked elevated run as another user: run heapscope-cli as that user, or with sudo, to capture them."
        : "Processes marked elevated can only be captured when HeapScope runs as administrator.";

    public static string SampleRefusedOn(bool linux) => linux
        ? "HeapScope runs as root, so the sample would too. Start HeapScope as your own user to run it."
        : "HeapScope runs as administrator, so the sample would too. Start HeapScope without administrator rights to run it.";

    /// <summary>The refusal to reach a process that needs more rights: what HeapScope cannot do, and what to do instead.</summary>
    public static string Refusal(string? name, int processId, string cannot, string again, bool linux) => linux
        ? $"{name ?? "The process"} (pid {processId}) runs as another user, so HeapScope cannot {cannot}. Run HeapScope as that user, or as root (sudo), and {again}."
        : $"{name ?? "The process"} (pid {processId}) runs elevated or as another user, so HeapScope cannot {cannot}. Run HeapScope as administrator (right-click, Run as administrator) and {again}.";
}
