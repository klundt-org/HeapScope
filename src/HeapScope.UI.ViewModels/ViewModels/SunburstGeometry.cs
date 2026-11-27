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
/// The sunburst's geometry, independent of the UI toolkit, drawn from the icicle's segments (ADR-046): the top segment as the center
/// disc, each level a ring of equal width around it, and each segment an arc whose angle is its share of the top. Children lie
/// inside their parent's angle, as in the icicle they lie inside its width. Angles run clockwise from the top; points are relative to
/// the center.
/// </summary>
public static class SunburstGeometry
{
    /// <summary>A full turn, in radians.</summary>
    public const double Turn = 2 * Math.PI;

    /// <summary>
    /// The arcs of a sunburst <paramref name="diameter"/> pixels across, in level order. A segment's length is measured along the
    /// middle of its ring, so the same segment groups sooner near the center than further out (<see cref="ChartSegments"/>).
    /// </summary>
    public static IReadOnlyList<SunburstArc> Arcs(IReadOnlyList<IcicleSegmentDto> segments, long total, double diameter)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (total <= 0 || diameter <= 0 || segments.Count == 0)
        {
            return [];
        }

        var ring = RingWidth(segments, diameter);
        return [.. ChartSegments.Shown(segments, level => Turn * (level + 0.5) * ring / total).Select(s => s.Segment.Level == 0
            ? new SunburstArc(s.Segment, 0, ring, 0, Turn, s.Count)
            : new SunburstArc(s.Segment, s.Segment.Level * ring, (s.Segment.Level + 1) * ring, Turn * s.Segment.Offset / total, Turn * s.Segment.Width / total, s.Count))];
    }

    /// <summary>The width of a ring (and the center disc's radius): the levels share the radius.</summary>
    public static double RingWidth(IReadOnlyList<IcicleSegmentDto> segments, double diameter)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return segments.Count == 0 || diameter <= 0 ? 0 : diameter / 2 / (segments.Max(static s => s.Level) + 1);
    }

    /// <summary>The arc under (<paramref name="x"/>, <paramref name="y"/>), relative to the center, if any.</summary>
    public static SunburstArc? ArcAt(IReadOnlyList<SunburstArc> arcs, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(arcs);
        var radius = Math.Sqrt((x * x) + (y * y));
        var angle = Angle(x, y);
        foreach (var arc in arcs)
        {
            if (radius >= arc.Inner && radius < arc.Outer && (arc.IsCenter || (angle >= arc.Start && angle < arc.Start + arc.Sweep)))
            {
                return arc;
            }
        }

        return null;
    }

    /// <summary>The segment a click selects at a point: a group is no one object or frame, so it selects nothing.</summary>
    public static IcicleSegmentDto? HitTest(IReadOnlyList<SunburstArc> arcs, double x, double y) =>
        ArcAt(arcs, x, y) is { IsGroup: false } arc ? arc.Segment : null;

    /// <summary>The angle of a point relative to the center, clockwise from the top, from 0 up to a full turn.</summary>
    public static double Angle(double x, double y)
    {
        var angle = Math.Atan2(x, -y);
        return angle < 0 ? angle + Turn : angle;
    }

    /// <summary>The point at <paramref name="radius"/> and <paramref name="angle"/> (clockwise from the top), relative to the center.</summary>
    public static (double X, double Y) Point(double radius, double angle) => (radius * Math.Sin(angle), -radius * Math.Cos(angle));
}
