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

using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace HeapScope.Benchmarks;

/// <summary>
/// The call stack gate of ADR-037 (sprint 4.3 task 1): can the process that tails a growing trace fold its call stacks?
/// TraceEvent's <c>EventPipeEventSource</c> exposes none, so this reads the event record it keeps internally, checking its
/// layout first. The reader is a process of its own (<c>--read</c>); it folds stacks by instruction pointer. Afterwards the
/// stacks are compared, sample by sample, with those of TraceLog's conversion of the finished file; the conversion of the
/// file's prefix and a rundown-only session at the start are tried too. Measurement only: nothing here ships.
/// </summary>
internal static class LiveStacksSpike
{
    private const string RundownProvider = "Microsoft-Windows-DotNETRuntimeRundown";

    private static readonly List<object> s_kept = [];
    private static int s_next;

    public static int Run(BenchmarkArgs args)
    {
        if (args.Get("allocate") is not null)
        {
            return Allocate(args.GetInt("seconds", 30), args.GetInt("pause-every", 0), args.GetInt("depth", 24), args.GetInt("idle-ms", 0), args.GetInt("cold-ms", 0));
        }

        if (args.Get("read") is { } file)
        {
            return Read(file, args.Get("stacks") != "no");
        }

        var seconds = args.GetInt("seconds", 10);
        var pauseEvery = args.GetInt("pause-every", 0);
        var depth = args.GetInt("depth", 24);
        var idle = args.GetInt("idle-ms", 0);
        var stacks = args.Get("stacks") ?? "yes";
        var folder = Directory.CreateTempSubdirectory("heapscope-live-stacks-").FullName;
        var path = Path.Combine(folder, "live.nettrace");
        var prefix = Path.Combine(folder, "prefix.nettrace");
        var pid = args.GetInt("pid", 0);
        using var target = pid > 0 ? null : Start(["live-stacks", "--allocate", "true", "--seconds", Text(seconds + 60), "--pause-every", Text(pauseEvery), "--depth", Text(depth), "--idle-ms", Text(idle), "--cold-ms", Text(args.GetInt("cold-ms", 0))], redirect: false);
        Process? reader = null;
        var copiedAt = DateTime.MinValue;
        try
        {
            Thread.Sleep(pid > 0 ? 0 : 3000);
            var client = new DiagnosticsClient(target?.Id ?? pid);
            Line($"live-stacks: {(pid > 0 ? "another process, " : string.Empty)}{seconds} s, pause every {pauseEvery}, depth {depth}, stacks {stacks}");
            var names = StartRundown(client, Path.Combine(folder, "rundown.nettrace"));

            var keywords = (long)(ClrTraceEventParser.Keywords.GC | ClrTraceEventParser.Keywords.Loader | ClrTraceEventParser.Keywords.Jit);
            var providers = new List<EventPipeProvider> { new("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, keywords) };
            if (args.GetInt("counters-s", 0) is > 0 and var every)
            {
                // A counter at an interval: does an event of another thread push out the samples the runtime holds?
                providers.Add(new EventPipeProvider("System.Runtime", EventLevel.Informational, 0, new Dictionary<string, string> { ["EventCounterIntervalSec"] = Text(every) }));
            }

            using (var session = client.StartEventPipeSession(providers, requestRundown: true, circularBufferMB: 256))
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1))
            {
                var recording = Task.Run(() => session.EventStream.CopyTo(output));
                reader = Start(["live-stacks", "--read", path, "--stacks", stacks], redirect: true);
                var said = reader.StandardOutput.ReadToEndAsync();
                var failed = reader.StandardError.ReadToEndAsync();
                Thread.Sleep(TimeSpan.FromSeconds(seconds / 2.0));
                if (idle > 0)
                {
                    WaitUntilStill(path);
                }

                copiedAt = DateTime.UtcNow;
                CopyPrefix(path, prefix);
                Thread.Sleep(TimeSpan.FromSeconds(seconds / 2.0));
                session.Stop();
                recording.Wait(TimeSpan.FromSeconds(60));
                output.Flush(flushToDisk: true);
                File.WriteAllText(path + ".done", "done");
                if (!reader.WaitForExit(120_000))
                {
                    Line("  the reader did not finish in 120 s");
                    return 1;
                }

                Console.Write(said.Result);
                Console.Write(failed.Result);
                Line($"  reader exit code {reader.ExitCode}");
            }

            Line($"  file {Mb(new FileInfo(path).Length)}");
            if (stacks != "no")
            {
                Compare(path, names, copiedAt);
            }

            Convert(prefix, "prefix");
            return reader.ExitCode;
        }
        finally
        {
            Kill(reader);
            Kill(target);
            if (args.Get("keep") is { } keep)
            {
                Directory.CreateDirectory(keep);
                foreach (var kept in new[] { path, prefix }.Where(File.Exists))
                {
                    File.Copy(kept, Path.Combine(keep, Path.GetFileName(kept)), overwrite: true);
                }
            }

            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The reader: tails the file, counts ticks and folds their stacks, and writes every sample for the comparison.</summary>
    private static unsafe int Read(string path, bool stacks)
    {
        var record = stacks ? EventRecordAccessor.Create() : null;
        var parents = new Dictionary<(int Parent, ulong Frame), int>();
        var types = new Dictionary<string, (long Count, long Bytes)>(StringComparer.Ordinal);
        var lags = new List<double>();
        var late = new SortedDictionary<int, (int Count, double Longest, HashSet<int> Threads)>();
        var samples = new List<(int Frames, ulong Hash)>();
        long ticks = 0, withStack = 0, frames = 0, lost;
        var fold = new Stopwatch();
        var started = Stopwatch.StartNew();
        using (var tail = new LiveTailSpike.TailStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), () => File.Exists(path + ".done")))
        using (var source = new EventPipeEventSource(tail))
        {
            source.Clr.GCAllocationTick += e =>
            {
                ticks++;
                var lag = (DateTime.UtcNow - e.TimeStamp.ToUniversalTime()).TotalMilliseconds;
                if (ticks % 16 == 0)
                {
                    lags.Add(lag);
                }

                // Samples more than half a second late, by the second of the recording they were taken in.
                if (lag > 500)
                {
                    var second = (int)(e.TimeStampRelativeMSec / 1000);
                    var seen = late.TryGetValue(second, out var known) ? known : (0, 0, []);
                    seen.Threads.Add(e.ThreadID);
                    late[second] = (seen.Count + 1, Math.Max(seen.Longest, lag), seen.Threads);
                }

                var name = e.TypeName;
                types.TryGetValue(name, out var total);
                types[name] = (total.Count + 1, total.Bytes + e.AllocationAmount64);
                if (record is null)
                {
                    return;
                }

                fold.Start();
                var stack = record.Stack(e);
                var node = 0;
                for (var i = stack.Length - 1; i >= 0; i--)
                {
                    ref var id = ref CollectionsMarshal.GetValueRefOrAddDefault(parents, (node, stack[i]), out var known);
                    if (!known)
                    {
                        id = parents.Count;
                    }

                    node = id;
                }

                fold.Stop();
                withStack += stack.Length > 0 ? 1 : 0;
                frames += stack.Length;
                samples.Add((stack.Length, Hash(stack)));
            };
            source.Process();
            lost = source.EventsLost;
        }

        using var self = Process.GetCurrentProcess();
        lags.Sort();
        double P(double p) => lags.Count == 0 ? double.NaN : lags[(int)Math.Min(lags.Count - 1, Math.Round(p * (lags.Count - 1)))];
        Line($"  reader: {ticks:N0} ticks, {types.Count} types, {lost:N0} lost, {withStack:N0} with a stack, {frames:N0} frames, {parents.Count:N0} folded nodes");
        Line($"  reader: delay p50 {P(0.5):N0} / p95 {P(0.95):N0} / max {P(1):N0} ms");
        foreach (var (second, (count, longest, threads)) in late)
        {
            Line($"  late samples taken in second {second}: {count:N0} on {threads.Count} threads, at most {longest:N0} ms");
        }

        Line($"  reader: CPU {self.TotalProcessorTime.TotalMilliseconds:N0} ms of {started.ElapsedMilliseconds:N0} ms, reading and folding stacks {fold.ElapsedMilliseconds:N0} ms, peak working set {Mb(self.PeakWorkingSet64)}, GC heap {Mb(GC.GetTotalMemory(false))} (server GC {System.Runtime.GCSettings.IsServerGC})");
        using var writer = new BinaryWriter(File.Create(path + ".samples"));
        foreach (var (count, hash) in samples)
        {
            writer.Write(count);
            writer.Write(hash);
        }

        return 0;
    }

    /// <summary>The reader's stacks against TraceLog's for the finished file, as multisets; and what names the rundowns give.</summary>
    private static void Compare(string path, Dictionary<ulong, ulong>? early, DateTime copiedAt)
    {
        var ours = new Dictionary<(int, ulong), int>();
        using (var reader = new BinaryReader(File.OpenRead(path + ".samples")))
        {
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                var key = (reader.ReadInt32(), reader.ReadUInt64());
                ours[key] = ours.GetValueOrDefault(key) + 1;
            }
        }

        var watch = Stopwatch.StartNew();
        var etlx = TraceLog.CreateFromEventPipeDataFile(path, path + ".etlx", new TraceLogOptions { ContinueOnError = true });
        var converted = watch.Elapsed;
        var theirs = new Dictionary<(int, ulong), int>();
        long ticks = 0, frames = 0, inEarly = 0, beforeCopy = 0;
        var ranges = early?.OrderBy(m => m.Key).ToArray();
        var starts = ranges?.Select(m => m.Key).ToArray();
        long inCompiled = 0, notNamed = 0;
        var compiled = new List<(ulong Start, ulong Size)>();
        var namedBefore = new HashSet<ulong>();
        using (var log = new TraceLog(etlx))
        {
            var source = log.Events.GetSource();
            source.Clr.MethodLoadVerbose += e => compiled.Add((e.MethodStartAddress, (ulong)e.MethodSize));
            source.Clr.GCAllocationTick += e =>
            {
                ticks++;
                beforeCopy += e.TimeStamp.ToUniversalTime() < copiedAt ? 1 : 0;
                var stack = new List<ulong>();
                for (var s = e.CallStack(); s is not null; s = s.Caller)
                {
                    stack.Add(s.CodeAddress.Address);
                }

                var span = CollectionsMarshal.AsSpan(stack);
                frames += span.Length;
                if (starts is not null)
                {
                    foreach (var address in span)
                    {
                        var i = Array.BinarySearch(starts, address);
                        i = i < 0 ? ~i - 1 : i;
                        if (i >= 0 && address < starts[i] + ranges![i].Value)
                        {
                            inEarly++;
                        }
                        else if (namedBefore.Contains(address) || compiled.Exists(m => address >= m.Start && address < m.Start + m.Size))
                        {
                            // Compiled while recording, its event before this sample: named when the sample arrives.
                            namedBefore.Add(address);
                            inCompiled++;
                        }
                        else
                        {
                            notNamed++;
                        }
                    }
                }

                var key = (span.Length, Hash(span));
                theirs[key] = theirs.GetValueOrDefault(key) + 1;
            };
            source.Process();
        }

        var same = ours.Sum(o => Math.Min(o.Value, theirs.GetValueOrDefault(o.Key)));
        Line($"  TraceLog, finished file: {ticks:N0} ticks, {frames:N0} frames, {theirs.Count:N0} distinct stacks, converted in {converted.TotalMilliseconds:N0} ms");
        Line($"  samples taken before the copy at half time: {beforeCopy:N0} (the prefix line says how many the file already held)");
        Line($"  same stack in both: {same:N0} of {ours.Values.Sum():N0} read live and {ticks:N0} of TraceLog ({ours.Count:N0} distinct live)");
        if (starts is not null)
        {
            Line($"  frames named by the start rundown: {inEarly:N0} of {frames:N0}; by a method event before the sample: {inCompiled:N0} ({compiled.Count:N0} methods compiled); by neither: {notNamed:N0}");
        }
    }

    /// <summary>Converts a trace that ends anywhere: does TraceEvent accept it, and how long does it take.</summary>
    private static void Convert(string path, string what)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var etlx = TraceLog.CreateFromEventPipeDataFile(path, path + ".etlx", new TraceLogOptions { ContinueOnError = true });
            var converted = watch.Elapsed;
            long ticks = 0, frames = 0, named = 0;
            using var log = new TraceLog(etlx);
            var source = log.Events.GetSource();
            source.Clr.GCAllocationTick += e =>
            {
                ticks++;
                for (var s = e.CallStack(); s is not null; s = s.Caller)
                {
                    frames++;
                    named += s.CodeAddress.Method is null ? 0 : 1;
                }
            };
            source.Process();
            Line($"  TraceLog, {what} ({Mb(new FileInfo(path).Length)}): {ticks:N0} ticks, {named:N0} of {frames:N0} frames named, converted in {converted.TotalMilliseconds:N0} ms");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Line($"  TraceLog, {what}: refused after {watch.ElapsedMilliseconds:N0} ms: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>A session that is stopped at once with a rundown: the methods compiled so far, by start address and size.</summary>
    private static Dictionary<ulong, ulong>? StartRundown(DiagnosticsClient client, string path)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using (var session = client.StartEventPipeSession([new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, (long)ClrTraceEventParser.Keywords.Loader)], requestRundown: true, circularBufferMB: 64))
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 16))
            {
                var copy = Task.Run(() => session.EventStream.CopyTo(output));
                session.Stop();
                copy.Wait(TimeSpan.FromSeconds(30));
            }

            var taken = watch.Elapsed;
            var methods = new Dictionary<ulong, ulong>();
            using var source = new EventPipeEventSource(path);
            var rundown = new ClrRundownTraceEventParser(source);
            rundown.MethodDCStopVerbose += e => methods[e.MethodStartAddress] = (ulong)e.MethodSize;
            rundown.MethodDCStartVerbose += e => methods[e.MethodStartAddress] = (ulong)e.MethodSize;
            source.Process();
            Line($"  start rundown: {methods.Count:N0} methods, {Mb(new FileInfo(path).Length)}, session {taken.TotalMilliseconds:N0} ms, read by {watch.ElapsedMilliseconds:N0} ms ({RundownProvider})");
            return methods;
        }
        catch (Exception ex) when (ex is ServerErrorException or ServerNotAvailableException or IOException or FormatException)
        {
            Line($"  start rundown failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static ulong Hash(ReadOnlySpan<ulong> stack)
    {
        var hash = 14695981039346656037UL;
        foreach (var frame in stack)
        {
            hash = (hash ^ frame) * 1099511628211UL;
        }

        return hash;
    }

    /// <summary>Waits until the file has not grown for a second: the target is idle and the runtime has sent what it had.</summary>
    private static void WaitUntilStill(string path)
    {
        var (length, since) = (-1L, Stopwatch.StartNew());
        var limit = Stopwatch.StartNew();
        while (since.ElapsedMilliseconds < 1000 && limit.Elapsed < TimeSpan.FromSeconds(20))
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length != length)
            {
                length = file.Length;
                since.Restart();
            }

            Thread.Sleep(20);
        }
    }

    private static void CopyPrefix(string from, string to)
    {
        using var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = File.Create(to);
        input.CopyTo(output);
    }

    private static Process Start(string[] arguments, bool redirect)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!, arguments) { UseShellExecute = false, RedirectStandardOutput = redirect, RedirectStandardError = redirect };
        return Process.Start(info)!;
    }

    private static void Kill(Process? process)
    {
        try
        {
            process?.Kill();
            process?.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("N1", CultureInfo.InvariantCulture) + " MB";

    private static void Line(FormattableString text) => Console.WriteLine(text.ToString(CultureInfo.InvariantCulture));

    private static void Line(string text) => Console.WriteLine(text);

    /// <summary>The target: allocates at the end of call paths of a given depth that branch at the bottom.</summary>
    private static int Allocate(int seconds, int pauseEvery, int depth, int idleMs, int coldMs)
    {
        Thread.Sleep(coldMs);
        var until = Stopwatch.StartNew();
        var burst = Stopwatch.StartNew();
        while (until.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            // A second of work, then idle: what a program does between requests.
            if (idleMs > 0 && burst.ElapsedMilliseconds > 1000)
            {
                Thread.Sleep(idleMs);
                burst.Restart();
            }

            s_next++;
            Left(depth, s_next);
            if (pauseEvery > 0 && s_next % pauseEvery == 0)
            {
                Thread.Sleep(1);
            }
        }

        return s_kept.Count >= 0 ? 0 : 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Left(int depth, int path) => depth == 0 ? Leaf(path) : 1 + Step(depth - 1, path);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Right(int depth, int path) => depth == 0 ? Leaf(path) : 2 + Step(depth - 1, path);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Step(int depth, int path) => ((path >> (depth & 7)) & 1) == 0 ? Left(depth, path) + 1 : Right(depth, path) + 1;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Leaf(int path)
    {
        object item = (path % 3) switch
        {
            0 => new byte[1024],
            1 => new string('x', 200),
            _ => new List<int>(64),
        };
        if (path % 100 == 0)
        {
            s_kept.Add(item);
        }

        if (s_kept.Count > 100_000)
        {
            s_kept.Clear();
        }

        return 1;
    }

    /// <summary>
    /// Reads the stack of an event from the record TraceEvent keeps internally. The record is the one Windows defines
    /// (EVENT_RECORD, evntcons.h); its offsets are checked against the library's own struct before anything is read.
    /// </summary>
    private sealed unsafe class EventRecordAccessor(Func<TraceEvent, nint> record)
    {
        private const int ExtendedDataCount = 84;
        private const int ExtendedData = 88;
        private const ushort StackTrace64 = 6;

        public static EventRecordAccessor Create()
        {
            var field = typeof(TraceEvent).GetField("eventRecord", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new NotSupportedException("TraceEvent.eventRecord is not there.");
            var layout = field.FieldType.GetElementType() ?? throw new NotSupportedException("TraceEvent.eventRecord is not a pointer.");
            Check(layout, "ExtendedDataCount", ExtendedDataCount);
            Check(layout, "ExtendedData", ExtendedData);
            var item = layout.GetField("ExtendedData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.FieldType.GetElementType()!;
            Check(item, "ExtType", 2);
            Check(item, "DataSize", 6);
            Check(item, "DataPtr", 8);
            var method = new DynamicMethod("EventRecord", typeof(nint), [typeof(TraceEvent)], typeof(TraceEvent).Module, skipVisibility: true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Ret);
            return new EventRecordAccessor(method.CreateDelegate<Func<TraceEvent, nint>>());
        }

        public ReadOnlySpan<ulong> Stack(TraceEvent e)
        {
            var data = (byte*)record(e);
            if (data is null)
            {
                return [];
            }

            var count = *(ushort*)(data + ExtendedDataCount);
            var item = *(byte**)(data + ExtendedData);
            for (var i = 0; i < count && item is not null; i++, item += 16)
            {
                var size = *(ushort*)(item + 6);
                if (*(ushort*)(item + 2) == StackTrace64 && size >= 8)
                {
                    // A match id, then the instruction pointers, the innermost first.
                    return new ReadOnlySpan<ulong>(*(byte**)(item + 8) + 8, (size - 8) / 8);
                }
            }

            return [];
        }

        private static void Check(Type type, string field, int offset)
        {
            var actual = (int)Marshal.OffsetOf(type, field);
            if (actual != offset)
            {
                throw new NotSupportedException($"{type.Name}.{field} is at {actual}, not {offset}.");
            }
        }
    }
}
