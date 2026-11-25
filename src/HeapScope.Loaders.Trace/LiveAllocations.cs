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
using System.Runtime.InteropServices;
using HeapScope.Core.Allocations;

namespace HeapScope.Loaders.Trace;

/// <summary>
/// The allocations of a trace that is still being recorded, added up as they arrive (sprint 4.3, ADR-037): by stretch of time,
/// type, stack and kind. Stacks are folded by method where it is known when the sample arrives, by address where not, and the
/// addresses take their method's name once it is (<see cref="Rename"/>). Bounded: a stretch grows when there are too many of them,
/// or too many entries, and what is past the limits is added up under <see cref="OtherStacks"/> and <see cref="OtherTypes"/>,
/// counted. Not thread-safe: the reader and whoever asks take its lock.
/// </summary>
/// <param name="notice">Told what changes the meaning of figures already shown: a stretch that grew, stacks given up.</param>
internal sealed class LiveAllocations(LiveMethodNames names, LiveAllocationLimits limits, Action<string>? notice = null)
{
    /// <summary>The frame of the samples whose stack is past the limit.</summary>
    public const string OtherStacks = "[other stacks]";

    /// <summary>The type of the samples whose type is past the limit.</summary>
    public const string OtherTypes = "[other types]";

    private readonly List<string> _types = [];
    private readonly Dictionary<string, int> _typeIds = new(StringComparer.Ordinal);
    private readonly List<string> _frames = [];
    private readonly Dictionary<string, int> _frameIds = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, int> _frameOfAddress = [];
    private readonly Dictionary<int, ulong> _unnamed = [];
    private readonly Dictionary<ulong, int> _unnamedByAddress = [];
    private List<int> _stackFrames = [];
    private List<int> _stackParents = [];
    private Dictionary<(int Parent, int Frame), int> _nodes = [];
    private List<Dictionary<Key, Amount>> _buckets = [];
    private long _typeText;
    private int _entries;
    private bool _stacksFolded;
    private int _addressVersion;
    private int _renamedVersion;
    private (long Version, int Nodes, long Count) _unnamedSamples = (-1, 0, 0);

    /// <summary>Bumped by each sample.</summary>
    public long Version { get; private set; }

    /// <summary>How long one stretch of time is.</summary>
    public double BucketMs { get; private set; } = limits.BucketMs;

    public long Samples { get; private set; }

    public long Bytes { get; private set; }

    public long WithoutStack { get; private set; }

    /// <summary>Samples under <see cref="OtherStacks"/> or <see cref="OtherTypes"/>.</summary>
    public long PastLimits { get; private set; }

    public int Types => _types.Count;

    public int StackNodes => _stackFrames.Count;

    public int Frames => _frames.Count;

    public int Entries => _entries;

    /// <summary>The samples with a frame that has no name by now.</summary>
    public long Unnamed()
    {
        if (_unnamed.Count == 0 && !_frameIds.ContainsKey(AllocationTrace.UnresolvedFrame(null)))
        {
            return 0;
        }

        if (_unnamedSamples.Version == Version && _unnamedSamples.Nodes == _stackFrames.Count)
        {
            return _unnamedSamples.Count;
        }

        // A parent comes before its children.
        var shared = _frameIds.GetValueOrDefault(AllocationTrace.UnresolvedFrame(null), -1);
        var unnamed = new bool[_stackFrames.Count];
        for (var n = 0; n < unnamed.Length; n++)
        {
            unnamed[n] = _stackFrames[n] == shared || _unnamed.ContainsKey(_stackFrames[n]) || (_stackParents[n] >= 0 && unnamed[_stackParents[n]]);
        }

        var samples = 0L;
        foreach (var bucket in _buckets)
        {
            foreach (var (key, amount) in bucket)
            {
                samples += unnamed[key.Leaf] ? amount.Count : 0;
            }
        }

        _unnamedSamples = (Version, _stackFrames.Count, samples);
        return samples;
    }

    public void Add(double timeMs, string? typeName, long bytes, AllocationKind kind, ReadOnlySpan<ulong> stack)
    {
        var past = false;
        var type = Type(string.IsNullOrEmpty(typeName) ? "?" : typeName, ref past);
        var leaf = Leaf(stack, ref past);
        var bucket = BucketOf(timeMs);
        while (bucket >= limits.MaxBuckets)
        {
            Widen("the run is longer than the stretches kept");
            bucket = BucketOf(timeMs);
        }

        while (_buckets.Count <= bucket)
        {
            _buckets.Add([]);
        }

        Put(_buckets[bucket], new Key(type, leaf, kind), new Amount(bytes, 1));
        while (_entries > limits.MaxEntries && _buckets.Count > limits.FewestBuckets)
        {
            Widen("there are more entries than are kept");
        }

        if (_entries > limits.MaxEntries)
        {
            // Time is kept to a degree, so the stacks are what there is too much of.
            FoldStacks();
            past = true;
        }

        Samples++;
        Bytes += bytes;
        PastLimits += past ? 1 : 0;
        Version++;
    }

    /// <summary>What was added so far, as the queries read a trace.</summary>
    public AllocationTrace View(AllocationTraceInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var times = new double[_entries];
        var stacks = new int[_entries];
        var types = new int[_entries];
        var bytes = new long[_entries];
        var kinds = new AllocationKind[_entries];
        var counts = new int[_entries];
        var i = 0;
        for (var b = 0; b < _buckets.Count; b++)
        {
            foreach (var (key, amount) in _buckets[b])
            {
                (times[i], stacks[i], types[i], bytes[i], kinds[i], counts[i]) = (b * BucketMs, key.Leaf, key.Type, amount.Bytes, key.Kind, amount.Count);
                i++;
            }
        }

        return new AllocationTrace([.. _frames], [.. _types], [.. _stackFrames], [.. _stackParents], times, stacks, types, bytes, kinds, info, counts);
    }

    /// <summary>Addresses whose method is known by now take its name: the stacks they are in are folded again. Whether any did.</summary>
    public bool Rename()
    {
        if (_unnamed.Count == 0 || _renamedVersion == names.Version)
        {
            return false;
        }

        _renamedVersion = names.Version;
        var renamed = new Dictionary<int, int>();
        foreach (var (frame, address) in _unnamed)
        {
            if (names.Find(address) is { } name)
            {
                renamed.Add(frame, FrameId(name));
            }
        }

        if (renamed.Count == 0)
        {
            return false;
        }

        foreach (var frame in renamed.Keys)
        {
            _unnamedByAddress.Remove(_unnamed[frame]);
            _unnamed.Remove(frame);
        }

        _frameOfAddress.Clear();
        Refold((node, frames) => renamed.GetValueOrDefault(frames[node], frames[node]), keep: true);
        _unnamedSamples = (-1, 0, 0);
        return true;
    }

    private int BucketOf(double timeMs) => timeMs <= 0 || double.IsNaN(timeMs) ? 0 : (int)Math.Min(timeMs / BucketMs, int.MaxValue - 1);

    private void Put(Dictionary<Key, Amount> bucket, Key key, Amount amount)
    {
        ref var kept = ref CollectionsMarshal.GetValueRefOrAddDefault(bucket, key, out var known);
        kept = new Amount(kept.Bytes + amount.Bytes, kept.Count + amount.Count);
        _entries += known ? 0 : 1;
    }

    private int Type(string name, ref bool past)
    {
        if (_typeIds.TryGetValue(name, out var id))
        {
            return id;
        }

        if (_types.Count >= limits.MaxTypes || _typeText + name.Length > limits.MaxTypeText)
        {
            past = true;
            name = OtherTypes;
            if (_typeIds.TryGetValue(name, out id))
            {
                return id;
            }
        }

        id = _types.Count;
        _types.Add(name);
        _typeIds.Add(name, id);
        _typeText += name.Length;
        return id;
    }

    private int Leaf(ReadOnlySpan<ulong> stack, ref bool past)
    {
        if (stack.IsEmpty)
        {
            WithoutStack++;
            return Node(-1, FrameId(AllocationTrace.NoStackFrame));
        }

        if (_stacksFolded)
        {
            past = true;
            return Node(-1, FrameId(OtherStacks));
        }

        if (_addressVersion != names.Version || _frameOfAddress.Count >= limits.MaxFrames)
        {
            // A method may have taken another's place: what an address is, is asked again.
            _frameOfAddress.Clear();
            _addressVersion = names.Version;
        }

        var node = -1;
        for (var i = stack.Length - 1; i >= 0; i--)
        {
            // Past the limit only what is known already is followed: no frame is made for a stack that is not kept.
            var full = _stackFrames.Count >= limits.MaxStackNodes;
            var frame = Frame(stack[i], create: !full);
            if (full && (frame < 0 || !_nodes.ContainsKey((node, frame))))
            {
                past = true;
                return Node(-1, FrameId(OtherStacks));
            }

            node = Node(node, frame);
        }

        return node;
    }

    /// <summary>The frame of an address, or -1 when it would have to be made.</summary>
    private int Frame(ulong address, bool create)
    {
        if (_frameOfAddress.TryGetValue(address, out var frame))
        {
            return frame;
        }

        if (names.Find(address) is { } name)
        {
            if (!_frameIds.TryGetValue(name, out frame))
            {
                if (!create)
                {
                    return -1;
                }

                frame = FrameId(name);
            }
        }
        else if (!_unnamedByAddress.TryGetValue(address, out frame))
        {
            if (!create)
            {
                return -1;
            }

            frame = UnnamedFrame(address);
        }

        _frameOfAddress.Add(address, frame);
        return frame;
    }

    private int UnnamedFrame(ulong address)
    {
        if (_unnamed.Count >= limits.MaxUnnamed)
        {
            // Too many to tell apart: one frame for the rest.
            return FrameId(AllocationTrace.UnresolvedFrame(null));
        }

        // A frame of its own: merging unnamed frames would hide different methods.
        var frame = _frames.Count;
        _frames.Add(AllocationTrace.UnresolvedFrame(null));
        _unnamed.Add(frame, address);
        _unnamedByAddress.Add(address, frame);
        return frame;
    }

    private int FrameId(string name)
    {
        if (!_frameIds.TryGetValue(name, out var id))
        {
            id = _frames.Count;
            _frames.Add(name);
            _frameIds.Add(name, id);
        }

        return id;
    }

    private int Node(int parent, int frame)
    {
        ref var node = ref CollectionsMarshal.GetValueRefOrAddDefault(_nodes, (parent, frame), out var known);
        if (!known)
        {
            node = _stackFrames.Count;
            _stackFrames.Add(frame);
            _stackParents.Add(parent);
        }

        return node;
    }

    /// <summary>Every stack becomes <see cref="OtherStacks"/>: the entries left are types per stretch.</summary>
    private void FoldStacks()
    {
        var other = FrameId(OtherStacks);
        _stacksFolded = true;
        PastLimits = Samples;
        var entries = _entries;
        _unnamed.Clear();
        _unnamedByAddress.Clear();
        _frameOfAddress.Clear();
        Refold((_, _) => other, keep: false);
        notice?.Invoke(string.Create(CultureInfo.InvariantCulture, $"the call stacks of {Samples} samples were given up, {entries} entries became {_entries}"));
    }

    /// <summary>Builds the tree of stacks again, each node's frame as <paramref name="frameOf"/> says; without <paramref name="keep"/> every node becomes a root.</summary>
    private void Refold(Func<int, List<int>, int> frameOf, bool keep)
    {
        var (frames, parents, buckets) = (_stackFrames, _stackParents, _buckets);
        (_stackFrames, _stackParents, _nodes) = ([], [], []);
        var moved = new int[frames.Count];
        for (var n = 0; n < frames.Count; n++)
        {
            moved[n] = Node(keep && parents[n] >= 0 ? moved[parents[n]] : -1, frameOf(n, frames));
        }

        _entries = 0;
        for (var b = 0; b < buckets.Count; b++)
        {
            // One at a time: the old ones are let go as the new ones are made.
            var bucket = buckets[b];
            var again = new Dictionary<Key, Amount>(bucket.Count);
            foreach (var (key, amount) in bucket)
            {
                Put(again, key with { Leaf = moved[key.Leaf] }, amount);
            }

            buckets[b] = again;
        }
    }

    /// <summary>Two stretches become one.</summary>
    private void Widen(string why)
    {
        BucketMs *= 2;
        var entries = _entries;
        var wide = new List<Dictionary<Key, Amount>>((_buckets.Count + 1) / 2);
        _entries = 0;
        for (var b = 0; b < _buckets.Count; b += 2)
        {
            var into = _buckets[b];
            _entries += into.Count;
            if (b + 1 < _buckets.Count)
            {
                foreach (var (key, amount) in _buckets[b + 1])
                {
                    Put(into, key, amount);
                }
            }

            wide.Add(into);
        }

        _buckets = wide;
        notice?.Invoke(string.Create(CultureInfo.InvariantCulture, $"stretches are {BucketMs:N0} ms long from now on: {why} ({Samples} samples, {entries} entries became {_entries} in {_buckets.Count} stretches)"));
    }

    private readonly record struct Key(int Type, int Leaf, AllocationKind Kind);

    private readonly record struct Amount(long Bytes, int Count);
}
