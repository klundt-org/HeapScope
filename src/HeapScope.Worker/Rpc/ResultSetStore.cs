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
/// A set names the snapshot it came from and, for a view of one tab, the tab shown when it was added; once the client says which
/// snapshot and tab its window shows (<see cref="Show"/>), the other snapshots' sets are evicted first, then the shown snapshot's
/// other tabs', so the tab on screen and what its snapshot's tabs share keep their rows while hidden ones give way (ADR-044,
/// ADR-045).
/// </summary>
internal sealed class ResultSetStore
{
    // Room for several workspaces' sets (an inspections run alone holds about 20); tabs add sets a hidden tab runs again when
    // shown, and the byte budget guards the memory (ADR-044, ADR-045).
    public const int DefaultCapacity = 128;
    public const long DefaultByteBudget = 256L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, LinkedListNode<StoredResultSet>> _sets = [];
    private readonly LinkedList<StoredResultSet> _recency = new();
    private readonly ILogger? _logger;
    private long _bytes;
    private Guid? _shown;
    private Guid? _shownScope;

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

    /// <summary>The tab of the shown snapshot the client shows (ADR-045); null when it said none.</summary>
    public Guid? ShownScope
    {
        get
        {
            lock (_gate)
            {
                return _shownScope;
            }
        }
    }

    /// <summary>
    /// The client shows <paramref name="snapshot"/> now (null: none), in its tab <paramref name="scope"/>: from here on the other
    /// snapshots' sets go first, then the shown snapshot's other tabs'. Only the shown tab queries a tab's views, so a new set of
    /// the shown snapshot is that tab's, unless it is one the tabs share (<see cref="Add"/> with perTab false).
    /// </summary>
    public void Show(Guid? snapshot, Guid? scope = null)
    {
        lock (_gate)
        {
            _shown = snapshot;
            _shownScope = snapshot is null ? null : scope;
        }
    }

    /// <summary>
    /// Stores a source of <paramref name="snapshot"/> (null for a set of no snapshot: a trace, a synthetic graph) and returns
    /// the descriptor clients page through. The newest set is always kept, even when it alone exceeds the byte budget. Older
    /// sets are evicted least recently used first, those of a snapshot not shown before any other, then the shown snapshot's
    /// other tabs'.
    /// </summary>
    /// <param name="perTab">
    /// Whether the set belongs to the tab shown (a view of one tab). False for what a snapshot's tabs share, its inspections
    /// (ADR-045): such a set stays with the snapshot, never counted as another tab's.
    /// </param>
    public ResultSetInfo Add(Guid? snapshot, IResultSource source, IDisposable? owner = null, bool perTab = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        var evicted = new List<(StoredResultSet Set, string Rule)>();
        Guid? shown;
        Guid? shownScope;
        int count;
        long bytes;
        StoredResultSet set;
        lock (_gate)
        {
            (shown, shownScope) = (_shown, _shownScope);
            set = new StoredResultSet(Guid.NewGuid(), snapshot, perTab && snapshot is not null && snapshot == shown ? shownScope : null, source, owner);
            _sets.Add(set.Id, _recency.AddFirst(set));
            _bytes += source.EstimatedBytes;
            while (_sets.Count > 1 && (_sets.Count > Capacity || _bytes > ByteBudget))
            {
                var (victim, rule) = NextToEvictLocked();
                RemoveLocked(victim.Id);
                evicted.Add((victim, rule));
            }

            (count, bytes) = (_sets.Count, _bytes);
        }

        foreach (var (old, rule) in evicted)
        {
            // A hidden snapshot's or another tab's set is meant to go first (Debug); one the window shows, or with none said, is
            // what a "my grid ran again" question turns on (Information).
            var level = rule is OtherSnapshot or OtherTab ? LogLevel.Debug : LogLevel.Information;
            _logger?.Log(
                level,
                "Result set {Id} of snapshot {Snapshot}, tab {Scope}, evicted as {Rule} ({Bytes} bytes; the client shows {Shown}, tab {ShownScope}; {Count}/{Capacity} sets, {Total}/{Budget} bytes); paging it runs its query again",
                old.Id,
                old.Snapshot?.ToString() ?? "none",
                old.Scope?.ToString() ?? "none",
                rule,
                old.Source.EstimatedBytes,
                shown?.ToString() ?? "none",
                shownScope?.ToString() ?? "none",
                count,
                Capacity,
                bytes,
                ByteBudget);
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

    /// <summary>
    /// Drops every set of a closed snapshot, so none keeps its graph alive through its lease until evicted; owners are
    /// disposed as on a release. Returns how many went.
    /// </summary>
    public int ReleaseSnapshot(Guid snapshot)
    {
        var removed = new List<StoredResultSet>();
        lock (_gate)
        {
            foreach (var set in _recency.Where(s => s.Snapshot == snapshot).ToList())
            {
                RemoveLocked(set.Id);
                removed.Add(set);
            }
        }

        foreach (var set in removed)
        {
            set.DisposeOwnerIfIdle();
        }

        return removed.Count;
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

    private const string OtherSnapshot = "another snapshot's";
    private const string OtherTab = "another tab's";
    private const string LeastRecentlyUsed = "the least recently used";

    /// <summary>
    /// The least recently used set of a snapshot not shown, else of another tab of the shown snapshot (a set with a tab, not the
    /// one shown: what the tabs share has none), else the least recently used set; never the newest one. With no snapshot shown
    /// it is plain least recently used, as before the client said anything.
    /// </summary>
    private (StoredResultSet Set, string Rule) NextToEvictLocked()
    {
        if (_shown is { } shown)
        {
            if (Oldest(set => set.Snapshot is { } snapshot && snapshot != shown) is { } otherSnapshot)
            {
                return (otherSnapshot, OtherSnapshot);
            }

            if (_shownScope is { } scope && Oldest(set => set.Snapshot == shown && set.Scope is { } tab && tab != scope) is { } otherTab)
            {
                return (otherTab, OtherTab);
            }
        }

        return (_recency.Last!.Value, LeastRecentlyUsed);
    }

    private StoredResultSet? Oldest(Func<StoredResultSet, bool> match)
    {
        for (var node = _recency.Last; node is not null && node != _recency.First; node = node.Previous)
        {
            if (match(node.Value))
            {
                return node.Value;
            }
        }

        return null;
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
    private sealed class StoredResultSet(Guid id, Guid? snapshot, Guid? scope, IResultSource source, IDisposable? owner)
    {
        public Guid Id { get; } = id;

        public Guid? Snapshot { get; } = snapshot;

        /// <summary>The tab of the shown snapshot that added it; null when none was said.</summary>
        public Guid? Scope { get; } = scope;

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
