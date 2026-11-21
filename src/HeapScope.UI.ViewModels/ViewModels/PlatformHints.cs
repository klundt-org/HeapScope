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

namespace HeapScope.UI.ViewModels;

/// <summary>
/// Next steps that depend on whether this system can reach a running process: Windows and Linux (sprint 4.1) can; elsewhere
/// dumps come from dotnet-dump (ADR-025). The pages' own banners give the same reason (sprint 3.10 review M20).
/// </summary>
public static class PlatformHints
{
    public static bool CanCapture { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <summary>The Processes page's banner for a process HeapScope needs more rights to reach, per system (sprint 4.1).</summary>
    public static string ElevationHint => HeapScope.Application.Sessions.RightsTexts.Hint;

    /// <summary>The Learn page's note on who can capture whom, on Linux only (sprint 4.1 task 8); null elsewhere.</summary>
    public static string? LearnNote { get; } = LearnNoteOn(OperatingSystem.IsLinux());

    /// <summary>Whether this system has a <see cref="LearnNote"/> (the page shows its box only then).</summary>
    public static bool HasLearnNote => LearnNote is not null;

    public static string? LearnNoteOn(bool linux) => linux
        ? "On Linux, HeapScope reaches a process that runs as your own user, as the sample does. Another user's process needs HeapScope run as that user or as root, and some systems block it altogether (kernel.yama.ptrace_scope); when a capture is refused, HeapScope says what to change."
        : null;

    /// <summary>The Processes page's Elevation column tooltip, per system (sprint 4.1).</summary>
    public static string ElevationColumnTip => HeapScope.Application.Sessions.RightsTexts.ColumnTip;

    /// <summary>The label of the check box that lists the processes hidden because they need more rights (sprint 4.1.1).</summary>
    public static string ShowHidden => HeapScope.Application.Sessions.RightsTexts.ShowHidden;

    /// <summary>The tooltip of <see cref="ShowHidden"/>.</summary>
    public static string ShowHiddenTip => HeapScope.Application.Sessions.RightsTexts.ShowHiddenTip;

    public static string SnapshotEmpty { get; } = SnapshotEmptyOn(CanCapture);

    public static string LiveEmpty { get; } = LiveEmptyOn(CanCapture);

    public static string AllocationsEmpty { get; } = AllocationsEmptyOn(CanCapture);

    public static string CaptureTip { get; } = CanCapture
        ? "Choose a running .NET process and capture a full dump of it."
        : "Capturing needs Windows or Linux. Take a dump with dotnet-dump, then open it here.";

    public static string LiveTip { get; } = CanCapture
        ? "Chart a running process's GC heap, and capture dumps when a rule fires."
        : "Watching a process live needs Windows or Linux. Take a dump with dotnet-dump, then open it here.";

    public static string SnapshotEmptyOn(bool canCapture) => canCapture
        ? "Open a dump (.dmp, .gcdump or a core dump) to see what is on the heap, or capture one on the Processes page."
        : "Open a full memory dump to see what is on the heap. Take one with dotnet-dump collect --process-id <pid> --type Full.";

    public static string LiveEmptyOn(bool canCapture) => canCapture
        ? "Choose a process and press Start to chart its GC heap."
        : "Watching a process live needs Windows or Linux. Take a dump with dotnet-dump and open it on the Snapshots page.";

    public static string AllocationsEmptyOn(bool canCapture) => canCapture
        ? "No allocation trace is open. Select a process on the Processes page and use Record allocations while the application runs, or open a .nettrace file. HeapScope then shows which types and call stacks allocated the most."
        : "No allocation trace is open. Recording allocations needs Windows or Linux; here you can open a .nettrace file. HeapScope then shows which types and call stacks allocated the most.";
}
