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
/// logs under the store's category. The store serializes opens; nothing here outlives one open but the test hooks.
/// </summary>
internal sealed class SnapshotOpener(IndexCacheLocator locator, IndexCacheSigner signer, SnapshotKinds kinds, ILogger logger)
{
    private readonly IndexCacheSigner _signer = signer;
    private readonly SnapshotKinds _kinds = kinds;

    /// <summary>Tests only: a loader that stands in for the file's own, to reach what a real dump cannot be made to do.</summary>
    internal Func<string, ISnapshotLoader>? LoaderForTests { get; set; }

    /// <summary>Tests only: the DAC check of a cache open (<see cref="SnapshotKindHandler.CheckDac"/>), which a made-up dump cannot reach.</summary>
    internal Func<string, Exception?>? DacCheckForTests { get; set; }

    /// <summary>Tests only: the process name read again from the dump for an older cache (<see cref="SnapshotKindHandler.ReadProcessName"/>).</summary>
    internal Func<string, (string? Name, string Source)?>? ProcessNameForTests { get; set; }

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
        // the clean-up collection, which pauses queries on other snapshots, is skipped then (sprint 1.2 review). Set only
        // once a load starts: a refusal before it allocated nothing (sprint 3.12 review of task 9).
        var collect = false;
        try
        {
            throttled.Report(new JobProgress("Checking index cache", 0, null));
            var fingerprint = SnapshotFingerprint.Compute(path);
            var cachePath = locator.GetCachePath(path);
            IndexCacheOpenResult? cached = null;
            string? rebuildReason = null;
            OlderCache? older = null;
            Exception? dacProblem = null;
            if (!request.Rebuild)
            {
                // A full dump about to open from a valid cache loads no DAC now, but its field reader will (review H1): the
                // DAC is checked first, before the cache is read (re-review), so a refusal costs no graph. One that needs
                // trust is refused when the front end can ask; otherwise, and when the DAC is missing (sprint 3.12), the
                // graph opens and value reads are refused from the start, with the reason.
                if (IndexCacheReader.Probe(cachePath, _signer, fingerprint) is null && SnapshotFileSignature.Detect(path) is { } kind)
                {
                    dacProblem = (DacCheckForTests ?? _kinds.For(kind).CheckDac)(path);
                    if (request.CheckDac && dacProblem is UntrustedDacException untrusted)
                    {
                        throw untrusted;
                    }
                }

                cached = TryOpenCache(cachePath, fingerprint, forced, out rebuildReason, out older, cancellationToken);
            }

            if (cached is null && request.RequireCache)
            {
                // A snapshot that opened from an older cache in place of a rebuild has one still, but a reopen does not use it:
                // its own message, so "changed" is not blamed (sprint 3.12 review of task 9; owner's wording, 2026-11-13).
                throw new SnapshotChangedException(older is not null
                    ? $"{Path.GetFileName(path)} was opened from an older index cache because its DAC could not be used, so it was not reopened automatically after the analysis worker restarted. Open it again."
                    : $"{path} changed since it was opened, or its index cache is gone or out of date, so it was not reopened automatically. Open it again to load the dump.");
            }

            collect = cached is null;

            string? writtenCache = cachePath;
            HeapGraph graph;
            IndexCacheMetadata metadata;
            MemoryMode mode;
            ulong identity;
            var fromCache = cached is not null;
            ValueReadsRefused? refused = null;
            var fellBack = false;
            if (cached is not null)
            {
                graph = cached.Graph!;
                metadata = cached.Metadata!;
                mode = cached.Mode;
                identity = cached.Identity;

                // Reads the DAC would refuse are not offered (sprint 3.12, B2): refused from the start, with the reason.
                if (dacProblem is not null)
                {
                    refused = Refuse(path, dacProblem, notes);
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
                catch (Exception ex) when (older is not null && LacksDac(ex))
                {
                    // A cancel that raced the failure is a cancel, not a reason to open the older cache (review of task 9).
                    cancellationToken.ThrowIfCancellationRequested();

                    // The rebuild cannot reach the DAC it needs (gone after a runtime update, declined, or the symbol server
                    // failed): the older cache opens instead, so the dump stays readable (sprint 3.12, the 3.10 review M14).
                    throttled.Report(new JobProgress("Opening the older index cache", 0, null));
                    var fallback = OpenOlder(older, fingerprint, forced, ex, cancellationToken);
                    if (fallback is null)
                    {
                        // OpenOlder collected already (3.12 re-review: not twice).
                        collect = false;
                        throw;
                    }

                    (graph, metadata, mode, identity) = (fallback.Graph!, fallback.Metadata!, fallback.Mode, fallback.Identity);
                    writtenCache = cachePath;
                    fromCache = true;
                    fellBack = true;
                    try
                    {
                        metadata = WithCurrentProcessName(path, metadata);
                        refused = Refuse(path, ex, notes: null);
                        notes.Add(OlderCacheNote(DacRefusals.Cause(ex)));
                        dacProblem = ex;
                    }
                    catch
                    {
                        graph.Dispose();
                        throw;
                    }
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
                descriptor = Describe(snapshot, mode, fromCache, writtenCache, identity, stopwatch.Elapsed, notes) with
                {
                    UntrustedDac = refused is not null && dacProblem is UntrustedDacException { Path: { } dacPath, Sha256: { } sha256 } untrusted
                        ? new DacTrustDetails(dacPath, sha256, untrusted.RuntimeVersion ?? "")
                        : null,
                };
            }
            catch
            {
                graph.Dispose();
                throw;
            }

            throttled.Flush();
            LogIdentity(descriptor, snapshot, metadata, fingerprint, request, fellBack ? older : null, fromCache, mode);
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
    private void LogIdentity(SnapshotDescriptor descriptor, ISnapshot snapshot, IndexCacheMetadata metadata, SnapshotFingerprint fingerprint, OpenSnapshotRequest request, OlderCache? older, bool fromCache, MemoryMode mode)
    {
        var info = snapshot.Info;
        var source = older is not null ? string.Create(CultureInfo.InvariantCulture, $"older index cache (loader version {older.LoaderVersion}) in place of a failed rebuild")
            : fromCache ? "index cache" : request.Rebuild ? "dump, rebuild requested" : "dump";
        source += request.CheckDac ? ", DAC question allowed" : ", DAC question not asked";

        // A full dump's cache written before 2.3.7 has no diagnostics or DAC: its zeros are not facts (2.3.7 review, H2 confirmation).
        var recorded = info.Kind != SnapshotKind.FullDump || metadata.Dac is not null;
        string Recorded(long value) => recorded ? value.ToString(CultureInfo.InvariantCulture) : "not recorded";
        logger.LogInformation(
            "Snapshot {Id} opened in {Seconds:N1} s: {Path}; {Bytes} bytes, fingerprint {Fingerprint:x16}, modified {ModifiedUtc:O}; {Kind}, capabilities {Capabilities}, facts {Facts}; process {Process} ({Pid}; name: {ProcessNameSource}), runtime {Runtime}, captured {CapturedUtc:O}{CaptureSource}; from {Source}{Reopen}, {Mode}, cache {CachePath} identity {Identity:x16}, written by {WriterVersion} with loader version {LoaderVersion}; {Objects} objects, {References} references, {Roots} roots, {DroppedReferences} references and {DroppedRoots} roots dropped, unreadable objects {Unreadable}, incomplete segments {IncompleteSegments}, static field roots {StaticRoots}; GC handles {Handles}; DAC {Dac} ({DacRule}); unavailable features {Unavailable}; notes: {Notes}",
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
            Describe(metadata.Handles, info.Kind),
            (metadata.Dac?.Path ?? (metadata.Dac is not null ? "symbol server" : info.Kind == SnapshotKind.FullDump ? "not recorded (index cache from before 2.3.7)" : "none (a gcdump needs none)"))
                + (snapshot.TryGetService<ValueReadsRefused>(out _) ? ", recorded when the cache was written and not usable now" : string.Empty),
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
        // only to be dropped before the rebuild. Nor is one whose metadata cannot be read (3.12 re-review): it is rebuilt.
        var written = IndexCacheReader.ReadMetadata(cachePath, _signer, fingerprint, out var metadataFailure);
        if (written is null && metadataFailure is { Reason: not IndexCacheInvalidReason.Missing })
        {
            logger.LogInformation("Index cache {Path} not used: {Reason} ({Detail})", cachePath, metadataFailure.Reason, metadataFailure.Detail);
            rebuildReason = metadataFailure.Detail;
            return null;
        }

        if (written is not null
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

        // A load that could not check something then, which it can now (peer review 4.5: the second walk of GC handles turned on again).
        if (written is not null && _kinds.For(written.Info.Kind).WhyLoadAgain(written) is { } again)
        {
            logger.LogInformation("Index cache {Path} not used: {Why}", cachePath, again);
            rebuildReason = again;
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
            diagnostics.ProcessNameSource,
            diagnostics.Handles);
        var timings = loaded.Timings;
        logger.LogInformation(
            "Loaded {Path} from the dump in {Seconds:N1} s: open {OpenSeconds:N1} s, objects {ObjectsSeconds:N1} s, freeze and collect {FreezeSeconds:N1} s, references {ReferencesSeconds:N1} s, roots {RootsSeconds:N1} s, build {BuildSeconds:N1} s; per phase {Costs}; unreadable objects {Unreadable}, incomplete segments {IncompleteSegments}, static field roots {StaticRoots}, debuggable modules {Debuggable}; GC handles {Handles}",
            path,
            timings.Total.TotalSeconds,
            timings.Open.TotalSeconds,
            timings.Objects.TotalSeconds,
            timings.Freeze.TotalSeconds,
            timings.References.TotalSeconds,
            timings.Roots.TotalSeconds,
            timings.Build.TotalSeconds,
            timings.PhaseCosts.Count == 0 ? "not measured" : string.Join("; ", timings.PhaseCosts),
            diagnostics.UnreadableObjects,
            diagnostics.IncompleteSegments,
            diagnostics.StaticFieldRoots,
            diagnostics.DebuggableModules.Count,
            Describe(diagnostics.Handles, metadata.Info.Kind));

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

    /// <summary>
    /// The snapshot's note when it opened from an older loader's cache because a rebuild could not reach its DAC (owner's
    /// wording, 2026-11-13; reworded for loader version 5, a draft for the owner). Version 4 changed only the process name, which is read again
    /// from the dump; version 5 reads GC handles and references that an older cache lacks.
    /// </summary>
    internal static string OlderCacheNote(string cause) =>
        $"Opened from the index cache an older version of HeapScope wrote, because a new one could not be built: {cause}. "
        + "It can lack what this version reads: in Server GC processes and collectible assemblies, objects held only by a GC handle or by a type can look unreachable. The process name was read again from the dump. "
        + "Once the DAC can be used, close the snapshot and open the dump again to rebuild the cache.";

    /// <summary>
    /// The metadata with the process name this loader reads from the dump (no DAC needed): the one answer loader version 4
    /// changed (ClrMdDumpLoader, sprint 3.10), which Compare and the static collections detector match on. Kept as cached
    /// when the dump cannot be read for it.
    /// </summary>
    private IndexCacheMetadata WithCurrentProcessName(string path, IndexCacheMetadata metadata)
    {
        try
        {
            if ((ProcessNameForTests ?? _kinds.For(metadata.Info.Kind).ReadProcessName)(path) is not { } current)
            {
                return metadata;
            }

            if (current.Name != metadata.Info.ProcessName)
            {
                logger.LogInformation("Process name of {Path} read again from the dump: {Old} in the older index cache, now {New} ({Source})", path, metadata.Info.ProcessName, current.Name, current.Source);
            }

            return metadata with { Info = metadata.Info with { ProcessName = current.Name }, ProcessNameSource = current.Source };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Any failure of the data target: the graph is fine without it (3.12 re-review: nothing may leak the graph here).
            logger.LogWarning("Process name of {Path} could not be read again from the dump ({Error}: {Message}); the older index cache's is kept", path, ex.GetType().Name, ex.Message);
            return metadata;
        }
    }

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

        // Still an older cache at or above the floor: another process may have replaced it meanwhile (3.12 re-review).
        if (result.Success
            && (result.Metadata!.LoaderVersion < _kinds.For(result.Metadata.Info.Kind).OldestFallbackLoaderVersion || result.Metadata.LoaderVersion >= older.CurrentVersion))
        {
            logger.LogWarning(
                "Index cache {Path} could not be opened in place of the failed rebuild: it now holds loader version {Version}, and only a version from {Oldest} to below {Current} may open in its place",
                older.Path,
                result.Metadata.LoaderVersion,
                _kinds.For(result.Metadata.Info.Kind).OldestFallbackLoaderVersion,
                older.CurrentVersion);
            result.Graph?.Dispose();
            return null;
        }

        if (!result.Success)
        {
            result.Graph?.Dispose();
            logger.LogWarning("Index cache {Path} could not be opened in place of the failed rebuild: {Reason} ({Detail})", older.Path, result.Reason, result.Detail);
            return null;
        }

        logger.LogWarning(
            "Index cache {Path} from loader version {Old} opened in place of a rebuild (current loader version {New}), {Mode}: the rebuild could not reach the dump's DAC ({Error}: {Message}). Field values and strings are refused for this snapshot.",
            older.Path,
            result.Metadata!.LoaderVersion,
            older.CurrentVersion,
            mode,
            failure.GetType().Name,
            failure.Message);
        return result;
    }

    // What a rebuild cannot get past without native code nobody has or trusts; a damaged dump is not one of them.
    private static bool LacksDac(Exception ex) => ex is UntrustedDacException or RuntimeComponentMissingException;

    /// <summary>
    /// The refusal of value reads for a snapshot whose DAC cannot be used (<paramref name="problem"/>), logged with the full
    /// detail the short reason leaves out. A cache hit also puts the reason in <paramref name="notes"/>, which the front ends
    /// point to; the older cache's note words its own.
    /// </summary>
    private ValueReadsRefused Refuse(string path, Exception problem, List<string>? notes)
    {
        var refused = DacRefusals.For(problem);
        logger.LogInformation("Snapshot {Path} opens without reading field values or strings: {Error}: {Message}", path, problem.GetType().Name, problem.Message);
        notes?.Add(refused.Reason);
        return refused;
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

    private static string Describe(HandleWalk? walk, SnapshotKind kind) => walk is null
        ? (kind == SnapshotKind.FullDump ? "not recorded" : "none (not a full dump)")
        : string.Create(CultureInfo.InvariantCulture, $"{walk.Outcome}: {walk.Heaps} heaps, {walk.Tables} tables walked, {walk.Processors?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} processors, {walk.Added} handles added ({walk.How})");

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
