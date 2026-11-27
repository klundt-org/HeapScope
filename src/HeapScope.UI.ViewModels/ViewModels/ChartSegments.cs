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
/// Which of a chart's segments are drawn, for either shape: each one at least <see cref="MinLength"/> pixels long, except that two or
/// more neighbors under <see cref="GroupBelow"/> pixels with one parent are one group, which hides what lies below them. How long a
/// segment is depends on the window and, in a sunburst, on its ring, so this runs here and not in the worker. A segment too short to
/// draw hides what lies below it, as a group does.
/// </summary>
internal static class ChartSegments
{
    /// <summary>A segment shorter than this many pixels is not drawn on its own (and cannot be hit).</summary>
    public const double MinLength = 1;

    /// <summary>Neighbors with one parent shorter than this many pixels are one "+N smaller" segment: too thin to point at, or to read.</summary>
    public const double GroupBelow = 6;

    /// <summary>
    /// The segments to draw, in level order, each with how many it stands for (more than 1 for a group).
    /// <paramref name="pixelsPerByte"/> gives a level's length of a byte in pixels.
    /// </summary>
    public static List<(IcicleSegmentDto Segment, int Count)> Shown(IReadOnlyList<IcicleSegmentDto> segments, Func<int, double> pixelsPerByte)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(pixelsPerByte);
        var shown = new List<(IcicleSegmentDto Segment, int Count)>();

        // One walk along each level, in step with the level above and the groups so far: a chart can hold 20,000 segments, and this runs
        // on every draw. Both lists are sorted by offset, and spans at one level do not overlap.
        var groups = new List<IcicleSegmentDto>();
        var row = new List<IcicleSegmentDto>();
        var parents = new List<IcicleSegmentDto>();
        foreach (var level in segments.GroupBy(static s => s.Level).OrderBy(static l => l.Key))
        {
            var scale = pixelsPerByte(level.Key);
            row.Clear();
            var nextGroup = 0;
            foreach (var segment in level.OrderBy(static s => s.Offset))
            {
                if (Find(groups, segment, ref nextGroup) is null)
                {
                    row.Add(segment);
                }
            }

            var run = new List<IcicleSegmentDto>();
            var nextParent = 0;
            var runParent = -1;
            var added = new List<IcicleSegmentDto>();
            foreach (var segment in row)
            {
                var parent = Find(parents, segment, ref nextParent) is null ? -1 : nextParent;
                if (run.Count > 0 && (segment.Width * scale >= GroupBelow || parent != runParent))
                {
                    AddRun(run, shown, added, scale);
                }

                runParent = parent;
                if (segment.Width * scale < GroupBelow)
                {
                    run.Add(segment);
                }
                else
                {
                    shown.Add((segment, 1));
                }
            }

            AddRun(run, shown, added, scale);
            if (added.Count > 0)
            {
                groups.AddRange(added);
                groups.Sort(static (a, b) => a.Offset.CompareTo(b.Offset));
            }

            (parents, row) = (row, parents);
        }

        return shown;
    }

    /// <summary>The span in the sorted <paramref name="spans"/> that holds the segment; segments are asked about in offset order.</summary>
    private static IcicleSegmentDto? Find(List<IcicleSegmentDto> spans, IcicleSegmentDto segment, ref int next)
    {
        while (next < spans.Count && spans[next].Offset + Math.Max(1, spans[next].Width) <= segment.Offset)
        {
            next++;
        }

        return next < spans.Count && spans[next].Offset <= segment.Offset ? spans[next] : null;
    }

    private static void AddRun(List<IcicleSegmentDto> run, List<(IcicleSegmentDto Segment, int Count)> shown, List<IcicleSegmentDto> grouped, double scale)
    {
        if (run.Count == 1 && run[0].Width * scale >= MinLength)
        {
            shown.Add((run[0], 1));
        }
        else if (run.Count == 1)
        {
            // Too short to draw, and so is what lies beyond it: in a sunburst a byte is longer further out, so its children could
            // otherwise be drawn over an empty ring, with no parent under them.
            grouped.Add(run[0]);
        }
        else if (run.Count > 1)
        {
            var first = run[0];
            var last = run[^1];
            var group = new IcicleSegmentDto(
                first.Level,
                first.Offset,
                last.Offset + last.Width - first.Offset,
                string.Create(CultureInfo.InvariantCulture, $"group:{first.Level}:{first.Offset}"),
                string.Create(CultureInfo.CurrentCulture, $"+{run.Count:N0} smaller"),
                0);
            grouped.Add(group);
            shown.Add((group, run.Count));
        }

        run.Clear();
    }
}
