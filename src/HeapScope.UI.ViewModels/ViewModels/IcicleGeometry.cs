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
using HeapScope.Contracts;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// The icicle chart's geometry, independent of the UI toolkit: each bar's rectangle, the bar under a point,
/// and the bars on the path from the top to a selected one.
/// </summary>
public static class IcicleGeometry
{
    /// <summary>A bar narrower than this many pixels is not drawn on its own (and cannot be hit).</summary>
    public const double MinWidth = ChartSegments.MinLength;

    /// <summary>Neighbors with one parent narrower than this many pixels are one "+N smaller" bar: too thin to point at, or to read.</summary>
    public const double GroupBelow = ChartSegments.GroupBelow;

    /// <summary>
    /// The rectangle of each bar, in level order: each bar at least <see cref="MinWidth"/> wide, except that two or more neighbors under
    /// <see cref="GroupBelow"/> with one parent are one group, which hides what lies below them (<see cref="ChartSegments"/>).
    /// </summary>
    public static IReadOnlyList<IcicleBar> Bars(IReadOnlyList<IcicleSegmentDto> segments, long total, double width, double rowHeight)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (total <= 0 || width <= 0)
        {
            return [];
        }

        var scale = width / total;
        return [.. ChartSegments.Shown(segments, _ => scale).Select(s =>
            new IcicleBar(s.Segment, s.Segment.Offset * scale, s.Segment.Level * rowHeight, s.Segment.Width * scale, rowHeight, s.Count))];
    }

    /// <summary>What a bar stands for, in words (<see cref="ChartTexts"/>).</summary>
    public static string Describe(IcicleBar bar, long total, ChartSubject subject = ChartSubject.Retention)
    {
        ArgumentNullException.ThrowIfNull(bar);
        return ChartTexts.Describe(bar.Segment, bar.Count, total, subject, ChartShape.Icicle);
    }

    /// <summary>What a bar this wide can show, or null when it has no room for words: a group's count must not be cut to "+12 ...".</summary>
    public static string? Label(IcicleBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        var full = string.Create(CultureInfo.CurrentCulture, $"{bar.Segment.Name} ({Format.Bytes(bar.Segment.Width)})");
        return bar switch
        {
            { IsGroup: true, Width: >= 110 } => full,
            { IsGroup: true, Width: >= 24 } => string.Create(CultureInfo.CurrentCulture, $"+{bar.Count:N0}"),
            { IsGroup: false, Width: >= 40 } => full,
            _ => null,
        };
    }

    /// <summary>The height that shows every level.</summary>
    public static double Height(IReadOnlyList<IcicleSegmentDto>? segments, double rowHeight) =>
        segments is { Count: > 0 } ? (segments.Max(s => s.Level) + 1) * rowHeight : 0;

    /// <summary>The bar under (<paramref name="x"/>, <paramref name="y"/>), if any.</summary>
    public static IcicleBar? BarAt(IReadOnlyList<IcicleBar> bars, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(bars);
        foreach (var bar in bars)
        {
            if (x >= bar.X && x < bar.X + bar.Width && y >= bar.Y && y < bar.Y + bar.Height)
            {
                return bar;
            }
        }

        return null;
    }

    /// <summary>The segment a click selects at a point: a group is no one object, so it selects nothing.</summary>
    public static IcicleSegmentDto? HitTest(IReadOnlyList<IcicleBar> bars, double x, double y) =>
        BarAt(bars, x, y) is { IsGroup: false } bar ? bar.Segment : null;

    /// <summary>
    /// Whether <paramref name="segment"/> is on the path from the top to <paramref name="selected"/> (itself included): a
    /// bar at a level above it whose span holds its left edge. Children always lie inside their parent, so the spans
    /// nest.
    /// </summary>
    public static bool IsOnPath(IcicleSegmentDto segment, IcicleSegmentDto? selected)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return selected is not null && segment.Level <= selected.Level
            && segment.Offset <= selected.Offset && selected.Offset < segment.Offset + Math.Max(1, segment.Width);
    }
}

/// <summary>A drawn bar of the icicle chart: its segment and its rectangle in pixels.</summary>
public sealed record IcicleBar(IcicleSegmentDto Segment, double X, double Y, double Width, double Height, int Count = 1)
{
    /// <summary>Whether this bar stands for several narrow segments.</summary>
    public bool IsGroup => Count > 1;
}
