// HeapScope
// Copyright (C) 2026 Ray Klundt
// https://github.com/rklundt/HeapScope
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
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

namespace HeapScope.Benchmarks;

/// <summary>
/// Developer tool (sprint 3.12, task 8): where a process's busiest thread spent a WPR recording (the same recording as
/// <c>ui-stalls</c>): on the processor, ready for one, or off it by wait reason (a page read in from a memory-mapped dump
/// waits as PageIn or Executive), per stretch of time; and its heaviest methods, inclusive and at the leaf.
/// Usage: cpu-profile --etl &lt;file.etl&gt; --process &lt;name&gt; [--top 25] [--stretch-s 5] [--modules a,b] (inclusive methods
/// of these modules only; default HeapScope and Microsoft.Diagnostics.Runtime).
/// </summary>
internal static class CpuProfileReport
{
    public static int Run(BenchmarkArgs args)
    {
        var etl = args.Get("etl") ?? throw new ArgumentException("--etl <file.etl> is required.");
        var name = args.Get("process") ?? throw new ArgumentException("--process <name> is required.");
        var top = args.GetInt("top", 25);
        var stretchMs = args.GetDouble("stretch-s", 5) * 1000;
        var modules = (args.Get("modules") ?? "heapscope,microsoft.diagnostics.runtime").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        using var log = TraceLog.OpenOrConvert(etl, new TraceLogOptions { ConversionLog = TextWriter.Null });
        var process = log.Processes.Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).MaxBy(p => p.EndTimeRelativeMsec - p.StartTimeRelativeMsec)
            ?? throw new ArgumentException($"No process {name} in {etl}.");
        var thread = process.Threads.MaxBy(t => t.CPUMSec)!.ThreadID;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{etl}: {process.Name} {process.ProcessID}, {process.EndTimeRelativeMsec - process.StartTimeRelativeMsec:N0} ms, {process.Threads.Count()} threads; busiest thread {thread}"));

        double? runningSince = null;
        double? readySince = null;
        (double At, string Reason)? offSince = null;
        var states = new SortedDictionary<int, Dictionary<string, double>>();
        void Add(double from, double to, string state)
        {
            for (var at = from; at < to;)
            {
                var stretch = (int)((at - process.StartTimeRelativeMsec) / stretchMs);
                var stretchEnd = process.StartTimeRelativeMsec + ((stretch + 1) * stretchMs);
                var part = Math.Min(to, stretchEnd) - at;
                var row = states.TryGetValue(stretch, out var found) ? found : states[stretch] = new Dictionary<string, double>(StringComparer.Ordinal);
                row[state] = row.GetValueOrDefault(state) + part;
                at += part;
            }
        }

        var inclusive = new Dictionary<string, int>(StringComparer.Ordinal);
        var leaves = new Dictionary<string, int>(StringComparer.Ordinal);
        var samples = 0;
        foreach (var ev in log.Events.FilterByTime(process.StartTimeRelativeMsec, process.EndTimeRelativeMsec))
        {
            switch (ev)
            {
                case CSwitchTraceData cs when cs.NewThreadID == thread:
                    if (readySince is { } ready)
                    {
                        Add(ready, cs.TimeStampRelativeMSec, "ready");
                        readySince = null;
                    }
                    else if (offSince is { } off)
                    {
                        Add(off.At, cs.TimeStampRelativeMSec, "off: " + off.Reason);
                    }

                    offSince = null;
                    runningSince = cs.TimeStampRelativeMSec;
                    break;
                case CSwitchTraceData cs when cs.OldThreadID == thread:
                    if (runningSince is { } since)
                    {
                        Add(since, cs.TimeStampRelativeMSec, "running");
                    }

                    runningSince = null;
                    offSince = (cs.TimeStampRelativeMSec, cs.OldThreadState.ToString() is "Ready" or "Standby" ? "preempted" : cs.OldThreadWaitReason.ToString());
                    break;
                case DispatcherReadyThreadTraceData readied when readied.AwakenedThreadID == thread && runningSince is null:
                    if (offSince is { } waited)
                    {
                        Add(waited.At, readied.TimeStampRelativeMSec, "off: " + waited.Reason);
                        offSince = null;
                    }

                    readySince ??= readied.TimeStampRelativeMSec;
                    break;
                case SampledProfileTraceData sample when sample.ThreadID == thread:
                    samples++;
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    var leaf = true;
                    for (var frame = sample.CallStack(); frame is not null; frame = frame.Caller)
                    {
                        var module = frame.CodeAddress.ModuleName;
                        var method = module + "!" + frame.CodeAddress.FullMethodName;
                        if (leaf && !module.StartsWith("ntoskrnl", StringComparison.OrdinalIgnoreCase))
                        {
                            leaves[method] = leaves.GetValueOrDefault(method) + 1;
                            leaf = false;
                        }

                        if (Array.Exists(modules, m => module.StartsWith(m, StringComparison.OrdinalIgnoreCase)) && seen.Add(method))
                        {
                            inclusive[method] = inclusive.GetValueOrDefault(method) + 1;
                        }
                    }

                    break;
            }
        }

        var reasons = states.Values.SelectMany(r => r.Keys).Distinct().OrderBy(k => k == "running" ? 0 : k == "ready" ? 1 : 2).ThenBy(k => k, StringComparer.Ordinal).ToList();
        Console.WriteLine();
        Console.WriteLine($"Busiest thread by {stretchMs / 1000:N0} s stretch (ms): {string.Join(" | ", reasons)}");
        foreach (var (stretch, row) in states)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {stretch * stretchMs / 1000,5:N0} s  {string.Join(" | ", reasons.Select(r => $"{row.GetValueOrDefault(r),7:N0}"))}"));
        }

        var totals = reasons.ToDictionary(r => r, r => states.Values.Sum(row => row.GetValueOrDefault(r)));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  total   {string.Join(" | ", reasons.Select(r => $"{r} {totals[r]:N0}"))}"));
        Console.WriteLine();
        Console.WriteLine($"Heaviest methods of {string.Join(", ", modules)}, inclusive ({samples:N0} samples, about 1 ms each):");
        foreach (var (method, count) in inclusive.OrderByDescending(m => m.Value).Take(top))
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {count,7:N0}  {100.0 * count / Math.Max(samples, 1),5:N1}%  {method}"));
        }

        Console.WriteLine();
        Console.WriteLine("Heaviest leaf methods:");
        foreach (var (method, count) in leaves.OrderByDescending(m => m.Value).Take(top))
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {count,7:N0}  {100.0 * count / Math.Max(samples, 1),5:N1}%  {method}"));
        }

        return 0;
    }
}
