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

namespace HeapScope.Application.Sessions;

/// <summary>
/// The words for a process HeapScope needs more rights to reach on this system, for the front ends, which reach Core
/// only through this layer (the texts themselves are in <see cref="HeapScope.Core.ElevationTexts"/>).
/// </summary>
public static class RightsTexts
{
    /// <summary>Whether this system can capture a running process at all (the capture layer's answer).</summary>
    public static bool CanCapture => HeapScope.Capture.CapturePlatform.IsSupported;

    /// <summary>The Learn page's note on who can capture whom on this system, or null.</summary>
    public static string? LearnNote => HeapScope.Core.ElevationTexts.LearnNote;

    /// <summary>The Processes page's mark on a process HeapScope cannot reach as it runs now.</summary>
    public static string NeedsMore => HeapScope.Core.ElevationTexts.NeedsMore;

    /// <summary>The mark on a process that needs more rights, which HeapScope has.</summary>
    public static string Reachable => HeapScope.Core.ElevationTexts.Reachable;

    /// <summary>The Processes page's banner when a process is marked <see cref="NeedsMore"/>.</summary>
    public static string Hint => HeapScope.Core.ElevationTexts.Hint;

    /// <summary>The tooltip of the Processes page's Elevation column.</summary>
    public static string ColumnTip => HeapScope.Core.ElevationTexts.ColumnTip;

    /// <summary>The command line's note under its process list when a process is marked elevated.</summary>
    public static string CliNote => HeapScope.Core.ElevationTexts.CliNote;

    /// <summary>The label of the check box that lists the processes hidden because they need more rights.</summary>
    public static string ShowHidden => HeapScope.Core.ElevationTexts.ShowHidden;

    /// <summary>The tooltip of <see cref="ShowHidden"/>.</summary>
    public static string ShowHiddenTip => HeapScope.Core.ElevationTexts.ShowHiddenTip;

    /// <summary>The note that says how many processes are hidden, and why.</summary>
    public static string HiddenNote(int count) => HeapScope.Core.ElevationTexts.HiddenNote(count);
}
