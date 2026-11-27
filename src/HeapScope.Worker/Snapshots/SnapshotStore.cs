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

using System.Collections.Concurrent;
using System.Diagnostics;
using HeapScope.Contracts;
using HeapScope.Core;
using HeapScope.Core.Loading;
using HeapScope.Storage;
using HeapScope.Worker.Rpc;
using Microsoft.Extensions.Logging;

namespace HeapScope.Worker.Snapshots;

/// <summary>
/// The snapshots loaded in this worker (ARCHITECTURE.md 4.1). Opening reuses a valid index cache or loads the dump with
/// ClrMD and writes the cache; the memory mode follows the budget (ADR-015). Loads are serialized: two first loads would
/// stack their peaks.
/// </summary>
internal sealed partial class SnapshotStore(WorkerOptions options, IndexCacheLocator locator, IndexCacheSigner signer, ILogger<SnapshotStore> logger) : IDisposable
{
    private readonly ConcurrentDictionary<Guid, Entry> _snapshots = new();
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SnapshotKinds _kinds = new(
        options,
        choice => LogDac(logger, choice),
        (dump, reason) => logger.LogWarning("References of {Dump} kept {Reason}; from there on they take 4 bytes each of the worker's memory", dump, reason),
        symbols => LogSymbols(logger, symbols));
    private readonly IndexCacheSigner _signer = signer;
    private readonly ReverseIndexBuilder _reverseBuilder = new(logger, signer);
    private SnapshotOpener? _opener;

    /// <summary>The memory reserved by large derived computations of every snapshot.</summary>
    private readonly MemoryReservations _reservations = new();

    private DominatorIndexBuilder? _dominatorBuilder;

    private DominatorIndexBuilder DominatorBuilder => _dominatorBuilder ??= new(logger, _reservations, _signer);

    private ObjectSetCache? _sets;

    private bool _disposed;

    public int Count => _snapshots.Count;

    /// <summary>
    /// The resolved object sets and detector tables of the open snapshots, within one budget over all of them.
    /// Closing a snapshot forgets its entries, and the cache refuses entries for snapshots that are no longer open.
    /// </summary>
    public ObjectSetCache Sets => LazyInitializer.EnsureInitialized(ref _sets, () =>
    {
        var cache = new ObjectSetCache(_snapshots.ContainsKey);
        cache.Evicted += what => logger.LogDebug("Object set cache: dropped {What} to stay within its budget", what);
        return cache;
    });

    /// <summary>
    /// Looks at a file without loading it: size, index cache state (header and fingerprint, no
    /// section hashing) and whether it is open. Application uses it to decide the memory warning.
    /// </summary>
    public SnapshotProbe Probe(string requestedPath)
    {
        var path = ExistingFullPath(requestedPath);
        var cachePath = locator.GetCachePath(path);
        var fingerprint = SnapshotFingerprint.Compute(path);
        var failure = IndexCacheReader.Probe(cachePath, _signer, fingerprint);
        var state = failure is null ? SnapshotCacheState.Valid
            : failure.Reason == IndexCacheInvalidReason.Missing ? SnapshotCacheState.Missing
            : SnapshotCacheState.Invalid;
        var detail = failure?.Detail;

        // A cache an older loader wrote is rebuilt on open, a full load: the memory warning must see it.
        if (state == SnapshotCacheState.Valid
            && IndexCacheReader.ReadMetadata(cachePath, _signer, fingerprint, out _) is { } written
            && written.LoaderVersion < _kinds.For(written.Info.Kind).LoaderVersion)
        {
            state = SnapshotCacheState.Invalid;
            detail = "it was written by an older version of HeapScope, so the dump is read again";
        }

        return new SnapshotProbe(path, new FileInfo(path).Length, state, cachePath, detail, FindOpen(path) is not null);
    }

    public async Task<SnapshotDescriptor> OpenAsync(OpenSnapshotRequest request, IProgress<JobProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = ExistingFullPath(request.Path);
        _kinds.Trust(request.TrustedDacs);
        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (FindOpen(path) is { } open)
            {
                if (!request.Rebuild)
                {
                    logger.LogInformation("Snapshot {Id} ({Path}) is already open; its descriptor is returned", open.Descriptor.SnapshotId, path);
                    return open.Descriptor;
                }

                throw new RequestRefusedException($"{path} is open. Close it before rebuilding its index cache.", nameof(request));
            }

            var opened = await Task.Run(() => Opener.Open(path, request, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
            var entry = new Entry(opened.Snapshot, opened.Descriptor, opened.Index);
            if (_disposed)
            {
                entry.Snapshot.Dispose();
                throw new ObjectDisposedException(nameof(SnapshotStore));
            }

            _snapshots[entry.Descriptor.SnapshotId] = entry;
            return entry.Descriptor;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public bool Close(Guid snapshotId)
    {
        if (!_snapshots.TryRemove(snapshotId, out var entry))
        {
            return false;
        }

        // Never blocks: outstanding leases (result sets, running queries) keep the graph until they are released. The sets
        // are forgotten after the snapshot left the open list, so a resolution finishing now cannot add one back.
        entry.Derived.Close();
        Sets.Forget(snapshotId);
        entry.Snapshot.Dispose();
        logger.LogInformation("Snapshot {Id} closed", snapshotId);
        return true;
    }

    /// <summary>
    /// The type aggregation of a loaded snapshot, computed once on first use (one parallel pass over the objects) and
    /// kept with the snapshot until it is closed. The computation is shared: it holds its own lease and runs
    /// until the snapshot closes, so a caller that cancels (a superseded query) only stops waiting. Returns null when the
    /// snapshot is not loaded or closes meanwhile.
    /// </summary>
    public async Task<Analysis.TypeAggregation?> GetAggregationAsync(Guid snapshotId, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        if (!_snapshots.TryGetValue(snapshotId, out var entry))
        {
            return null;
        }

        using var waiter = entry.Derived.JoinAggregation(lifetime => Task.Run(
            () =>
            {
                using var lease = entry.Snapshot.AcquireLease();
                var stopwatch = Stopwatch.StartNew();
                var aggregation = Analysis.TypeAggregation.Compute(entry.Snapshot.Graph, progress, lifetime);
                logger.LogInformation("Snapshot {Id} aggregated by type in {Seconds:N2} s", snapshotId, stopwatch.Elapsed.TotalSeconds);
                return aggregation;
            },
            lifetime));
        try
        {
            return await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Closed before or during the computation.
            return null;
        }
    }

    /// <summary>The snapshot, or null when it is not loaded in this worker.</summary>
    public ISnapshot? TryGet(Guid snapshotId) => _snapshots.TryGetValue(snapshotId, out var entry) ? entry.Snapshot : null;

    public void Dispose()
    {
        // The load gate is not disposed: a load in flight still releases it, then sees the flag and drops its result.
        _disposed = true;
        foreach (var id in _snapshots.Keys)
        {
            Close(id);
        }
    }

    private static string ExistingFullPath(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            throw new RequestRefusedException("A snapshot path is required.", nameof(requested));
        }

        var path = Path.GetFullPath(requested);
        return File.Exists(path) ? path : throw new RequestRefusedException($"The file {path} does not exist.", nameof(requested));
    }

    private Entry? FindOpen(string path)
    {
        foreach (var entry in _snapshots.Values)
        {
            if (PathIdentity.Same(entry.Descriptor.Path, path))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// The DAC each runtime was created with, by the loader and later by the field reader. A
    /// download from the symbol server is a warning: it sent module identifiers to Microsoft (ADR-017).
    /// </summary>
    private static void LogDac(ILogger logger, DacChoice choice)
    {
        if (choice.Downloaded)
        {
            logger.LogWarning("DAC for {Dump} downloaded from Microsoft's symbol server (no local file matched): looked at {Candidates}", choice.DumpPath, string.Join("; ", choice.Candidates));
        }
        else
        {
            logger.LogInformation("DAC for {Dump}: {Dac}, accepted by {Rule}; looked at {Candidates}", choice.DumpPath, choice.Path, choice.Rule, string.Join("; ", choice.Candidates));
        }
    }

    /// <summary>
    /// The DAC lookup the loads use from now on, with where each value came from: the command line or the
    /// Settings page. Downloads on is a warning, as at the worker's start (ADR-017).
    /// </summary>
    private static void LogSymbols(ILogger logger, WorkerSymbols symbols) => logger.Log(
        symbols.AllowMicrosoftSymbolServer ? LogLevel.Warning : LogLevel.Information,
        "DAC lookup from here on: DAC folder {Folder} ({FolderFrom}); symbol server downloads {Downloads} ({DownloadsFrom}){Note}",
        symbols.DacFolder ?? "(none)",
        symbols.DacFolderFrom,
        symbols.AllowMicrosoftSymbolServer ? "on" : "off",
        symbols.AllowFrom,
        symbols.AllowMicrosoftSymbolServer ? "; a missing DAC may be downloaded from Microsoft's symbol server, which sends module identifiers (never dump contents)" : string.Empty);

    private sealed record Entry(ISnapshot Snapshot, SnapshotDescriptor Descriptor, IndexState Index)
    {
        public DerivedData Derived { get; } = new();
    }

    /// <summary>Tests only: a loader that stands in for the file's own, to reach what a real dump cannot be made to do.</summary>
    internal Func<string, ISnapshotLoader>? LoaderForTests
    {
        get => Opener.LoaderForTests;
        init => Opener.LoaderForTests = value;
    }

    /// <summary>Tests only: the DAC check of a cache open, which a made-up dump cannot reach.</summary>
    internal Func<string, Exception?>? DacCheckForTests
    {
        get => Opener.DacCheckForTests;
        init => Opener.DacCheckForTests = value;
    }

    /// <summary>Tests only: the process name an older cache takes from the dump, which a made-up dump cannot give.</summary>
    internal Func<string, (string? Name, string Source)?>? ProcessNameForTests
    {
        get => Opener.ProcessNameForTests;
        init => Opener.ProcessNameForTests = value;
    }

    private SnapshotOpener Opener => LazyInitializer.EnsureInitialized(ref _opener, () => new SnapshotOpener(locator, _signer, _kinds, logger, _reservations));
}
