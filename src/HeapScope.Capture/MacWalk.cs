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

/// <summary>What a walk of a process's regions on macOS held, and how it ended (sprint 4.2 review).</summary>
/// <param name="Regions">The regions read.</param>
/// <param name="WithPath">How many had a file.</param>
/// <param name="LastEnd">Where the last one ended.</param>
/// <param name="End">Why the walk ended: the outcome of the last call, or what stopped it.</param>
/// <param name="ReadableBytes">The readable bytes; null when no region was read or the sum is not plausible.</param>
/// <param name="Runtime">The runtime from the first region that maps it; null when none did.</param>
internal sealed record MacWalk(int Regions, int WithPath, ulong LastEnd, string End, long? ReadableBytes, RuntimeFacts? Runtime)
{
    /// <summary>The walk for the log, so a short one can be seen.</summary>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Regions} regions ({WithPath} with a file) to 0x{LastEnd:X}, ended: {End}; {(ReadableBytes is { } bytes ? bytes.ToString(CultureInfo.InvariantCulture) + " readable bytes" : "readable bytes unknown")}; runtime {Runtime?.Directory ?? "not seen"}");
}
