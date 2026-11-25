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

using HeapScope.Core.Allocations;
using HeapScope.Loaders.Trace;

namespace HeapScope.Worker.Tests;

/// <summary>What a live trace keeps (sprint 4.3): samples added up, stacks folded and named, all of it bounded.</summary>
public sealed class LiveAllocationsTests
{
    private static readonly AllocationTraceInfo s_info = new(1, "app", DateTime.UnixEpoch, 0, 0, 0);

    private readonly LiveMethodNames _names = new();

    [Fact]
    public void Samples_are_added_up_by_stretch_type_stack_and_kind()
    {
        var live = Live();
        _names.Add(0x1000, 0x100, "App.Main()");
        _names.Add(0x2000, 0x100, "App.Work()");
        live.Add(10, "A", 100, AllocationKind.Small, [0x2010, 0x1010]);
        live.Add(20, "A", 200, AllocationKind.Small, [0x2020, 0x1030]);
        live.Add(30, "A", 300, AllocationKind.Large, [0x2020, 0x1030]);
        live.Add(1500, "B", 400, AllocationKind.Pinned, []);

        var trace = live.View(s_info);

        trace.SampleCount.ShouldBe(3, "two calls of one method are one stack");
        Enumerable.Range(0, 3).Select(i => (trace.SampleTimes[i], trace.Types[trace.SampleTypes[i]], trace.SampleBytes[i], trace.SampleKinds[i], trace.CountOf(i), Stack(trace, i)))
            .ShouldBe([
                (0.0, "A", 300L, AllocationKind.Small, 2, "App.Work() < App.Main()"),
                (0.0, "A", 300L, AllocationKind.Large, 1, "App.Work() < App.Main()"),
                (1000.0, "B", 400L, AllocationKind.Pinned, 1, AllocationTrace.NoStackFrame),
            ]);
        (live.Samples, live.Bytes, live.WithoutStack, live.Unnamed()).ShouldBe((4, 1000, 1, 0));
        live.View(s_info).ShouldBeSameAs(trace, "nothing was added");
    }

    [Fact]
    public void Names_that_arrive_late_name_the_stacks_already_folded()
    {
        var live = Live();
        _names.Add(0x1000, 0x100, "App.Main()");
        live.Add(1, "A", 10, AllocationKind.Small, [0x2010, 0x1010]);
        live.Add(2, "A", 10, AllocationKind.Small, [0x2020, 0x1010]);
        var before = live.View(s_info);
        before.SampleCount.ShouldBe(2, "two unnamed addresses may be two methods");
        Stack(before, 0).ShouldBe(AllocationTrace.UnresolvedFrame(null) + " < App.Main()");
        live.Unnamed().ShouldBe(2);

        _names.Add(0x2000, 0x100, "App.Work()");

        var after = live.View(s_info);
        after.SampleCount.ShouldBe(1, "they are one");
        (Stack(after, 0), after.CountOf(0), after.SampleBytes[0]).ShouldBe(("App.Work() < App.Main()", 2, 20L));
        live.Unnamed().ShouldBe(0);
        live.Add(3, "A", 10, AllocationKind.Small, [0x2030, 0x1010]);
        live.View(s_info).CountOf(0).ShouldBe(3, "later samples join them");
    }

    [Fact]
    public void A_method_loaded_where_another_was_takes_its_place_from_then_on()
    {
        var live = Live();
        _names.Add(0x1000, 0x100, "Gone()");
        live.Add(1, "A", 10, AllocationKind.Small, [0x1010]);
        _names.Add(0x1000, 0x80, "New()");
        live.Add(2, "A", 10, AllocationKind.Small, [0x1010]);

        var trace = live.View(s_info);

        Enumerable.Range(0, 2).Select(i => Stack(trace, i)).ShouldBe(["Gone()", "New()"]);
    }

    [Fact]
    public void Stretches_double_when_there_are_too_many_and_nothing_is_lost()
    {
        var live = Live(new LiveAllocationLimits(MaxBuckets: 4));
        for (var second = 0; second < 10; second++)
        {
            live.Add(second * 1000, "A", 10, AllocationKind.Small, []);
        }

        // A sample can arrive after a later one.
        live.Add(500, "A", 10, AllocationKind.Small, []);
        var trace = live.View(s_info);

        live.BucketMs.ShouldBe(4000);
        trace.SampleTimes.ShouldBe([0, 4000, 8000]);
        Enumerable.Range(0, 3).Select(trace.CountOf).ShouldBe([5, 4, 2]);
        trace.SampleBytes.Sum().ShouldBe(110);
    }

    [Fact]
    public void Too_many_entries_make_stretches_longer_and_then_give_up_the_stacks()
    {
        var live = Live(new LiveAllocationLimits(MaxEntries: 10));
        for (var i = 0; i < 12; i++)
        {
            live.Add(i * 1000, "A", 10, AllocationKind.Small, [0x1000]);
        }

        (live.Entries <= 10, live.BucketMs, live.PastLimits).ShouldBe((true, 2000.0, 0L));
        for (var i = 0; i < 50; i++)
        {
            live.Add(100, "A", 10, AllocationKind.Small, [(ulong)(0x5000 + i)]);
        }

        var trace = live.View(s_info);
        live.Entries.ShouldBeLessThanOrEqualTo(10);
        Stack(trace, 0).ShouldBe(LiveAllocations.OtherStacks);
        (trace.SampleBytes.Sum(), Enumerable.Range(0, trace.SampleCount).Sum(trace.CountOf), live.PastLimits).ShouldBe((620L, 62, 62L));
    }

    [Fact]
    public void Types_and_stacks_past_the_limits_are_added_up_and_counted()
    {
        var live = Live(new LiveAllocationLimits(MaxTypes: 2, MaxStackNodes: 2));
        live.Add(1, "A", 1, AllocationKind.Small, [0x1000]);
        live.Add(2, "B", 2, AllocationKind.Small, [0x2000]);
        live.Add(3, "C", 4, AllocationKind.Small, [0x1000]);
        live.Add(4, "D", 8, AllocationKind.Small, [0x3000]);
        live.Add(5, "A", 16, AllocationKind.Small, [0x1000]);

        var trace = live.View(s_info);

        trace.Types.ShouldBe(["A", "B", LiveAllocations.OtherTypes]);
        live.PastLimits.ShouldBe(2);
        trace.SampleBytes.Sum().ShouldBe(31);
        Enumerable.Range(0, trace.SampleCount).Select(i => Stack(trace, i)).ShouldContain(LiveAllocations.OtherStacks);
    }

    [Fact]
    public void Names_are_found_by_range_and_bounded()
    {
        _names.Add(0x2000, 0x10, "B");
        _names.Add(0x1000, 0x10, "A");
        _names.Add(0x1000, 0x10, "A");
        _names.Add(0, 0x10, "nowhere");
        _names.Add(0x3000, 0, "empty");

        (_names.Count, _names.Version, _names.Find(0x1000), _names.Find(0x100f), _names.Find(0x1010), _names.Find(0xfff), _names.Find(0x2001))
            .ShouldBe((2, 2, "A", "A", null, null, "B"));
        _names.Add(0x9000, 0x10, new string('x', (int)(LiveMethodNames.MaxTextBytes / 2)));
        (_names.Count, _names.LeftOut).ShouldBe((2, 1));
    }

    private LiveAllocations Live(LiveAllocationLimits? limits = null) => new(_names, limits ?? LiveAllocationLimits.Default);

    private static string Stack(AllocationTrace trace, int sample)
    {
        var frames = new List<string>();
        for (var at = trace.SampleStacks[sample]; at >= 0; at = trace.StackParents[at])
        {
            frames.Add(trace.Frames[trace.StackFrames[at]]);
        }

        return string.Join(" < ", frames);
    }
}
