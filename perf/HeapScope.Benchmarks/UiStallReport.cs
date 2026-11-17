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
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

namespace HeapScope.Benchmarks;

/// <summary>
/// Developer tool (sprint 3.12, task 3): attributes each UI stall the app's watchdog logged to its causes, from a WPR
/// recording of the same run (CPU sampling, context switches, ready thread and .NET events: <c>wpr -start CPU -start
/// DotNET</c>). A stall is a "UI thread was blocked for X ms" or "A UI operation ran for at least X ms" line of the app's log
/// (the window before its time stamp); the process is the one the log's file name names.
/// Usage: ui-stalls --etl &lt;file.etl&gt; --log &lt;heapscope-...-ui-PID.log&gt; [--min-ms 100] [--frames 0] [--thread &lt;id&gt;]
/// (--frames N prints each stall's N heaviest leaf frames of the Other samples, to see what they are).
/// </summary>
internal static partial class UiStallReport
{
    public static int Run(BenchmarkArgs args)
    {
        var etl = args.Get("etl") ?? throw new ArgumentException("--etl <file.etl> is required.");
        var logPath = args.Get("log") ?? throw new ArgumentException("--log <the app's log> is required.");
        var minMs = args.GetDouble("min-ms", 100);
        var frames = args.GetInt("frames", 0);
        var pid = int.Parse(PidOf().Match(Path.GetFileName(logPath)) is { Success: true } m ? m.Groups[1].Value : throw new ArgumentException("The log's file name names no process id (heapscope-DATE-ui-PID.log)."), CultureInfo.InvariantCulture);
        var stalls = ReadStalls(logPath, minMs);
        Console.WriteLine($"{logPath}: {stalls.Count} stalls of at least {minMs} ms in process {pid}");

        using var log = TraceLog.OpenOrConvert(etl, new TraceLogOptions { ConversionLog = TextWriter.Null });
        var process = log.Processes.Where(p => p.ProcessID == pid).MaxBy(p => p.EndTimeRelativeMsec - p.StartTimeRelativeMsec)
            ?? throw new ArgumentException($"Process {pid} is not in {etl}.");
        var uiThread = args.Get("thread") is { } chosen ? int.Parse(chosen, CultureInfo.InvariantCulture)
            : process.Threads.OrderBy(t => t.StartTimeRelativeMSec).First().ThreadID;
        Console.WriteLine($"{etl}: process {process.Name} {pid}, UI thread {uiThread}; trace starts {log.SessionStartTime:O}");

        var timeline = Read(log, process, uiThread);
        Console.WriteLine($"UI thread: {timeline.Running.Count:N0} running stretches, {timeline.Ready.Count:N0} ready stretches, {timeline.Samples.Count:N0} samples, {timeline.Jit.Count:N0} methods compiled, {timeline.GcPauses.Count:N0} GC pauses");
        Console.WriteLine();
        Console.WriteLine("Stall (log time, ms)         | waiting for CPU | GC   | JIT  | styles+templates | layout | rendering | other | blocked | main cause");
        // The watchdog logs a long operation and the lag it caused as two lines of one stall: each kind is totaled alone.
        var totals = new Dictionary<string, Dictionary<StallCause, double>>(StringComparer.Ordinal)
        {
            ["lag"] = Enum.GetValues<StallCause>().ToDictionary(c => c, _ => 0.0),
            ["operation"] = Enum.GetValues<StallCause>().ToDictionary(c => c, _ => 0.0),
        };
        foreach (var stall in stalls)
        {
            var start = (stall.EndUtc - log.SessionStartTime.ToUniversalTime()).TotalMilliseconds - stall.Ms;
            var end = start + stall.Ms;
            var samples = timeline.Samples.Where(s => s.Ms >= start && s.Ms < end).ToList();
            var facts = new StallFacts(
                stall.Ms,
                Overlap(timeline.Running, start, end),
                Overlap(timeline.Ready, start, end),
                Overlap(timeline.GcPauses, start, end),
                Overlap(timeline.Jit, start, end),
                samples.GroupBy(s => s.Cause).ToDictionary(g => g.Key, g => g.Count()));
            var split = UiStallAttribution.Split(facts);
            foreach (var (cause, ms) in split)
            {
                totals[stall.Kind][cause] += ms;
            }

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{stall.EndUtc.ToLocalTime():HH:mm:ss.fff} {stall.Kind,-9} {stall.Ms,5:N0} | {split[StallCause.WaitingForCpu],15:N0} | {split[StallCause.GarbageCollection],4:N0} | {split[StallCause.Jit],4:N0} | {split[StallCause.StylesAndTemplates],16:N0} | {split[StallCause.Layout],6:N0} | {split[StallCause.Rendering],9:N0} | {split[StallCause.Other],5:N0} | {split[StallCause.Blocked],7:N0} | {UiStallAttribution.MainCause(split)}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"    facts: running {facts.RunningMs:N1}, ready {facts.ReadyMs:N1}, GC {facts.GcPauseMs:N1}, JIT {facts.JitMs:N1}, samples {string.Join(", ", facts.Samples.Select(s => $"{s.Key} {s.Value}"))}"));
            if (timeline.Collections.Where(c => c.At >= start && c.At < end).ToList() is { Count: > 0 } collections)
            {
                Console.WriteLine($"    collections: {string.Join("; ", collections.Select(c => $"gen {c.Generation} {c.Reason} {c.Type}"))}");
            }
            if (split[StallCause.Blocked] >= stall.Ms / 3)
            {
                // The longest stretches off the processor in the stall, with where the UI thread stopped.
                var offs = timeline.SwitchOuts
                    .Where(o => o.At < end && o.At > start - 10_000)
                    .Select(o => (o.At, End: timeline.Running.FirstOrDefault(r => r.Start >= o.At).Start, o.Reason, o.Frames))
                    .Where(o => o.End > start && o.At < end)
                    .Select(o => (Ms: Math.Min(o.End, end) - Math.Max(o.At, start), o.Reason, o.Frames))
                    .OrderByDescending(o => o.Ms)
                    .Take(3);
                foreach (var off in offs)
                {
                    var managed = off.Frames.Where(f => !f.StartsWith("ntoskrnl", StringComparison.OrdinalIgnoreCase)).Take(14);
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"    off the processor {off.Ms:N1} ms ({off.Reason}): {string.Join(" <- ", managed)}"));
                }
            }

            if (frames > 0)
            {
                foreach (var leaf in samples.Where(s => s.Cause == StallCause.Other).GroupBy(s => s.Leaf).OrderByDescending(g => g.Count()).Take(frames))
                {
                    Console.WriteLine($"      {leaf.Count(),4}  {leaf.Key}");
                }
            }
        }

        Console.WriteLine();
        foreach (var (kind, total) in totals)
        {
            var all = total.Values.Sum();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"All {stalls.Count(s => s.Kind == kind)} {kind} stalls, {all:N0} ms: {string.Join(", ", total.OrderByDescending(t => t.Value).Select(t => $"{t.Key} {t.Value:N0} ms ({100 * t.Value / Math.Max(all, 1):N0}%)"))}"));
        }

        return 0;
    }

    private static List<Stall> ReadStalls(string path, double minMs)
    {
        var stalls = new List<Stall>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var match = StallLine().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var ms = double.Parse(match.Groups["ms"].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
            if (ms >= minMs)
            {
                var at = DateTimeOffset.Parse(match.Groups["at"].Value, CultureInfo.InvariantCulture).UtcDateTime;
                stalls.Add(new Stall(at, ms, match.Groups["kind"].Value == "blocked" ? "lag" : "operation"));
            }
        }

        return stalls;
    }

    private static Timeline Read(TraceLog log, TraceProcess process, int ui)
    {
        var timeline = new Timeline();
        double? runningSince = null;
        double? readySince = null;
        var jitSince = new Dictionary<long, double>();
        double? suspendedAt = null;
        foreach (var ev in log.Events.FilterByTime(process.StartTimeRelativeMsec, process.EndTimeRelativeMsec))
        {
            switch (ev)
            {
                case CSwitchTraceData cs:
                    if (cs.NewThreadID == ui)
                    {
                        if (readySince is { } ready)
                        {
                            timeline.Ready.Add((ready, cs.TimeStampRelativeMSec));
                            readySince = null;
                        }

                        runningSince = cs.TimeStampRelativeMSec;
                    }
                    else if (cs.OldThreadID == ui && runningSince is { } since)
                    {
                        timeline.Running.Add((since, cs.TimeStampRelativeMSec));
                        runningSince = null;

                        // Where it stopped: the waits that make a stall "blocked" (sprint 3.12, task 3).
                        var waited = new List<string>();
                        for (var frame = cs.CallStack(); frame is not null && waited.Count < 40; frame = frame.Caller)
                        {
                            waited.Add(frame.CodeAddress.ModuleName + "!" + frame.CodeAddress.FullMethodName);
                        }

                        timeline.SwitchOuts.Add((cs.TimeStampRelativeMSec, cs.OldThreadWaitReason.ToString(), waited));
                    }

                    break;
                case DispatcherReadyThreadTraceData ready when ready.AwakenedThreadID == ui && runningSince is null:
                    readySince ??= ready.TimeStampRelativeMSec;
                    break;
                case SampledProfileTraceData sample when sample.ThreadID == ui:
                    var stack = new List<string>();
                    for (var frame = sample.CallStack(); frame is not null; frame = frame.Caller)
                    {
                        stack.Add(frame.CodeAddress.ModuleName + "!" + frame.CodeAddress.FullMethodName);
                    }

                    timeline.Samples.Add(new Sampled(sample.TimeStampRelativeMSec, UiStallAttribution.Classify(stack), stack.FirstOrDefault(f => !f.StartsWith("ntoskrnl", StringComparison.OrdinalIgnoreCase)) ?? "?"));
                    break;
                case MethodJittingStartedTraceData jit when jit.ThreadID == ui && jit.ProcessID == process.ProcessID:
                    jitSince[jit.MethodID] = jit.TimeStampRelativeMSec;
                    break;
                case MethodLoadUnloadVerboseTraceData loaded when loaded.ThreadID == ui && jitSince.Remove(loaded.MethodID, out var began):
                    timeline.Jit.Add((began, loaded.TimeStampRelativeMSec));
                    break;
                case GCStartTraceData gcStart when gcStart.ProcessID == process.ProcessID:
                    timeline.Collections.Add((gcStart.TimeStampRelativeMSec, gcStart.Depth, gcStart.Reason.ToString(), gcStart.Type.ToString()));
                    break;
                case GCSuspendEETraceData suspend when suspend.ProcessID == process.ProcessID && suspend.Reason is GCSuspendEEReason.SuspendForGC or GCSuspendEEReason.SuspendForGCPrep:
                    suspendedAt = suspend.TimeStampRelativeMSec;
                    break;
                case GCNoUserDataTraceData restarted when restarted.ProcessID == process.ProcessID && restarted.EventName == "GC/RestartEEStop" && suspendedAt is { } paused:
                    timeline.GcPauses.Add((paused, restarted.TimeStampRelativeMSec));
                    suspendedAt = null;
                    break;
            }
        }

        return timeline;
    }

    private static double Overlap(List<(double Start, double End)> spans, double start, double end) =>
        spans.Sum(s => Math.Max(0, Math.Min(s.End, end) - Math.Max(s.Start, start)));

    [GeneratedRegex(@"-ui-(\d+)\.log$")]
    private static partial Regex PidOf();

    [GeneratedRegex(@"^(?<at>\S+) \[\w+\] HeapScope\.UI\.Services\.DispatcherWatchdog: (?:UI thread was (?<kind>blocked) for|A UI operation (?<kind>ran) for at least) (?<ms>[\d,]+) ms")]
    private static partial Regex StallLine();

    private sealed record Stall(DateTime EndUtc, double Ms, string Kind);

    private sealed record Sampled(double Ms, StallCause Cause, string Leaf);

    private sealed class Timeline
    {
        public List<(double Start, double End)> Running { get; } = [];

        public List<(double Start, double End)> Ready { get; } = [];

        public List<(double Start, double End)> Jit { get; } = [];

        public List<(double Start, double End)> GcPauses { get; } = [];

        public List<Sampled> Samples { get; } = [];

        public List<(double At, string Reason, List<string> Frames)> SwitchOuts { get; } = [];

        public List<(double At, int Generation, string Reason, string Type)> Collections { get; } = [];
    }
}
