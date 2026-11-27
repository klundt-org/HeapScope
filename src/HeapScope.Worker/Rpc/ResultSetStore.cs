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

using HeapScope.Contracts;
using Microsoft.Extensions.Logging;

namespace HeapScope.Worker.Rpc;

/// <summary>
/// Result sets held by the worker for paged retrieval (ARCHITECTURE.md 4.6). Sets are typed sources, formatted
/// per page. Least recently used sets are evicted when either the set count or the estimated byte budget is
/// exceeded, so a client that never releases cannot grow the worker without bound. A set may carry an owner
/// (for example a snapshot lease) that is disposed when the set is released or evicted, or, when a page is being
/// formatted at that moment, after that page: sources read the graph and the dump while formatting.
/// A set names the snapshot it came from; once the client says which snapshot its window shows (<see cref="Show"/>), the
/// sets of the others are evicted first, so the workspace on screen keeps its rows while hidden ones give way (ADR-044).
/// </summary>
internal sealed class ResultSetStore
{
    public const int DefaultCapacity = 32;
    public const long DefaultByteBudget = 256L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, LinkedListNode<StoredResultSet>> _sets = [];
    private readonly LinkedList<StoredResultSet> _recency = new();
    private readonly ILogger? _logger;
    private long _bytes;
    private Guid? _shown;

    public ResultSetStore(int capacity = DefaultCapacity, long byteBudget = DefaultByteBudget, ILogger? logger = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(byteBudget, 1);
        Capacity = capacity;
        ByteBudget = byteBudget;
        _logger = logger;
    }

    /// <summary>The snapshot the client shows, whose sets are evicted last; null when it shows none or never said.</summary>
    public Guid? ShownSnapshot
    {
        get
        {
            lock (_gate)
            {
                return _shown;
            }
        }
    }

    public int Capacity { get; }

    public long ByteBudget { get; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _sets.Count;
            }
        }
    }

    public long EstimatedBytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    /// <summary>The client shows <paramref name="snapshot"/> now (null: none): from here on the other snapshots' sets go first.</summary>
    public void Show(Guid? snapshot)
    {
        lock (_gate)
        {
            _shown = snapshot;
        }
    }

    /// <summary>
    /// Stores a source of <paramref name="snapshot"/> (null for a set of no snapshot: a trace, a synthetic graph) and returns
    /// the descriptor clients page through. The newest set is always kept, even when it alone exceeds the byte budget. Older
    /// sets are evicted least recently used first, those of a snapshot not shown before any other.
    /// </summary>
    public ResultSetInfo Add(Guid? snapshot, IResultSource source, IDisposable? owner = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var set = new StoredResultSet(Guid.NewGuid(), snapshot, source, owner);
        var evicted = new List<StoredResultSet>();
        Guid? shown;
        lock (_gate)
        {
            shown = _shown;
            _sets.Add(set.Id, _recency.AddFirst(set));
            _bytes += source.EstimatedBytes;
            while (_sets.Count > 1 && (_sets.Count > Capacity || _bytes > ByteBudget))
            {
                var victim = NextToEvictLocked();
                RemoveLocked(victim.Id);
                evicted.Add(victim);
            }
        }

        foreach (var old in evicted)
        {
            _logger?.LogDebug(
                "Result set {Id} of snapshot {Snapshot} evicted ({Bytes} bytes; the client shows {Shown}); paging it runs its query again",
                old.Id,
                old.Snapshot?.ToString() ?? "none",
                old.Source.EstimatedBytes,
                shown?.ToString() ?? "none");
            old.DisposeOwnerIfIdle();
        }

        return new ResultSetInfo(set.Id, source.Count, source.Columns);
    }

    /// <summary>Returns a page (at most <see cref="WorkerProtocol.MaxPageSize"/> rows), or null when the set is gone.</summary>
    public ResultPage? TryGetPage(Guid id, int offset, int count)
    {
        StoredResultSet set;
        lock (_gate)
        {
            if (!_sets.TryGetValue(id, out var node))
            {
                return null;
            }

            _recency.Remove(node);
            _recency.AddFirst(node);
            set = node.Value;
            set.Readers++;
        }

        try
        {
            return FormatPage(set, offset, count);
        }
        finally
        {
            bool last;
            lock (_gate)
            {
                last = --set.Readers == 0 && set.Removed;
            }

            if (last)
            {
                set.Owner?.Dispose();
            }
        }
    }

    public void Release(Guid id)
    {
        StoredResultSet? removed;
        lock (_gate)
        {
            removed = RemoveLocked(id);
        }

        removed?.DisposeOwnerIfIdle();
    }

    private static ResultPage FormatPage(StoredResultSet set, int offset, int count)
    {
        var total = set.Source.Count;
        var start = Math.Min(offset, total);
        var length = Math.Min(Math.Min(count, WorkerProtocol.MaxPageSize), total - start);
        var columns = set.Source.Columns.Count;
        var rows = new IReadOnlyList<string?>[length];
        for (var i = 0; i < length; i++)
        {
            var cells = new string?[columns];
            set.Source.FormatRow(start + i, cells);
            rows[i] = cells;
        }

        return new ResultPage(set.Id, start, total, rows);
    }

    /// <summary>
    /// The least recently used set of a snapshot not shown, else the least recently used set; never the newest one. With no
    /// snapshot shown it is plain least recently used, as before the client said anything.
    /// </summary>
    private StoredResultSet NextToEvictLocked()
    {
        if (_shown is { } shown)
        {
            for (var node = _recency.Last; node is not null && node != _recency.First; node = node.Previous)
            {
                if (node.Value.Snapshot is { } snapshot && snapshot != shown)
                {
                    return node.Value;
                }
            }
        }

        return _recency.Last!.Value;
    }

    private StoredResultSet? RemoveLocked(Guid id)
    {
        if (!_sets.Remove(id, out var node))
        {
            return null;
        }

        _recency.Remove(node);
        _bytes -= node.Value.Source.EstimatedBytes;
        node.Value.Removed = true;
        node.Value.IdleWhenRemoved = node.Value.Readers == 0;
        return node.Value;
    }

    /// <summary>A stored set and the pages being formatted from it (<see cref="Readers"/>, guarded by the store's lock).</summary>
    private sealed class StoredResultSet(Guid id, Guid? snapshot, IResultSource source, IDisposable? owner)
    {
        public Guid Id { get; } = id;

        public Guid? Snapshot { get; } = snapshot;

        public IResultSource Source { get; } = source;

        public IDisposable? Owner { get; } = owner;

        public int Readers { get; set; }

        public bool Removed { get; set; }

        /// <summary>No page was being formatted when the set was removed: the remover disposes the owner, else the last reader does.</summary>
        public bool IdleWhenRemoved { get; set; }

        public void DisposeOwnerIfIdle()
        {
            if (IdleWhenRemoved)
            {
                Owner?.Dispose();
            }
        }
    }
}
