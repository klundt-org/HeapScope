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

namespace HeapScope.Benchmarks;

/// <summary>Where a UI stall's time went, by cause (sprint 3.12, task 3).</summary>
internal enum StallCause
{
    /// <summary>The UI thread was ready to run but had no processor: the machine was busy.</summary>
    WaitingForCpu,

    /// <summary>The runtime was paused for a garbage collection.</summary>
    GarbageCollection,

    /// <summary>Methods compiled on the UI thread, or code loaded the first time it ran (stubs, type loading).</summary>
    Jit,

    /// <summary>Styles applied and control templates built (Avalonia's styling and templates).</summary>
    StylesAndTemplates,

    /// <summary>Measuring and arranging (Avalonia's layout pass).</summary>
    Layout,

    /// <summary>Drawing: the render loop on the UI thread.</summary>
    Rendering,

    /// <summary>Anything else the UI thread ran (HeapScope's own code, data grid rows, bindings).</summary>
    Other,

    /// <summary>The UI thread waited for something other than a processor (a lock, I/O, another thread).</summary>
    Blocked,
}

/// <summary>What a trace shows about the UI thread during one stall, in milliseconds (sprint 3.12, task 3).</summary>
/// <param name="DurationMs">The stall as the app's watchdog logged it.</param>
/// <param name="RunningMs">The UI thread on a processor (context switches).</param>
/// <param name="ReadyMs">The UI thread ready but not running (ready-thread to switch-in).</param>
/// <param name="GcPauseMs">The process's runtime suspended for a garbage collection.</param>
/// <param name="JitMs">Methods compiled on the UI thread (JIT events).</param>
/// <param name="Samples">CPU samples of the UI thread by the cause their stack shows.</param>
internal sealed record StallFacts(double DurationMs, double RunningMs, double ReadyMs, double GcPauseMs, double JitMs, IReadOnlyDictionary<StallCause, int> Samples);

/// <summary>
/// Splits a stall into its causes (sprint 3.12, task 3), kept apart from reading the trace so it is tested on a recording's
/// numbers. Ready time is waiting for a processor; the GC pause and JIT time come from the runtime's own events; the rest of
/// the running time is split by what the CPU samples' stacks show; what is left of the stall is blocked.
/// </summary>
internal static class UiStallAttribution
{
    public static IReadOnlyDictionary<StallCause, double> Split(StallFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var result = Enum.GetValues<StallCause>().ToDictionary(c => c, _ => 0.0);
        var duration = Math.Max(facts.DurationMs, 0);
        var ready = Clamp(facts.ReadyMs, duration);
        var gc = Clamp(facts.GcPauseMs, duration - ready);
        var running = Clamp(facts.RunningMs, duration - ready);

        // The GC pause and JIT time are measured, not sampled: they take their share of the running time first. A pause
        // is time the UI thread did not run its own work, whether it was switched out or spun.
        var jit = Clamp(facts.JitMs, Math.Max(running - gc, 0));
        result[StallCause.WaitingForCpu] = ready;
        result[StallCause.GarbageCollection] = gc;
        result[StallCause.Jit] = jit;

        // The rest of the running time by the samples' stacks; samples that showed JIT or GC are already counted above.
        var rest = Math.Max(running - gc - jit, 0);
        var sampled = facts.Samples.Where(s => s.Key is not (StallCause.Jit or StallCause.GarbageCollection or StallCause.WaitingForCpu or StallCause.Blocked)).ToList();
        var total = sampled.Sum(s => s.Value);
        if (total > 0)
        {
            foreach (var (cause, count) in sampled)
            {
                result[cause] += rest * count / total;
            }
        }
        else
        {
            result[StallCause.Other] += rest;
        }

        result[StallCause.Blocked] = Math.Max(duration - result.Values.Sum(), 0);
        return result;
    }

    /// <summary>The cause that took the most of the stall.</summary>
    public static StallCause MainCause(IReadOnlyDictionary<StallCause, double> split) => split.MaxBy(s => s.Value).Key;

    /// <summary>
    /// The cause a CPU sample's stack shows, its frames from the leaf to the root as <c>module!method</c>: the JIT or the
    /// GC anywhere in it first (they run inside whatever called them); then, from the leaf up, the first frame of
    /// Avalonia's styling or templates, its layout or its rendering.
    /// </summary>
    public static StallCause Classify(IReadOnlyList<string> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Any(IsJit))
        {
            return StallCause.Jit;
        }

        if (frames.Any(IsGc))
        {
            return StallCause.GarbageCollection;
        }

        foreach (var frame in frames)
        {
            if (Contains(frame, "Avalonia.Styling") || Contains(frame, "Avalonia.Controls.Templates") || Contains(frame, "ApplyTemplate")
                || Contains(frame, "TemplatedControl") || Contains(frame, "Avalonia.Markup.Xaml") || Contains(frame, "XamlIl") || Contains(frame, "!Avalonia.StyledElement.ApplyStyling"))
            {
                return StallCause.StylesAndTemplates;
            }

            if (Contains(frame, "Avalonia.Layout") || Contains(frame, ".MeasureOverride") || Contains(frame, ".ArrangeOverride")
                || Contains(frame, ".MeasureCore") || Contains(frame, ".ArrangeCore"))
            {
                return StallCause.Layout;
            }

            if (Contains(frame, "Avalonia.Rendering") || Contains(frame, "Avalonia.Skia") || Contains(frame, "SkiaSharp") || Contains(frame, "libSkiaSharp"))
            {
                return StallCause.Rendering;
            }
        }

        return StallCause.Other;
    }

    private static bool IsJit(string frame) =>
        frame.StartsWith("clrjit!", StringComparison.OrdinalIgnoreCase) || Contains(frame, "ThePreStub") || Contains(frame, "PreStubWorker")
        || Contains(frame, "MethodDesc::DoPrestub") || Contains(frame, "ClassLoader::") || Contains(frame, "JIT_");

    private static bool IsGc(string frame) =>
        Contains(frame, "WKS::gc_heap") || Contains(frame, "SVR::gc_heap") || Contains(frame, "GCHeap::") || Contains(frame, "gc_heap::");

    private static bool Contains(string frame, string text) => frame.Contains(text, StringComparison.Ordinal);

    private static double Clamp(double value, double max) => Math.Clamp(value, 0, Math.Max(max, 0));
}
