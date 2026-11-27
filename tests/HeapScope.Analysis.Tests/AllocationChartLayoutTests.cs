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

using HeapScope.Analysis.Allocations;
using HeapScope.Core.Allocations;

namespace HeapScope.Analysis.Tests;

/// <summary>The chart of an interval's call tree and back traces (ADR-046): laid out like the icicle, keyed like the tree's rows.</summary>
public sealed class AllocationChartLayoutTests
{
    private const int Main = 0;
    private const int Handle = 1;
    private const int Grow = 2;
    private const int Log = 3;
    private const int Bytes = 0;
    private const int Text = 1;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // As in AllocationProfileTests: Main > Handle > Grow allocates 200 bytes and Main > Handle > Log 50 in the first second;
    // Main > Log 30 bytes of String and Handle itself 20 bytes in the second.
    private static readonly AllocationTrace s_trace = new(
        ["Main", "Handle", "Grow", "Log"],
        ["System.Byte[]", "System.String"],
        stackFrames: [Main, Handle, Grow, Log, Log],
        stackParents: [-1, 0, 1, 1, 0],
        sampleTimes: [0, 10, 20, 1500, 1600],
        sampleStacks: [2, 2, 3, 4, 1],
        sampleTypes: [Bytes, Bytes, Text, Text, Bytes],
        sampleBytes: [100, 100, 50, 30, 20],
        sampleKinds: [AllocationKind.Small, AllocationKind.Large, AllocationKind.Small, AllocationKind.Small, AllocationKind.Pinned],
        new AllocationTraceInfo(42, "LeakyApi", DateTime.UnixEpoch, 2000, 0, 0));

    private static IEnumerable<(int Level, long Offset, long Width, string Key)> Shape(AllocationChart chart) =>
        chart.Segments.Select(static s => (s.Level, s.Offset, s.Width, s.Key));

    [Fact]
    public void The_call_tree_starts_at_the_outermost_frames_and_its_children_sit_inside_them()
    {
        var chart = AllocationChartLayout.Build(s_trace, AllocationFilter.All, AllocationTreeKind.CallTree, 6, 0.001, Ct);

        (chart.TotalBytes, chart.TotalSamples).ShouldBe((300L, 5));
        Shape(chart).ShouldBe(
        [
            (0, 0L, 300L, "root"),
            (1, 0L, 300L, "n:0"),
            (2, 0L, 270L, "n:1"),
            (2, 270L, 30L, "n:4"),
            (3, 0L, 200L, "n:2"),
            (3, 200L, 50L, "n:3"),
        ]);
        chart.Segments.Select(static s => s.Frame).ShouldBe([-1, Main, Handle, Log, Grow, Log]);
        FitInsideTheirParents(chart);
    }

    [Fact]
    public void Back_traces_start_at_the_allocating_frames_and_go_out_to_their_callers()
    {
        var chart = AllocationChartLayout.Build(s_trace, AllocationFilter.All, AllocationTreeKind.BackTraces, 6, 0.001, Ct);

        Shape(chart).ShouldBe(
        [
            (0, 0L, 300L, "root"),
            (1, 0L, 200L, "b:2"),
            (1, 200L, 80L, "b:3"),
            (1, 280L, 20L, "b:1"),
            (2, 0L, 200L, "b:2/1"),
            (2, 200L, 50L, "b:3/1"),
            (2, 250L, 30L, "b:3/0"),
            (2, 280L, 20L, "b:1/0"),
            (3, 0L, 200L, "b:2/1/0"),
            (3, 200L, 50L, "b:3/1/0"),
        ]);
        FitInsideTheirParents(chart);
    }

    /// <summary>A segment finds its row: every key the chart draws is a row of the tree with its parents open, with the same bytes.</summary>
    [Theory]
    [InlineData(AllocationTreeKind.CallTree)]
    [InlineData(AllocationTreeKind.BackTraces)]
    public void Each_segment_is_a_row_of_the_tree_with_the_same_bytes(AllocationTreeKind kind)
    {
        var chart = AllocationChartLayout.Build(s_trace, AllocationFilter.All, kind, 6, 0.001, Ct);
        var open = chart.Segments.Select(static s => s.Key).ToHashSet();
        var rows = AllocationTreeRows.Build(s_trace, AllocationFilter.All, kind, open, 100, Ct).Rows.ToDictionary(static r => r.Key);

        foreach (var segment in chart.Segments.Skip(1))
        {
            rows.ShouldContainKey(segment.Key);
            rows[segment.Key].Total.ShouldBe(segment.Width, segment.Key);
            rows[segment.Key].Depth.ShouldBe(segment.Level - 1, segment.Key);
        }
    }

    [Fact]
    public void Segments_under_the_share_and_levels_past_the_limit_are_left_out()
    {
        // 20 percent of 300 bytes is 60: the two Log calls and Handle's own 20 bytes are too narrow, with what lies below them.
        var narrow = AllocationChartLayout.Build(s_trace, AllocationFilter.All, AllocationTreeKind.CallTree, 6, 0.2, Ct);
        narrow.Segments.Select(static s => s.Key).ShouldBe(["root", "n:0", "n:1", "n:2"]);
        var back = AllocationChartLayout.Build(s_trace, AllocationFilter.All, AllocationTreeKind.BackTraces, 6, 0.2, Ct);
        back.Segments.Select(static s => s.Key).ShouldBe(["root", "b:2", "b:3", "b:2/1", "b:2/1/0"]);

        var shallow = AllocationChartLayout.Build(s_trace, AllocationFilter.All, AllocationTreeKind.CallTree, 2, 0.001, Ct);
        shallow.Segments.Max(static s => s.Level).ShouldBe(2);
        Should.Throw<ArgumentOutOfRangeException>(() => AllocationChartLayout.Build(s_trace, AllocationFilter.All, AllocationTreeKind.CallTree, 0, 0.001, Ct));
        Should.Throw<ArgumentOutOfRangeException>(() => AllocationChartLayout.Build(s_trace, AllocationFilter.All, AllocationTreeKind.CallTree, 6, 1.5, Ct));
    }

    [Fact]
    public void What_the_share_leaves_out_is_counted_with_its_bytes()
    {
        // 20 percent is 60 bytes: under Main the Log call (30) and under Handle the Log call (50) are left out, 80 bytes.
        var calls = AllocationChartLayout.Build(s_trace, AllocationFilter.All, AllocationTreeKind.CallTree, 6, 0.2, Ct);
        (calls.LeftOut, calls.LeftOutBytes, calls.Truncated).ShouldBe((2, 80L, false));

        // Handle's own 20 at the first level, then Log's callers Handle (50) and Main (30).
        var back = AllocationChartLayout.Build(s_trace, AllocationFilter.All, AllocationTreeKind.BackTraces, 6, 0.2, Ct);
        (back.LeftOut, back.LeftOutBytes, back.Truncated).ShouldBe((3, 100L, false));

        AllocationChartLayout.Build(s_trace, AllocationFilter.All, AllocationTreeKind.CallTree, 6, 0.001, Ct).LeftOut.ShouldBe(0);
    }

    /// <summary>Main > Walk > Walk > Alloc and Main > Walk > Alloc: recursion, and one frame reached by two nodes.</summary>
    [Fact]
    public void Recursion_keeps_each_call_and_every_level_fits_inside_the_one_before()
    {
        var trace = new AllocationTrace(
            ["Main", "Walk", "Alloc"],
            ["T"],
            stackFrames: [0, 1, 1, 2, 2],
            stackParents: [-1, 0, 1, 2, 1],
            sampleTimes: [0, 1],
            sampleStacks: [3, 4],
            sampleTypes: [0, 0],
            sampleBytes: [64, 36],
            sampleKinds: [AllocationKind.Small, AllocationKind.Small],
            new AllocationTraceInfo(1, null, DateTime.UnixEpoch, 10, 0, 0));

        var back = AllocationChartLayout.Build(trace, AllocationFilter.All, AllocationTreeKind.BackTraces, 6, 0.001, Ct);
        Shape(back).ShouldBe(
        [
            (0, 0L, 100L, "root"),
            (1, 0L, 100L, "b:2"),
            (2, 0L, 100L, "b:2/1"),
            (3, 0L, 64L, "b:2/1/1"),
            (3, 64L, 36L, "b:2/1/0"),
            (4, 0L, 64L, "b:2/1/1/0"),
        ]);
        FitInsideTheirParents(back);
        var calls = AllocationChartLayout.Build(trace, AllocationFilter.All, AllocationTreeKind.CallTree, 6, 0.001, Ct);
        Shape(calls).ShouldBe([(0, 0L, 100L, "root"), (1, 0L, 100L, "n:0"), (2, 0L, 100L, "n:1"), (3, 0L, 64L, "n:2"), (3, 64L, 36L, "n:4"), (4, 0L, 64L, "n:3")]);
        FitInsideTheirParents(calls);
    }

    /// <summary>Two nodes of one frame under one parent (a trace may hold them): their callers merge into one position, bytes summed.</summary>
    [Fact]
    public void Two_nodes_of_one_frame_merge_their_callers_bytes()
    {
        var trace = new AllocationTrace(
            ["Main", "Grow"],
            ["T"],
            stackFrames: [0, 1, 1],
            stackParents: [-1, 0, 0],
            sampleTimes: [0, 1],
            sampleStacks: [1, 2],
            sampleTypes: [0, 0],
            sampleBytes: [40, 60],
            sampleKinds: [AllocationKind.Small, AllocationKind.Small],
            new AllocationTraceInfo(1, null, DateTime.UnixEpoch, 10, 0, 0));

        var back = AllocationChartLayout.Build(trace, AllocationFilter.All, AllocationTreeKind.BackTraces, 6, 0.001, Ct);
        Shape(back).ShouldBe([(0, 0L, 100L, "root"), (1, 0L, 100L, "b:1"), (2, 0L, 100L, "b:1/0")]);
        var rows = AllocationTreeRows.Build(trace, AllocationFilter.All, AllocationTreeKind.BackTraces, new HashSet<string> { "b:1" }, 100, Ct).Rows;
        rows.Single(r => r.Key == "b:1/0").Total.ShouldBe(100, "the tree's row has the chart's bytes");
    }

    [Fact]
    public void A_chart_past_the_segment_cap_is_cut_and_says_so()
    {
        var count = AllocationChartLayout.MaxSegments + 10;
        var trace = new AllocationTrace(
            [.. Enumerable.Range(0, count).Select(i => "M" + i)],
            ["T"],
            stackFrames: [.. Enumerable.Range(0, count)],
            stackParents: [.. Enumerable.Repeat(-1, count)],
            sampleTimes: [.. Enumerable.Range(0, count).Select(i => (double)i)],
            sampleStacks: [.. Enumerable.Range(0, count)],
            sampleTypes: new int[count],
            sampleBytes: [.. Enumerable.Repeat(1L, count)],
            sampleKinds: new AllocationKind[count],
            new AllocationTraceInfo(1, null, DateTime.UnixEpoch, count, 0, 0));

        var chart = AllocationChartLayout.Build(trace, AllocationFilter.All, AllocationTreeKind.CallTree, 6, 0, Ct);

        chart.Truncated.ShouldBeTrue();
        chart.Segments.Count.ShouldBe(AllocationChartLayout.MaxSegments);
    }

    [Fact]
    public void A_type_and_an_interval_narrow_the_chart_and_an_empty_one_is_its_top_alone()
    {
        var filter = new AllocationFilter(1000, double.PositiveInfinity, Text);
        var text = AllocationChartLayout.Build(s_trace, filter, AllocationTreeKind.CallTree, 6, 0.001, Ct);
        Shape(text).ShouldBe([(0, 0L, 30L, "root"), (1, 0L, 30L, "n:0"), (2, 0L, 30L, "n:4")]);
        var tree = AllocationTreeRows.Build(s_trace, filter, AllocationTreeKind.CallTree, new HashSet<string>(), 100, Ct);
        (text.TotalBytes, text.TotalSamples).ShouldBe((tree.TotalBytes, tree.TotalSamples), "the chart's top is the tree's total");

        var none = AllocationChartLayout.Build(s_trace, new AllocationFilter(500, 900), AllocationTreeKind.BackTraces, 6, 0.001, Ct);
        Shape(none).ShouldBe([(0, 0L, 0L, "root")]);
        none.TotalSamples.ShouldBe(0);
    }

    private static void FitInsideTheirParents(AllocationChart chart)
    {
        foreach (var segment in chart.Segments.Where(static s => s.Level > 0))
        {
            var parent = chart.Segments.Single(p => p.Level == segment.Level - 1 && p.Offset <= segment.Offset && segment.Offset < p.Offset + Math.Max(1, p.Width));
            (segment.Offset + segment.Width).ShouldBeLessThanOrEqualTo(parent.Offset + parent.Width, segment.Key);
        }
    }
}
