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

using System.Diagnostics;
using System.Runtime;
using HeapScope.Core.Loading;
using HeapScope.Loaders.ClrMd;
using HeapScope.Storage;

namespace HeapScope.Benchmarks;

/// <summary>
/// Loads a dump with HeapScope's loader into the compact graph, writes the index cache, then measures the switch to
/// mapped mode and both reopens (ARCHITECTURE.md 6.5 and 10). Memory is peak private bytes.
/// Usage: load-dump --dump &lt;path&gt; [--cache-mb 512] [--keep-cache 1] [--cancel-at &lt;phase&gt;].
/// Budgets (ARCHITECTURE.md 10, exit code 1 when missed): first load under 20 s for dumps under 1 GB, otherwise under
/// 3 min; peak private bytes under 1.5x the managed heap and under 4 GB; reopen in memory under 20 s, mapped under 2 s.
/// <c>--cancel-at "Reading references"</c> (any phase name, or "Opening dump") cancels as that phase starts and reports
/// the time from cancel to the exception, against the 2 s acceptance criterion.
/// </summary>
internal static class LoadDumpBenchmark
{
    public static int Run(BenchmarkArgs args)
    {
        var path = Path.GetFullPath(args.Get("dump") ?? throw new ArgumentException("--dump <path> is required."));
        var cacheMb = args.GetInt("cache-mb", 512);
        var keep = args.GetInt("keep-cache", 0) != 0;
        var cache = path + IndexCacheFormat.Extension;
        if (args.Get("cancel-at") is { } cancelPhase)
        {
            return MeasureCancel(path, cacheMb, cancelPhase);
        }

        Console.WriteLine($"load-dump: {path} ({Measurement.Mb(new FileInfo(path).Length)}), ClrMD cache {cacheMb} MB");

        // The worker's GC (sprint 3.12, R5): Server GC, not concurrent, half the processors as heaps, which WorkerHost sets
        // with DOTNET_GCHeapCount. A load timed under another GC is not the worker's load.
        if (WhyNotTheWorkersGc() is { } different && args.Get("any-gc") is null)
        {
            Console.Error.WriteLine($"load-dump: {different} Set DOTNET_GCHeapCount={Math.Max(1, Environment.ProcessorCount / 2):x} as the worker does, or add --any-gc 1.");
            return 2;
        }

        using var sampler = new PrivateBytesSampler();
        var stopwatch = Stopwatch.StartNew();
        var loader = new ClrMdDumpLoader(new ClrMdLoaderOptions { MaxDumpCacheBytes = cacheMb * 1024L * 1024L });
        var result = loader.Load(path, new PhaseLogger(sampler));
        var graph = result.Graph;
        var managedHeap = (long)graph.TotalSize;
        var t = result.Timings;
        Measurement.Line("Runtime", result.Info.RuntimeVersion ?? "?");
        Measurement.Line("Objects", $"{graph.ObjectCount:N0}, {graph.Types.Count:N0} types, {graph.Roots.Count:N0} roots");
        Measurement.Line("References", $"{graph.EdgeCount:N0} ({(double)graph.EdgeCount / Math.Max(graph.ObjectCount, 1):N2} per object)");
        Measurement.Line("Dropped", $"{graph.DroppedReferenceCount:N0} references, {graph.DroppedRootCount:N0} roots, {result.Diagnostics.UnreadableObjects:N0} unreadable objects, {result.Diagnostics.IncompleteSegments} incomplete segments; complete graph {result.Capabilities.HasFlag(HeapScope.Core.SnapshotCapabilities.CompleteGraph)}");
        Measurement.Line("Static field roots", $"{result.Diagnostics.StaticFieldRoots:N0}; debuggable modules {result.Diagnostics.DebuggableModules.Count}");
        Measurement.Line("Graph columns", $"{Measurement.Mb(graph.ColumnBytes)} ({(double)graph.ColumnBytes / Math.Max(graph.ObjectCount, 1):N1} bytes per object)");
        Measurement.Line("Graph hash", GraphHash(result));
        Measurement.Line("Column hashes", string.Join(" ", new[] { Of(graph.Addresses.Span), Of(graph.Sizes.Span), Of(graph.HeapKinds.Span), Of(graph.Flags.Span), Of(graph.EdgeStarts.Span), Of(graph.Edges.Span), Of(graph.LargeSizes.Span) }));
        static string Of<T>(ReadOnlySpan<T> span) where T : unmanaged => Convert.ToHexStringLower(System.IO.Hashing.XxHash3.Hash(System.Runtime.InteropServices.MemoryMarshal.AsBytes(span)))[..8];
        Measurement.Line("Open dump", $"{t.Open.TotalSeconds:N1} s");
        Measurement.Line("Object pass", $"{t.Objects.TotalSeconds:N1} s");
        Measurement.Line("Freeze and collect", $"{t.Freeze.TotalSeconds:N1} s");
        Measurement.Line("Reference pass", $"{t.References.TotalSeconds:N1} s");
        Measurement.Line("Roots", $"{t.Roots.TotalSeconds:N1} s");
        Measurement.Line("Build", $"{t.Build.TotalSeconds:N1} s");
        foreach (var cost in t.PhaseCosts)
        {
            Measurement.Line("  " + cost.Phase, cost.ToString());
        }
        Measurement.Line("Peak private bytes (load)", Measurement.Mb(sampler.Peak));

        var write = Stopwatch.StartNew();
        var metadata = new IndexCacheMetadata(result.Info, result.Capabilities, IndexCacheMetadata.CurrentWriterVersion);
        var fingerprint = SnapshotFingerprint.Compute(path);
        IndexCacheWriter.Write(cache, IndexCacheSigner.None, graph, metadata, fingerprint);
        Measurement.Line("Write index cache", $"{write.Elapsed.TotalSeconds:N1} s ({Measurement.Mb(new FileInfo(cache).Length)})");
        var firstLoad = stopwatch.Elapsed;
        Measurement.Line("First load total", $"{firstLoad.TotalSeconds:N1} s");
        Measurement.Line("Peak private bytes (load + write)", Measurement.Mb(sampler.Peak));

        // Switch to mapped mode (ARCHITECTURE.md 6.5): drop the built graph, compact once, reopen mapped.
        var transition = Stopwatch.StartNew();
        graph.Dispose();
        result = null;
        graph = null;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        var privateAfterDrop = Private();
        var mappedStopwatch = Stopwatch.StartNew();
        var mapped = IndexCacheReader.Open(cache, IndexCacheSigner.None, fingerprint, MemoryMode.Mapped);
        var mappedReopen = mappedStopwatch.Elapsed;
        Measurement.Line("Mapped transition", $"{transition.Elapsed.TotalSeconds:N1} s, private {Measurement.Mb(privateAfterDrop)} after drop, {Measurement.Mb(Private())} mapped, {(mapped.Success ? "ok" : mapped.Detail)}");
        mapped.Graph?.Dispose();

        var reopen = Stopwatch.StartNew();
        var inMemory = IndexCacheReader.Open(cache, IndexCacheSigner.None, fingerprint, MemoryMode.InMemory);
        Measurement.Line("Reopen in memory", $"{reopen.Elapsed.TotalSeconds:N1} s, {(inMemory.Success ? "ok" : inMemory.Detail)}");
        inMemory.Graph?.Dispose();

        var inMemoryReopen = reopen.Elapsed;
        if (!keep)
        {
            File.Delete(cache);
        }

        var peak = sampler.Peak;
        var loadBudget = new FileInfo(path).Length < 1024L * 1024 * 1024 ? TimeSpan.FromSeconds(20) : TimeSpan.FromMinutes(3);
        var checks = new (string Name, bool Met)[]
        {
            ($"First load under {loadBudget.TotalSeconds:N0} s", firstLoad <= loadBudget),
            ("Peak private bytes under 1.5x the managed heap", peak <= managedHeap * 1.5 || peak <= 512L * 1024 * 1024),
            ("Peak private bytes under 4 GB", peak <= 4L * 1024 * 1024 * 1024),
            ("Reopen mapped under 2 s", mapped.Success && mappedReopen <= TimeSpan.FromSeconds(2)),
            ("Reopen in memory under 20 s", inMemory.Success && inMemoryReopen <= TimeSpan.FromSeconds(20)),
        };
        foreach (var (name, met) in checks)
        {
            Measurement.Line(name, met ? "met" : "MISSED");
        }

        return checks.All(c => c.Met) ? 0 : 1;
    }

    /// <summary>Cancels a load as <paramref name="phase"/> starts and reports how long the loader took to stop.</summary>
    private static int MeasureCancel(string path, int cacheMb, string phase)
    {
        using var cancellation = new CancellationTokenSource();
        var canceledAt = Stopwatch.StartNew();
        var progress = new CancelAtPhase(phase, cancellation, canceledAt);
        try
        {
            new ClrMdDumpLoader(new ClrMdLoaderOptions { MaxDumpCacheBytes = cacheMb * 1024L * 1024L }).Load(path, progress, cancellation.Token).Graph.Dispose();
            Measurement.Line($"Cancel at {phase}", "the phase was never reached; the load completed");
            return 1;
        }
        catch (OperationCanceledException)
        {
            var latency = canceledAt.Elapsed;
            Measurement.Line($"Cancel at {phase}", $"stopped {latency.TotalMilliseconds:N0} ms after cancel ({(latency <= TimeSpan.FromSeconds(2) ? "met" : "MISSED")}, budget 2 s)");
            return latency <= TimeSpan.FromSeconds(2) ? 0 : 1;
        }
    }

    private sealed class CancelAtPhase(string phase, CancellationTokenSource cancellation, Stopwatch canceledAt) : IProgress<LoadProgress>
    {
        public void Report(LoadProgress value)
        {
            if (!cancellation.IsCancellationRequested && string.Equals(value.Phase, phase, StringComparison.OrdinalIgnoreCase))
            {
                canceledAt.Restart();
                cancellation.Cancel();
            }
        }
    }

    /// <summary>Prints private bytes (now and peak so far) whenever the load enters a new phase.</summary>
    private sealed class PhaseLogger(PrivateBytesSampler sampler) : IProgress<LoadProgress>
    {
        private string? _phase;

        public void Report(LoadProgress value)
        {
            if (value.Phase != _phase)
            {
                _phase = value.Phase;
                Measurement.Line("  entering " + value.Phase, $"private {Measurement.Mb(Private())}, peak {Measurement.Mb(sampler.Peak)}");
            }
        }
    }

    private static long Private()
    {
        using var process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }

    /// <summary>Why this process's GC is not set up as the worker's, or null when it is.</summary>
    internal static string? WhyNotTheWorkersGc()
    {
        var heaps = GC.GetConfigurationVariables().TryGetValue("HeapCount", out var count) ? Convert.ToInt64(count, System.Globalization.CultureInfo.InvariantCulture) : 0;
        var expected = Math.Max(1, Environment.ProcessorCount / 2);
        return !System.Runtime.GCSettings.IsServerGC ? "This process does not run Server GC; the worker does."
            : System.Runtime.GCSettings.LatencyMode == System.Runtime.GCLatencyMode.Interactive ? "This process runs concurrent GC; the worker does not."
            : heaps != expected ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"This process has {heaps} GC heaps; the worker has {expected}.")
            : null;
    }

    /// <summary>
    /// One hash of everything the loader built (sprint 3.12, R4): the object columns, the edges, the type table, the roots, the
    /// free space per heap kind and what was dropped or unreadable (the review's M1 added the last three), so a change to the
    /// loader's hot loop is shown to build the same snapshot.
    /// </summary>
    internal static string GraphHash(HeapScope.Core.Loading.LoadResult result)
    {
        var graph = result.Graph;
        var hash = new System.IO.Hashing.XxHash3();
        hash.Append(System.Runtime.InteropServices.MemoryMarshal.AsBytes(graph.Addresses.Span));
        // Types by where they first appear in address order, so the order a loader meets them in does not change the hash.
        var rank = new int[graph.Types.Count];
        Array.Fill(rank, -1);
        var order = new List<int>();
        var ranked = new int[graph.ObjectCount];
        var typeIndices = graph.TypeIndices.Span;
        for (var i = 0; i < ranked.Length; i++)
        {
            var type = typeIndices[i];
            if (rank[type] < 0)
            {
                rank[type] = order.Count;
                order.Add(type);
            }

            ranked[i] = rank[type];
        }

        hash.Append(System.Runtime.InteropServices.MemoryMarshal.AsBytes(ranked.AsSpan()));
        hash.Append(System.Runtime.InteropServices.MemoryMarshal.AsBytes(graph.Sizes.Span));
        hash.Append(System.Runtime.InteropServices.MemoryMarshal.AsBytes(graph.HeapKinds.Span));
        hash.Append(System.Runtime.InteropServices.MemoryMarshal.AsBytes(graph.Flags.Span));
        hash.Append(System.Runtime.InteropServices.MemoryMarshal.AsBytes(graph.EdgeStarts.Span));
        hash.Append(System.Runtime.InteropServices.MemoryMarshal.AsBytes(graph.Edges.Span));
        hash.Append(System.Runtime.InteropServices.MemoryMarshal.AsBytes(graph.LargeSizes.Span));
        foreach (var type in order)
        {
            hash.Append(System.Text.Encoding.UTF8.GetBytes(graph.Types.GetName(type)));
            hash.Append(System.BitConverter.GetBytes((int)graph.Types.Kinds[type]));
        }

        hash.Append(System.BitConverter.GetBytes(graph.Roots.Count));
        hash.Append(System.Runtime.InteropServices.MemoryMarshal.AsBytes(graph.Roots.Roots.Span));
        hash.Append(System.BitConverter.GetBytes(graph.DroppedReferenceCount));
        hash.Append(System.BitConverter.GetBytes(graph.DroppedRootCount));
        hash.Append(System.Runtime.InteropServices.MemoryMarshal.AsBytes((result.Info.FreeBytes ?? []).ToArray().AsSpan()));
        hash.Append(System.BitConverter.GetBytes(result.Diagnostics.UnreadableObjects));
        hash.Append(System.BitConverter.GetBytes(result.Diagnostics.IncompleteSegments));
        return System.Convert.ToHexStringLower(hash.GetCurrentHash());
    }
}
