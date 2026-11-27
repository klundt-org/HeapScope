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

/// <summary>What a chart's segment stands for, in words, for both shapes and both subjects: most segments are too small to carry a label.</summary>
public static class ChartTexts
{
    /// <summary>Where a retention group's members are, whatever the tree's grouping.</summary>
    private const string TreeHasThem = "The tree below has them (expand a group or a \"more\" row if they are not shown).";

    /// <summary>Where an allocation group's members are.</summary>
    private const string AllocationTreeHasThem = "The tree below has them (expand a \"more\" row if they are not shown).";

    /// <summary>The words for a segment that stands for <paramref name="count"/> segments (more than 1 for a group) of a chart of <paramref name="total"/> bytes.</summary>
    public static string Describe(IcicleSegmentDto segment, int count, long total, ChartSubject subject = ChartSubject.Retention, ChartShape shape = ChartShape.Icicle)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var share = total > 0 ? (double)segment.Width / total : 0;
        if (subject == ChartSubject.Allocations)
        {
            return count <= 1
                ? string.Create(CultureInfo.CurrentCulture, $"{segment.Name}\nAbout {Format.Bytes(segment.Width)} allocated through it, {share:P1} of this chart")
                : string.Create(CultureInfo.CurrentCulture, $"{segment.Name}\nTogether about {Format.Bytes(segment.Width)}, {share:P1} of this chart\n{AllocationTreeHasThem}");
        }

        if (count <= 1)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{segment.Name}\nKeeps {Format.Bytes(segment.Width)} alive, {share:P1} of this chart");
        }

        // Level 1 sits next to the chart's top, the focus itself: a double-click there shows the same chart, so the tree
        // below is where they are. Further out, the segment next to them is their parent, and focusing it puts them in the tree. Under
        // Group children by Type or Namespace the tree lists groups, and past 100 rows a "more" row: the text holds for both.
        // A group's width adds up its members' retained sizes; what they keep alive together is counted in the parent.
        var parent = shape == ChartShape.Sunburst ? "the arc inside them" : "the bar above them";
        var where = segment.Level <= 1
            ? TreeHasThem
            : "Double-click " + parent + " to focus on it; " + char.ToLowerInvariant(TreeHasThem[0]) + TreeHasThem[1..];
        return string.Create(CultureInfo.CurrentCulture, $"{segment.Name}\nTogether keep at least {Format.Bytes(segment.Width)} alive, {share:P1} of this chart\n{where}");
    }
}
