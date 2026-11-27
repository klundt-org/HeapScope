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

using HeapScope.Contracts;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// A drawn arc of the sunburst: its segment, its ring's inner and outer radius in pixels, and its start and sweep in radians,
/// clockwise from the top. The top segment is the center disc (inner radius 0, a full turn).
/// </summary>
public sealed record SunburstArc(IcicleSegmentDto Segment, double Inner, double Outer, double Start, double Sweep, int Count = 1)
{
    /// <summary>Whether this arc stands for several narrow segments.</summary>
    public bool IsGroup => Count > 1;

    /// <summary>Whether this is the center disc.</summary>
    public bool IsCenter => Inner <= 0;
}
