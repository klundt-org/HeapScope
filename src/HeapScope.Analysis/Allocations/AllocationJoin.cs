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


using System.Runtime.InteropServices;
using System.Text;
using HeapScope.Core.Allocations;
using HeapScope.Core.Graph;

namespace HeapScope.Analysis.Allocations;

/// <summary>
/// Joins the sampled allocations of a trace to the types of two snapshots of the same process (sprint 4.4, ADR-028): by the
/// tick's method table to a full dump's types, where the name there is the tick's (an unloaded type's address may be another's later),
/// and by name to a gcdump's, which has no method tables and records names as the runtime's events do. Only a name that the whole
/// trace has for one method table is joined by name: of two types of one name a collection may leave objects of one. The caller
/// vouches that trace and snapshots are of one process.
/// </summary>
public sealed class AllocationJoin
{
    private const int CheckEvery = 0xFFFF;
    private const int Kept = 5;

    private AllocationJoin(AllocationJoinTotals totals, long[] baselineBytes, int[] baselineSamples, string?[] baselineNames, long[] currentBytes, int[] currentSamples, string?[] currentNames)
    {
        Totals = totals;
        BaselineBytes = baselineBytes;
        BaselineSamples = baselineSamples;
        BaselineNames = baselineNames;
        CurrentBytes = currentBytes;
        CurrentSamples = currentSamples;
        CurrentNames = currentNames;
    }

    public AllocationJoinTotals Totals { get; }

    /// <summary>Sampled bytes per type index of the baseline: types the current snapshot has no object of. A sample is in one of the two arrays.</summary>
    public long[] BaselineBytes { get; }

    public int[] BaselineSamples { get; }

    /// <summary>The name the trace has for each joined type of the baseline, which the trace's own queries take.</summary>
    public string?[] BaselineNames { get; }

    /// <summary>Sampled bytes per type index of the current snapshot.</summary>
    public long[] CurrentBytes { get; }

    public int[] CurrentSamples { get; }

    /// <summary>As <see cref="BaselineNames"/>, of the current snapshot.</summary>
    public string?[] CurrentNames { get; }

    /// <summary>The types allocated in an interval, by name and method table, most bytes first.</summary>
    public static IReadOnlyList<AllocatedType> Types(AllocationTrace trace, AllocationFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trace);
        var (start, end) = AllocationProfile.SampleRange(trace, filter);
        var totals = new Dictionary<long, (long Bytes, int Samples)>();
        for (var i = start; i < end; i++)
        {
            ref var total = ref CollectionsMarshal.GetValueRefOrAddDefault(totals, Key(trace, i), out _);
            total = (total.Bytes + trace.SampleBytes[i], total.Samples + trace.CountOf(i));
            Check(i, cancellationToken);
        }

        return
        [
            .. totals.Select(t => new AllocatedType(trace.Types[(int)(t.Key >> 32)], TableOf(trace, (int)t.Key), t.Value.Bytes, t.Value.Samples))
                .OrderByDescending(t => t.Bytes).ThenBy(t => t.Name, StringComparer.Ordinal).ThenBy(t => t.MethodTable),
        ];
    }

    /// <summary>The names, in the form they are compared in, that the whole trace has for more than one method table.</summary>
    public static IReadOnlySet<string> SharedNames(AllocationTrace trace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trace);
        var seen = new HashSet<long>();
        for (var i = 0; i < trace.SampleCount; i++)
        {
            seen.Add(Key(trace, i));
            Check(i, cancellationToken);
        }

        var forms = new Dictionary<int, string>();
        return seen.GroupBy(key =>
            {
                var type = (int)(key >> 32);
                if (!forms.TryGetValue(type, out var form))
                {
                    forms.Add(type, form = Form(trace.Types[type]));
                }

                return form;
            })
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Joins <paramref name="allocated"/> to the types that have objects in either snapshot.</summary>
    /// <param name="sharedNames">The trace's names that several types share (<see cref="SharedNames"/>).</param>
    public static AllocationJoin Join(
        IReadOnlyList<AllocatedType> allocated,
        IReadOnlySet<string> sharedNames,
        HeapGraph baselineGraph,
        TypeAggregation baseline,
        HeapGraph currentGraph,
        TypeAggregation current,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allocated);
        ArgumentNullException.ThrowIfNull(sharedNames);
        ArgumentNullException.ThrowIfNull(baselineGraph);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(currentGraph);
        ArgumentNullException.ThrowIfNull(current);
        var (currentTables, baselineTables) = (new Dictionary<ulong, int>(), new Dictionary<ulong, int>());
        var names = new Dictionary<string, Target>(StringComparer.Ordinal);

        // Which side's type index holds a joined name does not matter to a row both sides have.
        var unnamed = Index(currentGraph, current, isCurrent: true, currentTables, names, cancellationToken)
            + Index(baselineGraph, baseline, isCurrent: false, baselineTables, names, cancellationToken);

        var baselineBytes = new long[baseline.TypeCount];
        var baselineSamples = new int[baseline.TypeCount];
        var baselineNames = new string?[baseline.TypeCount];
        var currentBytes = new long[current.TypeCount];
        var currentSamples = new int[current.TypeCount];
        var currentNames = new string?[current.TypeCount];
        long total = 0, byTable = 0, byName = 0;
        var samples = 0;
        var (absent, shared, other) = (new List<AllocatedType>(), new List<AllocatedType>(), new List<(AllocatedType Type, string Snapshot)>());
        for (var i = 0; i < allocated.Count; i++)
        {
            Check(i, cancellationToken);
            var type = allocated[i];
            total += type.Bytes;
            samples += type.Samples;
            var form = Form(type.Name);
            Target target;
            if (type.MethodTable != 0 && (currentTables.ContainsKey(type.MethodTable) || baselineTables.ContainsKey(type.MethodTable)))
            {
                var (inCurrent, inBaseline) = (At(currentGraph, currentTables, type.MethodTable), At(baselineGraph, baselineTables, type.MethodTable));
                var (asCurrent, asBaseline) = (Match(inCurrent?.Name, form), Match(inBaseline?.Name, form));
                if (asCurrent == NameMatch.None && asBaseline == NameMatch.None)
                {
                    other.Add((type, (inCurrent ?? inBaseline)!.Value.Name));
                    absent.Add(type);
                    continue;
                }

                target = asCurrent >= asBaseline ? new Target(true, inCurrent!.Value.Type, string.Empty, Shared: false) : new Target(false, inBaseline!.Value.Type, string.Empty, Shared: false);
                byTable += type.Bytes;
            }
            else if (names.TryGetValue(form, out target))
            {
                if (target.Shared || sharedNames.Contains(form))
                {
                    shared.Add(type);
                    continue;
                }

                byName += type.Bytes;
            }
            else
            {
                absent.Add(type);
                continue;
            }

            (target.IsCurrent ? currentBytes : baselineBytes)[target.Type] += type.Bytes;
            (target.IsCurrent ? currentSamples : baselineSamples)[target.Type] += type.Samples;
            (target.IsCurrent ? currentNames : baselineNames)[target.Type] ??= type.Name;
        }

        return new AllocationJoin(
            new AllocationJoinTotals(
                total,
                samples,
                byTable,
                byName,
                shared.Sum(t => t.Bytes),
                absent.Sum(t => t.Bytes),
                absent.Count,
                other.Sum(t => t.Type.Bytes),
                unnamed,
                [.. absent.OrderByDescending(t => t.Bytes).Take(Kept)],
                [.. shared.OrderByDescending(t => t.Bytes).Take(Kept)],
                [.. other.OrderByDescending(t => t.Type.Bytes).Take(Kept)]),
            baselineBytes,
            baselineSamples,
            baselineNames,
            currentBytes,
            currentSamples,
            currentNames);
    }

    private static (int Type, string Name)? At(HeapGraph graph, Dictionary<ulong, int> tables, ulong methodTable) =>
        tables.TryGetValue(methodTable, out var type) ? (type, graph.Types.GetName(type)) : null;

    // Whether the snapshot's type at the tick's method table is the tick's: by its name, or by the name without its generic arguments,
    // which the runtime's events and a dump order differently where a declaring type has some. An unnamed type is taken for it last.
    private static NameMatch Match(string? name, string form) =>
        name is null ? NameMatch.None
        : name.StartsWith("<unknown", StringComparison.Ordinal) ? NameMatch.Unnamed
        : TypeNameForms.AsGcDumpName(name) is var inSnapshot && inSnapshot == form ? NameMatch.Exact
        : WithoutArguments(inSnapshot) == WithoutArguments(form) ? NameMatch.WithoutArguments
        : NameMatch.None;

    /// <summary>A name as the trace records it, in the one form both are compared in.</summary>
    private static string Form(string recorded) => TypeNameForms.AsGcDumpName(TypeNameForms.FromRuntimeEvent(recorded));

    /// <summary>A name without its generic argument lists (an angle bracket after a letter, a digit or an underscore opens one).</summary>
    internal static string WithoutArguments(string name)
    {
        if (!name.Contains('<', StringComparison.Ordinal))
        {
            return name;
        }

        var text = new StringBuilder(name.Length);
        var depth = 0;
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '<' && (depth > 0 || (i > 0 && (char.IsLetterOrDigit(name[i - 1]) || name[i - 1] == '_'))))
            {
                depth++;
            }
            else if (c == '>' && depth > 0)
            {
                depth--;
            }
            else if (depth == 0)
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    private static long Key(AllocationTrace trace, int sample) => ((long)trace.SampleTypes[sample] << 32) | (uint)(trace.MethodTables?.Samples[sample] ?? -1);

    private static ulong TableOf(AllocationTrace trace, int table) => table < 0 ? 0 : trace.MethodTables!.Distinct[table];

    private static void Check(int i, CancellationToken cancellationToken)
    {
        if ((i & CheckEvery) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    // Types with a method table are found by it alone: the dump would have the type under the sample's table, so a name that
    // matches another table's type is another type (two nested types of one name). Returns the types without a name.
    private static int Index(HeapGraph graph, TypeAggregation aggregation, bool isCurrent, Dictionary<ulong, int> tables, Dictionary<string, Target> names, CancellationToken cancellationToken)
    {
        var methodTables = graph.Types.MethodTables.Span;
        var unnamed = 0;
        for (var t = 0; t < aggregation.TypeCount; t++)
        {
            Check(t, cancellationToken);
            if (aggregation.Counts[t] == 0)
            {
                continue;
            }

            if (methodTables[t] != 0)
            {
                tables.TryAdd(methodTables[t], t);
                continue;
            }

            var module = TypeGroups.ModuleKey(graph.Types.GetModule(t));
            var name = TypeNameForms.AsGcDumpName(graph.Types.GetName(t));
            if (name.StartsWith("UNKNOWN 0x", StringComparison.Ordinal) || name.StartsWith("<unknown", StringComparison.Ordinal))
            {
                unnamed++;
            }
            else if (!names.TryAdd(name, new Target(isCurrent, t, module, Shared: false))
                && names[name] is { Shared: false } known && !string.Equals(known.Module, module, StringComparison.OrdinalIgnoreCase))
            {
                names[name] = known with { Shared = true };
            }
        }

        return unnamed;
    }

    private enum NameMatch
    {
        None,
        Unnamed,
        WithoutArguments,
        Exact,
    }

    private readonly record struct Target(bool IsCurrent, int Type, string Module, bool Shared);
}
