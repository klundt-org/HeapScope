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

/// <summary>An estimated dump size and what it rests on, for the log.</summary>
/// <param name="Bytes">The estimate.</param>
/// <param name="Walked">The readable bytes a walk of the process's memory gave; null when it could not be walked.</param>
/// <param name="Private">The process's private bytes; null when not readable.</param>
/// <param name="Uncounted">What a dump holds that the walk leaves out (macOS: the shared cache).</param>
/// <param name="FromWalk">Whether the walk decided, not the fallback from private bytes.</param>
public sealed record DumpEstimate(long Bytes, long? Walked, long? Private, long Uncounted, bool FromWalk)
{
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Bytes} bytes, from {(FromWalk ? "the memory walk" : "the private bytes")} (walk {Known(Walked)}, private bytes {Known(Private)}, not in the walk {Uncounted})");

    private static string Known(long? bytes) => bytes is { } known ? known.ToString(CultureInfo.InvariantCulture) : "not readable";
}
