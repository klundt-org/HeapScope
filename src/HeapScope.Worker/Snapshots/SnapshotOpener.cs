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

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using HeapScope.Contracts;
using HeapScope.Core;
using HeapScope.Core.Graph;
using HeapScope.Core.Loading;
using HeapScope.Loaders.ClrMd;
using HeapScope.Storage;
using HeapScope.Worker.Rpc;
using Microsoft.Extensions.Logging;

namespace HeapScope.Worker.Snapshots;

/// <summary>
/// Opens one snapshot (sprint 3.12, moved out of <see cref="SnapshotStore"/>, which keeps the open snapshots): reuses a valid
/// index cache or loads the dump and writes the cache, picks the memory mode (ADR-015), and describes and logs the result. It
/// logs under the store's category, so its lines read as before. The store serializes opens; this class holds no state of its own.
/// </summary>
internal sealed class SnapshotOpener(IndexCacheLocator locator, IndexCacheSigner signer, SnapshotKinds kinds, ILogger logger)
{
    private readonly IndexCacheSigner _signer = signer;
    private readonly SnapshotKinds _kinds = kinds;

    /// <summary>Tests only: a loader that stands in for the file's own, to reach what a real dump cannot be made to do.</summary>
    internal Func<string, ISnapshotLoader>? LoaderForTests { get; set; }

    /// <summary>Tests only: the DAC trust check of a cache open, which a made-up dump cannot reach.</summary>
    internal Action<string>? DacCheckForTests { get; set; }

    public OpenedSnapshot Open(string path, OpenSnapshotRequest request, IProgress<JobProgress>? progress, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var throttled = new Rpc.ThrottledProgress<JobProgress>(progress);
        var notes = new List<string>();
        var forced = request.Mode switch
        {
            SnapshotMemoryMode.InMemory => MemoryMode.InMemory,
            SnapshotMemoryMode.Mapped => MemoryMode.Mapped,
            _ => (MemoryMode?)null,
        };

        // Only a dump load (or a failed one) leaves large garbage behind; a cache hit allocates exactly its graph, so
        // the clean-up collection, which pauses queries on other snapshots, is skipped then (sprint 1.2 review).
        var collect = true;
        try
        {
            throttled.Report(new JobProgress("Checking index cache", 0, null));
            var fingerprint = SnapshotFingerprint.Compute(path);
            var cachePath = locator.GetCachePath(path);
            IndexCacheOpenResult? cached = null;
            string? rebuildReason = null;
            OlderCache? older = null;
            if (!request.Rebuild)
            {
                // A full dump about to open from a valid cache loads no DAC now, but its field reader will: only now can
                // the front end ask about a DAC that needs trust (review H1). Before the cache is read (re-review), so a
                // refusal costs no graph.
                if (request.CheckDac && IndexCacheReader.Probe(cachePath, _signer, fingerprint) is null
                    && SnapshotFileSignature.Detect(path) is { } kind)
                {
                    (DacCheckForTests ?? _kinds.For(kind).CheckBeforeCacheOpen)(path);
                }

                cached = TryOpenCache(cachePath, fingerprint, forced, out rebuildReason, out older, cancellationToken);
            }

            if (cached is null && request.RequireCache)
            {
                collect = false;
                throw new SnapshotChangedException(
                    $"{path} changed since it was opened, or its index cache is gone or out of date, so it was not reopened automatically. Open it again to load the dump.");
            }

            collect = cached is null;

            string? writtenCache = cachePath;
            HeapGraph graph;
            IndexCacheMetadata metadata;
            MemoryMode mode;
            ulong identity;
            var fromCache = cached is not null;
            ValueReadsRefused? refused = null;
            if (cached is not null)
            {
                graph = cached.Graph!;
                metadata = cached.Metadata!;
                mode = cached.Mode;
                identity = cached.Identity;

                // An open that did not ask about the DAC (the user declined it, or the command line, which never asks) must
                // not offer reads the DAC would refuse (sprint 3.12, B2): they are refused from the start, with the reason.
                if (!request.CheckDac)
                {
                    refused = RefusedByDac(path, metadata.Info.Kind);
                }
            }
            else
            {
                try
                {
                    (graph, metadata, mode, writtenCache, identity) = LoadDump(path, cachePath, fingerprint, forced, notes, rebuildReason is not null, throttled, cancellationToken);

                    // Only now is it known: "rebuilt" is true only when the load wrote its cache (3.10 review M39).
                    if (rebuildReason is not null)
                    {
                        notes.Add((writtenCache is not null ? "The index cache was rebuilt: " : "The index cache was not used: ") + rebuildReason);
                    }
                }
                catch (Exception ex) when (older is not null && LacksDac(ex) && !cancellationToken.IsCancellationRequested)
                {
                    // The rebuild cannot reach the DAC it needs (gone after a runtime update, declined, or the symbol server
                    // failed): the older cache opens instead, so the dump stays readable (sprint 3.12, the 3.10 review M14).
                    var fallback = OpenOlder(older, fingerprint, forced, ex, cancellationToken);
                    if (fallback is null)
                    {
                        throw;
                    }

                    (graph, metadata, mode, identity) = (fallback.Graph!, fallback.Metadata!, fallback.Mode, fallback.Identity);
                    writtenCache = cachePath;
                    fromCache = true;
                    refused = new ValueReadsRefused("Field values and strings are not read: " + ex.Message);
                    notes.Add(OlderCacheNote);
                }

                // LoadDump collected before choosing the mode (and again in the mapped transition), and so did the fallback.
                collect = false;
            }

            ISnapshot snapshot;
            SnapshotDescriptor descriptor;
            try
            {
                AddDataQualityNotes(graph, metadata, notes);

                // The snapshot is the file that was opened: a dump moved or copied together with its index cache keeps a
                // valid fingerprint, but the cache records the path of its first load (sprint 1.2 review).
                var info = metadata.Info with { SourcePath = path };
                snapshot = _kinds.For(info.Kind).Open(info, metadata.Capabilities, graph, path, () => EnsureUnchanged(path, fingerprint), refused);
                descriptor = Describe(snapshot, mode, fromCache, writtenCache, identity, stopwatch.Elapsed, notes);
            }
            catch
            {
                graph.Dispose();
                throw;
            }

            throttled.Flush();
            LogIdentity(descriptor, snapshot, metadata, fingerprint, request, fromCache, mode);
            return new OpenedSnapshot(snapshot, descriptor, new IndexState(writtenCache, identity, fingerprint, mode, forced));
        }
        finally
        {
            if (collect)
            {
                // A failed or canceled load leaves builder buffers, ClrMD caches and arrays behind; return them to the
                // OS (ARCHITECTURE.md 9). A successful load already collected inside LoadDump.
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            }
        }
    }

    /// <summary>
    /// One line that ties the worker's snapshot id to the file and everything a later result rests on (sprint 2.3.7 review
    /// H2): every other line names the snapshot by its id. Paths, sizes, versions, flags and notes only, never dump
    /// contents (ARCHITECTURE.md 11 and 12).
    /// </summary>
    private void LogIdentity(SnapshotDescriptor descriptor, ISnapshot snapshot, IndexCacheMetadata metadata, SnapshotFingerprint fingerprint, OpenSnapshotRequest request, bool fromCache, MemoryMode mode)
    {
        var info = snapshot.Info;
        var source = fromCache ? "index cache" : request.Rebuild ? "dump, rebuild requested" : "dump";

        // A full dump's cache written before 2.3.7 has no diagnostics or DAC: its zeros are not facts (2.3.7 review, H2 confirmation).
        var recorded = info.Kind != SnapshotKind.FullDump || metadata.Dac is not null;
        string Recorded(long value) => recorded ? value.ToString(CultureInfo.InvariantCulture) : "not recorded";
        logger.LogInformation(
            "Snapshot {Id} opened in {Seconds:N1} s: {Path}; {Bytes} bytes, fingerprint {Fingerprint:x16}, modified {ModifiedUtc:O}; {Kind}, capabilities {Capabilities}, facts {Facts}; process {Process} ({Pid}; name: {ProcessNameSource}), runtime {Runtime}, captured {CapturedUtc:O}{CaptureSource}; from {Source}{Reopen}, {Mode}, cache {CachePath} identity {Identity:x16}, written by {WriterVersion} with loader version {LoaderVersion}; {Objects} objects, {References} references, {Roots} roots, {DroppedReferences} references and {DroppedRoots} roots dropped, unreadable objects {Unreadable}, incomplete segments {IncompleteSegments}, static field roots {StaticRoots}; DAC {Dac} ({DacRule}); unavailable features {Unavailable}; notes: {Notes}",
            descriptor.SnapshotId,
            descriptor.OpenSeconds,
            descriptor.Path,
            fingerprint.Length,
            fingerprint.ContentHash,
            new DateTime(fingerprint.LastWriteUtcTicks, DateTimeKind.Utc),
            info.Kind,
            snapshot.Capabilities,
            info.Facts,
            info.ProcessName,
            info.ProcessId,
            metadata.ProcessNameSource ?? (info.Kind == SnapshotKind.FullDump ? "not recorded" : "-"),
            info.RuntimeVersion,
            info.CapturedAtUtc,
            info.Facts.HasFlag(SnapshotFacts.CaptureTimeFromFile) ? " (the file's time)" : string.Empty,
            source,
            request.RequireCache ? ", reopened after a worker restart" : string.Empty,
            mode,
            descriptor.CachePath ?? "(none)",
            descriptor.CacheIdentity,
            metadata.WriterVersion,
            metadata.LoaderVersion,
            descriptor.ObjectCount,
            descriptor.ReferenceCount,
            descriptor.RootCount,
            snapshot.Graph.DroppedReferenceCount,
            snapshot.Graph.DroppedRootCount,
            metadata.UnreadableObjects,
            Recorded(metadata.IncompleteSegments),
            Recorded(metadata.StaticFieldRoots),
            metadata.Dac?.Path ?? (metadata.Dac is not null ? "symbol server" : info.Kind == SnapshotKind.FullDump ? "not recorded (index cache from before 2.3.7)" : "none (a gcdump needs none)"),
            metadata.Dac?.Rule ?? "-",
            Unavailable(descriptor),
            descriptor.Notes.Count == 0 ? "none" : string.Join(" | ", descriptor.Notes));
    }

    // The ids only: each reason is the rule's fixed text, and a refusal logs its own.
    private static string Unavailable(SnapshotDescriptor descriptor) =>
        descriptor.Availability?.Where(a => a.Unavailable is not null).Select(a => a.Feature).ToList() is { Count: > 0 } ids ? string.Join(", ", ids) : "none";

    /// <summary>
    /// Field values and strings are read at the graph's addresses, so they must come from the dump the graph was built
    /// from: the reader opens the dump lazily, possibly hours after the snapshot opened (sprint 1.5 review H5).
    /// </summary>
    private static void EnsureUnchanged(string path, SnapshotFingerprint opened)
    {
        SnapshotFingerprint now;
        try
        {
            now = SnapshotFingerprint.Compute(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SnapshotChangedException($"The dump {path} cannot be read any more ({ex.Message}), so field values are not available. Put the dump back, or close the snapshot.", ex);
        }

        if (now != opened)
        {
            throw new SnapshotChangedException($"The dump {path} changed since this snapshot was opened, so its field values would not match. Close the snapshot and open the dump again.");
        }
    }

    /// <summary>
    /// The usable cache, or null; <paramref name="rebuildReason"/> says why one that exists was not used, and
    /// <paramref name="older"/> names a cache an older loader wrote that can still open if the rebuild cannot.
    /// </summary>
    private IndexCacheOpenResult? TryOpenCache(string cachePath, SnapshotFingerprint fingerprint, MemoryMode? forced, out string? rebuildReason, out OlderCache? older, CancellationToken cancellationToken)
    {
        rebuildReason = null;
        older = null;
        var required = IndexCacheReader.GetInMemoryBytes(cachePath, _signer, out var unusable);
        if (required is null)
        {
            // An untrusted, foreign or damaged cache is rebuilt, and the log and the snapshot's notes say why (3.7 review H3).
            if (unusable is { Reason: not IndexCacheInvalidReason.Missing })
            {
                logger.LogInformation("Index cache {Path} not used: {Reason} ({Detail})", cachePath, unusable.Reason, unusable.Detail);
                rebuildReason = unusable.Detail;
            }

            return null;
        }

        // The loader version from the metadata alone (sprint 3.12, the 3.10 review M31): an older cache's graph is not read
        // only to be dropped before the rebuild. A cache whose metadata cannot be read is opened below, which says why.
        if (IndexCacheReader.ReadMetadata(cachePath, _signer, fingerprint, out _) is { } written
            && _kinds.For(written.Info.Kind) is var handler
            && written.LoaderVersion < handler.LoaderVersion)
        {
            // Built by an older loader whose answers are now known to be wrong in some cases (review M7).
            var fallback = written.LoaderVersion >= handler.OldestFallbackLoaderVersion;
            logger.LogInformation(
                "Index cache {Path} not used: loader version {Old}, current {New}; it {Fallback}",
                cachePath,
                written.LoaderVersion,
                handler.LoaderVersion,
                fallback ? "opens instead if the rebuild cannot reach the DAC" : string.Create(CultureInfo.InvariantCulture, $"is older than version {handler.OldestFallbackLoaderVersion}, so it never opens"));
            rebuildReason = "it was written by an older version of HeapScope, so this open took longer. The release notes say what changed.";
            older = fallback ? new OlderCache(cachePath, written.LoaderVersion, handler.LoaderVersion) : null;
            return null;
        }

        var mode = MemoryModeSelector.Select(required.Value, MemoryBudget.Current(forced: forced));
        var result = IndexCacheReader.Open(cachePath, _signer, fingerprint, mode, cancellationToken);

        if (result.Success)
        {
            return result;
        }

        if (result.Reason != IndexCacheInvalidReason.Missing)
        {
            logger.LogInformation("Index cache {Path} not used: {Reason} ({Detail})", cachePath, result.Reason, result.Detail);
            rebuildReason = result.Detail;
        }

        return null;
    }

    private (HeapGraph Graph, IndexCacheMetadata Metadata, MemoryMode Mode, string? CachePath, ulong Identity) LoadDump(
        string path,
        string cachePath,
        SnapshotFingerprint fingerprint,
        MemoryMode? forced,
        List<string> notes,
        bool rebuilding,
        Rpc.ThrottledProgress<JobProgress> progress,
        CancellationToken cancellationToken)
    {
        var loader = LoaderForTests?.Invoke(path) ?? _kinds.ForFile(path).CreateLoader();
        LoadResult? loaded = loader.Load(path, new LoadProgressAdapter(progress, rebuilding), cancellationToken);
        HeapGraph? graph = loaded.Graph;
        var diagnostics = loaded.Diagnostics;
        var metadata = new IndexCacheMetadata(
            loaded.Info,
            loaded.Capabilities,
            IndexCacheMetadata.CurrentWriterVersion,
            diagnostics.DebuggableModules,
            diagnostics.UnreadableObjects,
            loader.Version,
            diagnostics.StaticFieldRoots,
            diagnostics.IncompleteSegments,
            diagnostics.Dac,
            diagnostics.ProcessNameSource);
        var timings = loaded.Timings;
        logger.LogInformation(
            "Loaded {Path} from the dump in {Seconds:N1} s: open {OpenSeconds:N1} s, objects {ObjectsSeconds:N1} s, references {ReferencesSeconds:N1} s, roots {RootsSeconds:N1} s, build {BuildSeconds:N1} s; unreadable objects {Unreadable}, incomplete segments {IncompleteSegments}, static field roots {StaticRoots}, debuggable modules {Debuggable}",
            path,
            (timings.Open + timings.Objects + timings.References + timings.Roots + timings.Build).TotalSeconds,
            timings.Open.TotalSeconds,
            timings.Objects.TotalSeconds,
            timings.References.TotalSeconds,
            timings.Roots.TotalSeconds,
            timings.Build.TotalSeconds,
            diagnostics.UnreadableObjects,
            diagnostics.IncompleteSegments,
            diagnostics.StaticFieldRoots,
            diagnostics.DebuggableModules.Count);

        // Nothing else may reference the built graph, or the mapped transition below cannot free it.
        loaded = null;
        try
        {
            progress.Report(new JobProgress("Writing index cache", 0, null));
            var identity = TryWriteCache(cachePath, graph, metadata, fingerprint, notes, cancellationToken);
            var written = identity != 0;

            // Decide the mode after the load's garbage is gone, counting the graph's own memory as available: if the
            // snapshot moved to mapped mode, that memory would be freed (sprint 1.2 review). No LOH compaction here: it
            // would copy the live graph's arrays; an aggressive collection already returns the freed regions.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            var budget = MemoryBudget.Current(forced: forced);
            budget = budget with { AvailableBytes = budget.AvailableBytes + graph.ColumnBytes };
            var mode = MemoryModeSelector.Select(graph.ColumnBytes, budget);
            logger.LogInformation(
                "Memory mode {Mode}: graph {GraphMb:N0} MB, budget {BudgetMb:N0} MB of {TotalMb:N0} MB physical",
                mode,
                graph.ColumnBytes / (1024 * 1024),
                budget.BudgetBytes / (1024 * 1024),
                budget.TotalPhysicalBytes / (1024 * 1024));
            if (mode == MemoryMode.InMemory || !written)
            {
                if (mode == MemoryMode.Mapped)
                {
                    notes.Add("The snapshot is held in memory because its index cache could not be written; memory use is higher than usual.");
                }

                return (graph, metadata, MemoryMode.InMemory, written ? cachePath : null, identity);
            }

            // Switch to mapped mode (ADR-015): drop the built arrays, compact once so the memory really returns to the
            // OS, then reopen the cache as views.
            progress.Report(new JobProgress("Mapping index cache", 0, null));
            graph.Dispose();
            graph = null;
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            var mapped = IndexCacheReader.Open(cachePath, _signer, fingerprint, MemoryMode.Mapped, cancellationToken);
            if (!mapped.Success)
            {
                throw new SnapshotFormatException($"The index cache just written could not be reopened: {mapped.Detail}");
            }

            return (mapped.Graph!, mapped.Metadata!, MemoryMode.Mapped, cachePath, mapped.Identity);
        }
        catch
        {
            graph?.Dispose();
            throw;
        }
    }

    /// <summary>The snapshot's note when it opened from an older loader's cache because a rebuild could not reach its DAC.</summary>
    internal const string OlderCacheNote =
        "Opened from the index cache an older version of HeapScope wrote, because a new one could not be built without the dump's DAC. "
        + "Types, references, roots and retained sizes come from that older version, so they can differ from what this version would find; "
        + "field values and strings are not read. When the DAC is available again, reopen the dump with Rebuild.";

    /// <summary>
    /// The older cache, opened after a rebuild failed for want of a DAC, or null when it cannot be opened any more (the caller
    /// then reports the rebuild's failure). The failed load's memory is returned to the system first, so the two never stack
    /// (the 3.10 review M31). The warning backs up a contested result: it names both loader versions and why.
    /// </summary>
    private IndexCacheOpenResult? OpenOlder(OlderCache older, SnapshotFingerprint fingerprint, MemoryMode? forced, Exception failure, CancellationToken cancellationToken)
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        var required = IndexCacheReader.GetInMemoryBytes(older.Path, _signer);
        var mode = MemoryModeSelector.Select(required ?? long.MaxValue, MemoryBudget.Current(forced: forced));
        var result = IndexCacheReader.Open(older.Path, _signer, fingerprint, mode, cancellationToken);
        if (!result.Success || result.Metadata!.LoaderVersion != older.LoaderVersion)
        {
            result.Graph?.Dispose();
            logger.LogWarning("Index cache {Path} could not be opened in place of the failed rebuild: {Reason} ({Detail})", older.Path, result.Reason, result.Detail);
            return null;
        }

        logger.LogWarning(
            "Index cache {Path} from loader version {Old} opened in place of a rebuild (current loader version {New}), {Mode}: the rebuild could not reach the dump's DAC ({Error}: {Message}). Field values and strings are refused for this snapshot.",
            older.Path,
            older.LoaderVersion,
            older.CurrentVersion,
            mode,
            failure.GetType().Name,
            failure.Message);
        return result;
    }

    // What a rebuild cannot get past without native code nobody has or trusts; a damaged dump is not one of them.
    private static bool LacksDac(Exception ex) => ex is UntrustedDacException or RuntimeComponentMissingException;

    /// <summary>
    /// The refusal of value reads for a snapshot opened without the DAC question, when its DAC lies untrusted next to the
    /// dump; null when the reads may go ahead (a trusted or installed DAC, a missing one the reader reports itself, or a kind
    /// that loads no native code).
    /// </summary>
    private ValueReadsRefused? RefusedByDac(string path, SnapshotKind kind)
    {
        try
        {
            (DacCheckForTests ?? _kinds.For(kind).CheckBeforeCacheOpen)(path);
            return null;
        }
        catch (UntrustedDacException ex)
        {
            logger.LogInformation("Snapshot {Path} opens without reading field values or strings: its DAC {Dac} (SHA-256 {Sha256}) is not trusted", path, ex.Path, ex.Sha256);
            return new ValueReadsRefused("Field values and strings are not read: " + ex.Message);
        }
    }

    /// <summary>Writes the index cache and returns its identity, or 0 (with a note) when it could not be written.</summary>
    private ulong TryWriteCache(string cachePath, HeapGraph graph, IndexCacheMetadata metadata, SnapshotFingerprint fingerprint, List<string> notes, CancellationToken cancellationToken)
    {
        try
        {
            return IndexCacheWriter.Write(cachePath, _signer, graph, metadata, fingerprint, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Index cache {Path} could not be written", cachePath);
            notes.Add($"The index cache could not be written ({ex.Message}); the next open will load the dump again.");
            return 0;
        }
    }

    private void AddDataQualityNotes(HeapGraph graph, IndexCacheMetadata metadata, List<string> notes)
    {
        if (metadata.DebuggableModules is { Count: > 0 } modules)
        {
            var names = string.Join(", ", modules.Take(5)) + (modules.Count > 5 ? $" and {modules.Count - 5} more" : string.Empty);
            notes.Add($"{modules.Count} module(s) were compiled as debuggable code ({names}). Objects held only by local variables in their methods can look unreachable.");
        }

        if (graph.DroppedReferenceCount > 0 || graph.DroppedRootCount > 0)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"{graph.DroppedReferenceCount:N0} references and {graph.DroppedRootCount:N0} roots did not point to an object and were left out."));
        }

        _kinds.For(metadata.Info.Kind).AddNotes(metadata, notes);

        if (metadata.UnreadableObjects > 0)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"{metadata.UnreadableObjects:N0} heap entries could not be read and were left out."));
        }
    }

    private static SnapshotDescriptor Describe(ISnapshot snapshot, MemoryMode mode, bool fromCache, string? cachePath, ulong identity, TimeSpan elapsed, List<string> notes)
    {
        var info = snapshot.Info;
        var graph = snapshot.Graph;
        return new SnapshotDescriptor(
            Guid.NewGuid(),
            info.SourcePath,
            info.Kind.ToDto(),
            snapshot.Capabilities.ToDto(),
            info.ProcessName,
            info.ProcessId,
            info.RuntimeVersion,
            info.CapturedAtUtc,
            graph.ObjectCount,
            graph.EdgeCount,
            graph.Types.Count,
            graph.Roots.Count,
            graph.TotalSize,
            mode == MemoryMode.Mapped ? SnapshotMemoryMode.Mapped : SnapshotMemoryMode.InMemory,
            fromCache,
            cachePath,
            elapsed.TotalSeconds,
            [.. notes],
            identity,
            info.Facts.HasFlag(SnapshotFacts.CaptureTimeFromFile),
            Rpc.FeatureRules.List(snapshot));
    }

    private sealed class LoadProgressAdapter(Rpc.ThrottledProgress<JobProgress> target, bool rebuilding) : IProgress<LoadProgress>
    {
        // A rebuild's wait says why, while the user waits (sprint 3.10 review M18).
        public void Report(LoadProgress value) =>
            target.Report(new JobProgress(rebuilding ? "Rebuilding the index cache: " + value.Phase : value.Phase, value.Completed, value.Total));
    }
}

/// <summary>An index cache an older loader wrote, which may open in place of a rebuild that cannot reach the dump's DAC.</summary>
internal sealed record OlderCache(string Path, int LoaderVersion, int CurrentVersion);

/// <summary>A snapshot just opened, with what the store keeps about its index cache.</summary>
internal sealed record OpenedSnapshot(ISnapshot Snapshot, SnapshotDescriptor Descriptor, IndexState Index);

/// <summary>Where a snapshot's index cache is and what it is; optional indices name it (sprint 2.1).</summary>
/// <param name="CachePath">The index cache the snapshot was opened from or wrote; null when none was written.</param>
/// <param name="Identity">The cache's identity; 0 when none was written.</param>
/// <param name="Forced">The memory mode the open request forced, if any; optional indices follow it too.</param>
internal sealed record IndexState(string? CachePath, ulong Identity, SnapshotFingerprint Fingerprint, MemoryMode Mode, MemoryMode? Forced);
