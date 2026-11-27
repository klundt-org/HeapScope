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
/// An allocation chart's segments in level order, left to right (their address is 0: a segment is a frame, found by its key); the
/// interval's sampled bytes, the top's width, and its samples; the rows of the drawn levels left out for being under the share, with
/// their bytes; and whether the segment cap cut the chart.
/// </summary>
public sealed record AllocationChartResult(
    IReadOnlyList<IcicleSegmentDto> Segments,
    long SampledBytes,
    int Samples,
    int LeftOut = 0,
    long LeftOutBytes = 0,
    bool Truncated = false);
