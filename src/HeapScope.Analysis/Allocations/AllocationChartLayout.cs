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

using System.Buffers;
using HeapScope.Core.Allocations;

namespace HeapScope.Analysis.Allocations;

/// <summary>
/// The chart of an interval's call tree or back traces (ADR-046), laid out like the icicle of the dominator tree: all the sampled bytes
/// as the top, each row's children side by side below it in the tree's order, as wide as their bytes. A call tree frame's children are
/// what it called, and a back trace's its callers, so children always fit inside their parent; the gap at a parent's right is what it
/// allocated itself (or, in back traces, the stacks that start there). A segment narrower than <c>minShare</c> of the top is left out
/// with what lies below it. The keys are the tree's own (<see cref="AllocationTreeRows"/>), so a segment finds its row.
/// </summary>
public static class AllocationChartLayout
{
    /// <summary>The most segments one chart holds, as for the icicle.</summary>
    public const int MaxSegments = 20_000;

    public static AllocationChart Build(
        AllocationTrace trace,
        AllocationFilter filter,
        AllocationTreeKind kind,
        int levels,
        double minShare,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentOutOfRangeException.ThrowIfLessThan(levels, 1);
        if (minShare is < 0 or > 1 || double.IsNaN(minShare))
        {
            throw new ArgumentOutOfRangeException(nameof(minShare));
        }

        var count = trace.StackFrames.Length;
        var own = ArrayPool<long>.Shared.Rent(Math.Max(count, 1));
        var ownSamples = ArrayPool<int>.Shared.Rent(Math.Max(count, 1));
        try
        {
            Array.Clear(own, 0, count);
            Array.Clear(ownSamples, 0, count);
            AllocationProfile.Own(trace, filter, own.AsSpan(0, count), ownSamples.AsSpan(0, count), cancellationToken);
            long total = 0;
            var samples = 0;
            for (var n = 0; n < count; n++)
            {
                total += own[n];
                samples += ownSamples[n];
            }

            var layout = new Layout(trace, levels, total > 0 ? Math.Max(1, (long)Math.Ceiling(total * minShare)) : 1, cancellationToken);
            layout.Segments.Add(new AllocationChartSegment(0, 0, total, AllocationTreeRows.RootKey, -1));
            if (total > 0)
            {
                if (kind == AllocationTreeKind.CallTree)
                {
                    layout.CallTree(own, count);
                }
                else
                {
                    layout.BackTraces(own, ownSamples, count);
                }
            }

            return new AllocationChart(layout.Segments, total, samples, layout.LeftOut, layout.LeftOutBytes, layout.Truncated);
        }
        finally
        {
            ArrayPool<long>.Shared.Return(own);
            ArrayPool<int>.Shared.Return(ownSamples);
        }
    }

    /// <summary>One chart's layout: its segments so far, what it left out, and whether the cap cut it.</summary>
    private sealed class Layout(AllocationTrace trace, int levels, long minBytes, CancellationToken cancellationToken)
    {
        public List<AllocationChartSegment> Segments { get; } = [];

        public int LeftOut { get; private set; }

        public long LeftOutBytes { get; private set; }

        public bool Truncated { get; private set; }

        public void CallTree(long[] own, int count)
        {
            // A parent comes before its children (AllocationTrace checks it), so one pass from the end sums each subtree.
            var totals = ArrayPool<long>.Shared.Rent(Math.Max(count, 1));
            try
            {
                Array.Copy(own, totals, count);
                for (var n = count - 1; n >= 0; n--)
                {
                    Check(n);
                    if (trace.StackParents[n] is var parent and >= 0)
                    {
                        totals[parent] += totals[n];
                    }
                }

                // The children wide enough to draw, per parent (-1 for the outermost frames).
                var children = new Dictionary<int, List<int>>();
                for (var n = 0; n < count; n++)
                {
                    Check(n);
                    var parent = trace.StackParents[n];
                    if (totals[n] >= minBytes)
                    {
                        if (!children.TryGetValue(parent, out var list))
                        {
                            list = [];
                            children.Add(parent, list);
                        }

                        list.Add(n);
                    }
                }

                // The tree's order: bytes, then name, then node.
                Comparison<int> order = (a, b) =>
                    totals[a] != totals[b] ? totals[b].CompareTo(totals[a])
                    : string.CompareOrdinal(trace.Frames[trace.StackFrames[a]], trace.Frames[trace.StackFrames[b]]) is var byName and not 0 ? byName
                    : a.CompareTo(b);
                // The parents whose children were laid out (at most the segments drawn), for the tally of the narrow ones after.
                var parents = new HashSet<int>();
                var level = new List<(int Node, long Offset)> { (-1, 0) };
                for (var depth = 1; depth <= levels && level.Count > 0 && !Truncated; depth++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var next = new List<(int Node, long Offset)>();
                    foreach (var (node, offset) in level)
                    {
                        if (Truncated)
                        {
                            break;
                        }

                        parents.Add(node);
                        if (!children.TryGetValue(node, out var list))
                        {
                            continue;
                        }

                        list.Sort(order);
                        var x = offset;
                        foreach (var child in list)
                        {
                            if (!Add(new AllocationChartSegment(depth, x, totals[child], AllocationTreeRows.NodeKey(child), trace.StackFrames[child])))
                            {
                                // Cut: the tally below still counts what the parents laid out so far left out.
                                break;
                            }

                            next.Add((child, x));
                            x += totals[child];
                        }
                    }

                    level = next;
                }

                // What the share left out under the parents laid out: one pass, kept to a set the size of the chart rather than a
                // tally per reached node.
                for (var n = 0; n < count; n++)
                {
                    Check(n);
                    if (totals[n] > 0 && totals[n] < minBytes && parents.Contains(trace.StackParents[n]))
                    {
                        (LeftOut, LeftOutBytes) = (LeftOut + 1, LeftOutBytes + totals[n]);
                    }
                }
            }
            finally
            {
                ArrayPool<long>.Shared.Return(totals);
            }
        }

        public void BackTraces(long[] own, int[] ownSamples, int count)
        {
            var level = new List<(FrameGroup Group, string Key, long Offset)>();
            Place(BackTraceGroups.Leaves(trace, own, ownSamples, count, cancellationToken), AllocationTreeRows.RootKey, 0, 1, level);
            for (var depth = 2; depth <= levels && level.Count > 0 && !Truncated; depth++)
            {
                var parents = level;
                level = [];
                foreach (var (group, key, offset) in parents)
                {
                    Place(BackTraceGroups.Callers(trace, group, cancellationToken), key, offset, depth, level);
                }
            }
        }

        private void Place(List<FrameGroup> groups, string parentKey, long offset, int depth, List<(FrameGroup Group, string Key, long Offset)> level)
        {
            var x = offset;
            foreach (var group in BackTraceGroups.Ordered(trace, groups))
            {
                if (group.Bytes < minBytes)
                {
                    (LeftOut, LeftOutBytes) = (LeftOut + 1, LeftOutBytes + group.Bytes);
                    continue;
                }

                var key = AllocationTreeRows.BackTraceKey(parentKey, group.Frame);
                if (!Add(new AllocationChartSegment(depth, x, group.Bytes, key, group.Frame)))
                {
                    return;
                }

                level.Add((group, key, x));
                x += group.Bytes;
            }
        }

        /// <summary>Adds a segment, or says the chart is full (and marks it cut).</summary>
        private bool Add(AllocationChartSegment segment)
        {
            if (Segments.Count >= MaxSegments)
            {
                Truncated = true;
                return false;
            }

            Segments.Add(segment);
            return true;
        }

        /// <summary>A loop over every stack node checks the token every 64K of them.</summary>
        private void Check(int n)
        {
            if ((n & 0xFFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}
