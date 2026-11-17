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

namespace HeapScope.Benchmarks.Tests;

/// <summary>Sprint 3.12, task 3: how a UI stall is split into its causes, and how a CPU sample's stack is read.</summary>
public sealed class UiStallAttributionTests
{
    private static StallFacts Facts(double duration, double running, double ready = 0, double gc = 0, double jit = 0, params (StallCause Cause, int Count)[] samples) =>
        new(duration, running, ready, gc, jit, samples.ToDictionary(s => s.Cause, s => s.Count));

    [Fact]
    public void Ready_time_is_waiting_for_the_cpu_and_the_rest_of_the_stall_is_blocked()
    {
        var split = UiStallAttribution.Split(Facts(200, running: 50, ready: 120, samples: (StallCause.Layout, 10)));

        split[StallCause.WaitingForCpu].ShouldBe(120);
        split[StallCause.Layout].ShouldBe(50);
        split[StallCause.Blocked].ShouldBe(30);
        UiStallAttribution.MainCause(split).ShouldBe(StallCause.WaitingForCpu);
    }

    [Fact]
    public void Measured_gc_and_jit_come_first_and_the_samples_split_what_is_left_of_the_running_time()
    {
        var split = UiStallAttribution.Split(Facts(300, running: 280, gc: 60, jit: 40, samples: [(StallCause.StylesAndTemplates, 30), (StallCause.Layout, 10), (StallCause.Jit, 99)]));

        split[StallCause.GarbageCollection].ShouldBe(60);
        split[StallCause.Jit].ShouldBe(40);
        split[StallCause.StylesAndTemplates].ShouldBe(135);
        split[StallCause.Layout].ShouldBe(45);
        split[StallCause.Blocked].ShouldBe(20);
        split.Values.Sum().ShouldBe(300);
    }

    [Fact]
    public void Times_beyond_the_stall_are_capped_so_the_parts_never_exceed_it()
    {
        var split = UiStallAttribution.Split(Facts(100, running: 150, ready: 30, gc: 500, jit: 500));

        split.Values.Sum().ShouldBe(100);
        split[StallCause.WaitingForCpu].ShouldBe(30);
        split[StallCause.GarbageCollection].ShouldBe(70);
    }

    /// <summary>
    /// A stall of the recorded smoke run (`wpr-now-1`, 2026-11-12 23:49:29.898, a 316 ms lag as the Snapshots page first
    /// showed a type grid): its numbers as the trace gave them, and the split `budgets.md` reports.
    /// </summary>
    [Fact]
    public void A_recorded_first_show_is_mostly_styles_and_templates_with_a_gc_pause()
    {
        var split = UiStallAttribution.Split(new StallFacts(
            316,
            RunningMs: 287.7,
            ReadyMs: 0.3,
            GcPauseMs: 93.4,
            JitMs: 13.2,
            new Dictionary<StallCause, int> { [StallCause.StylesAndTemplates] = 202, [StallCause.Other] = 19, [StallCause.Jit] = 13, [StallCause.Layout] = 38, [StallCause.Rendering] = 15 }));

        UiStallAttribution.MainCause(split).ShouldBe(StallCause.StylesAndTemplates);
        split[StallCause.StylesAndTemplates].ShouldBe(133.5, 0.1);
        split[StallCause.GarbageCollection].ShouldBe(93.4, 0.1);
        split[StallCause.Layout].ShouldBe(25.1, 0.1);
        split[StallCause.Jit].ShouldBe(13.2, 0.1);
        split[StallCause.Other].ShouldBe(12.6, 0.1);
        split[StallCause.Rendering].ShouldBe(9.9, 0.1);
        split[StallCause.Blocked].ShouldBe(28.0, 0.1);
        split[StallCause.WaitingForCpu].ShouldBe(0.3, 0.1);
    }

    [Theory]
    [InlineData("clrjit!CILJit::compileMethod|coreclr!?|Avalonia.Base!Avalonia.Layout.LayoutManager.ExecuteLayoutPass()", "Jit")]
    [InlineData("coreclr!WKS::gc_heap::garbage_collect|Avalonia.Base!Avalonia.Styling.Styles.TryAttach()", "GarbageCollection")]
    [InlineData("System.Private.CoreLib!System.String.Concat()|Avalonia.Base!Avalonia.Styling.Styles.TryAttach()|Avalonia.Base!Avalonia.Layout.LayoutManager.ExecuteLayoutPass()", "StylesAndTemplates")]
    [InlineData("Avalonia.Controls!Avalonia.Controls.Primitives.TemplatedControl.ApplyTemplate()|Avalonia.Base!Avalonia.Layout.Layoutable.Measure()", "StylesAndTemplates")]
    [InlineData("Avalonia.Controls!Avalonia.Controls.TextBlock.MeasureOverride()|Avalonia.Base!Avalonia.Layout.LayoutManager.ExecuteLayoutPass()", "Layout")]
    [InlineData("libSkiaSharp!?|Avalonia.Skia!Avalonia.Skia.DrawingContextImpl.DrawGlyphRun()", "Rendering")]
    [InlineData("HeapScope.UI.ViewModels!HeapScope.UI.ViewModels.TypeSummaryViewModel.Refresh()", "Other")]
    public void A_samples_stack_shows_its_cause(string stack, string expected)
    {
        UiStallAttribution.Classify(stack.Split('|')).ShouldBe(Enum.Parse<StallCause>(expected));
    }
}
