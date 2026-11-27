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

using HeapScope.Core.Allocations;

namespace HeapScope.Analysis.Allocations;

/// <summary>A place in the stack tree that a back-trace row stands for, with the bytes and samples allocated below it (16 bytes: the long first).</summary>
internal readonly record struct StackPosition(long Bytes, int Node, int Samples);

/// <summary>A back-trace row: the positions it stands for, grouped by their frame.</summary>
internal sealed class FrameGroup(int frame)
{
    public int Frame { get; } = frame;

    public long Bytes { get; set; }

    public int Samples { get; set; }

    public List<StackPosition> Positions { get; } = [];
}

/// <summary>
/// The back traces' grouping, shared by the tree's rows and the chart: at the first level the nodes that allocated, one level down
/// their callers, and so on, each grouped by frame and carrying the bytes of the samples that allocated below it.
/// </summary>
internal static class BackTraceGroups
{
    /// <summary>The first level: the nodes that allocated, by frame.</summary>
    public static List<FrameGroup> Leaves(AllocationTrace trace, long[] own, int[] ownSamples, int count, CancellationToken cancellationToken)
    {
        var leaves = new List<StackPosition>();
        for (var n = 0; n < count; n++)
        {
            if ((n & 0xFFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (ownSamples[n] > 0)
            {
                leaves.Add(new StackPosition(own[n], n, ownSamples[n]));
            }
        }

        // Each allocating node is one position, so none merges: no lookup of the nodes seen.
        return Group(trace, leaves, static p => p.Node, distinct: true, cancellationToken);
    }

    /// <summary>The callers of a back-trace row, grouped by frame (stacks that start at this row's frame end here).</summary>
    public static List<FrameGroup> Callers(AllocationTrace trace, FrameGroup group, CancellationToken cancellationToken)
    {
        var callers = new List<StackPosition>(group.Positions.Count);
        foreach (var position in group.Positions)
        {
            if (trace.StackParents[position.Node] >= 0)
            {
                callers.Add(position);
            }
        }

        return Group(trace, callers, p => trace.StackParents[p.Node], distinct: false, cancellationToken);
    }

    /// <summary>The order the tree lists them in, and the chart draws them in: bytes, then name, then frame.</summary>
    public static FrameGroup[] Ordered(AllocationTrace trace, List<FrameGroup> groups) =>
        [.. groups.OrderByDescending(g => g.Bytes).ThenBy(g => trace.Frames[g.Frame], StringComparer.Ordinal).ThenBy(g => g.Frame)];

    private static List<FrameGroup> Group(AllocationTrace trace, List<StackPosition> positions, Func<StackPosition, int> next, bool distinct, CancellationToken cancellationToken)
    {
        var groups = new Dictionary<int, FrameGroup>();
        var nodes = distinct ? null : new Dictionary<int, int>();
        for (var i = 0; i < positions.Count; i++)
        {
            if ((i & 0xFFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var position = positions[i];
            var node = next(position);
            var frame = trace.StackFrames[node];
            if (!groups.TryGetValue(frame, out var group))
            {
                group = new FrameGroup(frame);
                groups.Add(frame, group);
            }

            // Positions of one node merge, so a group never holds more positions than the tree has nodes.
            if (nodes is not null && nodes.TryGetValue(node, out var at) && group.Positions[at].Node == node)
            {
                var merged = group.Positions[at];
                group.Positions[at] = merged with { Bytes = merged.Bytes + position.Bytes, Samples = merged.Samples + position.Samples };
            }
            else
            {
                if (nodes is not null)
                {
                    nodes[node] = group.Positions.Count;
                }

                group.Positions.Add(new StackPosition(position.Bytes, node, position.Samples));
            }

            group.Bytes += position.Bytes;
            group.Samples += position.Samples;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return [.. groups.Values];
    }
}
