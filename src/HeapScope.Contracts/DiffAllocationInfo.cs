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


namespace HeapScope.Contracts;

/// <summary>
/// The sampled allocations a diff counted (sprint 4.4). Sizes are estimates: what the runtime's samples stand for.
/// </summary>
/// <param name="FromSeconds">The start of what was counted, in seconds since the recording started.</param>
/// <param name="ToSeconds">Its end.</param>
/// <param name="AskedFromSeconds">The start that was asked for; before 0 when the recording started later.</param>
/// <param name="AskedToSeconds">The end that was asked for; past <paramref name="RecordedSeconds"/> when the recording ended earlier.</param>
/// <param name="RowBytes">The bytes the diff's rows hold (before the filter).</param>
/// <param name="AbsentBytes">Bytes of <paramref name="AbsentTypes"/> types without an object in either snapshot: allocated and collected between them.</param>
/// <param name="SharedNameBytes">Bytes of a name that types of several modules share, which a gcdump cannot tell apart: no row holds them.</param>
/// <param name="LostEvents">Events the runtime dropped while recording: the figures are low by their samples.</param>
public sealed record DiffAllocationInfo(
    double FromSeconds,
    double ToSeconds,
    double AskedFromSeconds,
    double AskedToSeconds,
    double RecordedSeconds,
    int Samples,
    long Bytes,
    long RowBytes,
    long AbsentBytes,
    int AbsentTypes,
    long SharedNameBytes,
    long LostEvents);
