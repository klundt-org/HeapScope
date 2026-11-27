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

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>The sunburst's geometry, drawn from the icicle's segments (ADR-046).</summary>
public sealed class SunburstGeometryTests
{
    private const double Turn = SunburstGeometry.Turn;

    private static readonly IcicleSegmentDto[] s_segments =
    [
        new(0, 0, 1000, string.Empty, "All reachable objects", 0),
        new(1, 0, 750, "o:10", "App.Cache", 0x10),
        new(1, 750, 250, "o:20", "App.Other", 0x20),
        new(2, 0, 500, "o:30", "App.Entry", 0x30),
        new(2, 750, 100, "o:40", "App.Leaf", 0x40),
    ];

    [Fact]
    public void The_top_is_the_center_disc_and_each_level_a_ring_with_angles_by_share()
    {
        var arcs = SunburstGeometry.Arcs(s_segments, 1000, diameter: 300);

        SunburstGeometry.RingWidth(s_segments, 300).ShouldBe(50);
        arcs.Select(a => (a.Segment.Key, a.Inner, a.Outer, Math.Round(a.Start, 9), Math.Round(a.Sweep, 9))).ShouldBe(
        [
            (string.Empty, 0.0, 50.0, 0.0, Math.Round(Turn, 9)),
            ("o:10", 50.0, 100.0, 0.0, Math.Round(Turn * 0.75, 9)),
            ("o:20", 50.0, 100.0, Math.Round(Turn * 0.75, 9), Math.Round(Turn * 0.25, 9)),
            ("o:30", 100.0, 150.0, 0.0, Math.Round(Turn * 0.5, 9)),
            ("o:40", 100.0, 150.0, Math.Round(Turn * 0.75, 9), Math.Round(Turn * 0.1, 9)),
        ]);
        arcs[0].IsCenter.ShouldBeTrue();
        arcs.Skip(1).ShouldAllBe(a => !a.IsCenter);
    }

    [Fact]
    public void Children_lie_inside_their_parents_angle()
    {
        var arcs = SunburstGeometry.Arcs(s_segments, 1000, diameter: 300);

        foreach (var child in arcs.Where(a => a.Segment.Level >= 2))
        {
            var parent = arcs.Single(a => a.Segment.Level == child.Segment.Level - 1 && IcicleGeometry.IsOnPath(a.Segment, child.Segment));
            child.Start.ShouldBeGreaterThanOrEqualTo(parent.Start);
            (child.Start + child.Sweep).ShouldBeLessThanOrEqualTo(parent.Start + parent.Sweep + 1e-9);
        }
    }

    [Fact]
    public void A_point_finds_the_center_a_ring_a_gap_or_nothing()
    {
        var arcs = SunburstGeometry.Arcs(s_segments, 1000, diameter: 300);

        SunburstGeometry.HitTest(arcs, 0, 0)!.Key.ShouldBe(string.Empty, "the center is the top");
        SunburstGeometry.HitTest(arcs, 10, -60)!.Key.ShouldBe("o:10", "just right of 12 o'clock, in the first ring");
        SunburstGeometry.HitTest(arcs, -75, -5)!.Key.ShouldBe("o:20", "just left of 9 o'clock is past three quarters of a turn");
        SunburstGeometry.HitTest(arcs, 120, 10)!.Key.ShouldBe("o:30", "3 o'clock, second ring");
        SunburstGeometry.HitTest(arcs, -120, 20).ShouldBeNull("the second ring's gap at the parent's own bytes");
        SunburstGeometry.HitTest(arcs, 200, 0).ShouldBeNull("outside the chart");
        SunburstGeometry.Angle(0, -1).ShouldBe(0);
        SunburstGeometry.Angle(1, 0).ShouldBe(Turn / 4, 1e-12);
        SunburstGeometry.Angle(0, 1).ShouldBe(Turn / 2, 1e-12);
        var (x, y) = SunburstGeometry.Point(10, Turn / 4);
        x.ShouldBe(10, 1e-9);
        y.ShouldBe(0, 1e-9);
    }

    [Fact]
    public void Narrow_neighbors_become_one_arc_and_rings_further_out_group_later()
    {
        IcicleSegmentDto[] segments =
        [
            new(0, 0, 1000, string.Empty, "All", 0),
            new(1, 0, 900, "o:1", "App.Wide", 1),
            new(1, 900, 5, "o:2", "App.A", 2),
            new(1, 905, 5, "o:3", "App.B", 3),
            new(1, 910, 5, "o:4", "App.C", 4),
            new(2, 0, 5, "o:5", "App.D", 5),
            new(2, 5, 5, "o:6", "App.E", 6),
            new(2, 910, 5, "o:7", "App.Under", 7),
        ];

        // Rings of 40 pixels: 5 bytes of 1000 is 1.9 pixels mid first ring and 3.1 mid second, both under 6.
        var arcs = SunburstGeometry.Arcs(segments, 1000, diameter: 240);
        var group = arcs.Single(a => a.IsGroup && a.Segment.Level == 1);
        (group.Count, group.Segment.Name, group.Segment.Offset, group.Segment.Width).ShouldBe((3, "+3 smaller", 900L, 15L));
        arcs.Select(a => a.Segment.Key).ShouldNotContain("o:7", "what lies beyond a group is hidden with it");
        SunburstGeometry.HitTest(arcs, SunburstGeometry.Point(60, group.Start + (group.Sweep / 2)).X, SunburstGeometry.Point(60, group.Start + (group.Sweep / 2)).Y)
            .ShouldBeNull("a group is no one object");

        // Wide enough, the outer pair stands alone while the inner three still group: 5 bytes is 5.7 pixels mid first ring, 9.4 mid second.
        var wide = SunburstGeometry.Arcs(segments, 1000, diameter: 720);
        wide.Single(a => a.IsGroup).Segment.Level.ShouldBe(1);
        wide.Where(a => a.Segment.Level == 2).Select(a => a.Segment.Key).ShouldBe(["o:5", "o:6"]);
    }

    /// <summary>A byte is longer further out, so a parent too short to draw must hide its children, or they float over an empty ring.</summary>
    [Fact]
    public void What_lies_beyond_a_segment_too_short_to_draw_is_hidden_with_it()
    {
        IcicleSegmentDto[] segments =
        [
            new(0, 0, 1000, string.Empty, "All", 0),
            new(1, 0, 997, "o:1", "App.Wide", 1),
            new(1, 997, 3, "o:2", "App.Thin", 2),
            new(2, 997, 3, "o:3", "App.ThinChild", 3),
            new(3, 997, 3, "o:4", "App.ThinGrandchild", 4),
        ];

        // Rings of 30 pixels: 3 bytes of 1000 is 0.85 pixels mid first ring, 1.4 mid second and 2 mid third.
        var arcs = SunburstGeometry.Arcs(segments, 1000, diameter: 240);

        arcs.Select(a => a.Segment.Key).ShouldBe([string.Empty, "o:1"]);
        IcicleGeometry.Bars(segments, 1000, width: 200, rowHeight: 20).Select(b => b.Segment.Key).ShouldBe([string.Empty, "o:1"], "the icicle hides them the same way");
    }

    [Fact]
    public void An_empty_chart_has_no_arcs()
    {
        SunburstGeometry.Arcs([], 1000, 300).ShouldBeEmpty();
        SunburstGeometry.Arcs(s_segments, 0, 300).ShouldBeEmpty();
        SunburstGeometry.Arcs(s_segments, 1000, 0).ShouldBeEmpty();
        SunburstGeometry.RingWidth([], 300).ShouldBe(0);
        SunburstGeometry.HitTest([], 0, 0).ShouldBeNull();
    }

    [Fact]
    public void A_segment_is_put_in_words_for_its_shape_and_subject()
    {
        var group = new IcicleSegmentDto(2, 0, 10, "g", "+2 smaller", 0);
        ChartTexts.Describe(group, 2, 100, ChartSubject.Retention, ChartShape.Sunburst)
            .ShouldEndWith("\nDouble-click the arc inside them to focus on it; the tree below has them (expand a group or a \"more\" row if they are not shown).");
        ChartTexts.Describe(group, 2, 100, ChartSubject.Retention, ChartShape.Icicle).ShouldContain("Double-click the bar above them");

        var frame = new IcicleSegmentDto(1, 0, 256 * 1024, "n:4", "App.Program.Main", 0);
        var text = ChartTexts.Describe(frame, 1, 1024 * 1024, ChartSubject.Allocations);
        text.ShouldStartWith("App.Program.Main\nAbout ");
        text.ShouldContain("allocated through it");
        text.ShouldNotContain("alive");
        ChartTexts.Describe(group, 2, 100, ChartSubject.Allocations).ShouldEndWith("\nThe tree below has them (expand a \"more\" row if they are not shown).");
    }
}
