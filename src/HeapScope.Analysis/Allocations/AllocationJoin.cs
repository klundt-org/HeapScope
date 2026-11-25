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
using HeapScope.Core.Graph;

namespace HeapScope.Analysis.Allocations;

/// <summary>Sampled allocations of one type of an interval, as the trace tells types apart: by name and method table.</summary>
/// <param name="MethodTable">The tick's type id, or 0 when the trace has none.</param>
public readonly record struct AllocatedType(string Name, ulong MethodTable, long Bytes, int Samples);

/// <summary>How an interval's sampled bytes met a diff's types (<see cref="AllocationJoin.Join"/>), for the result and the log.</summary>
/// <param name="ByMethodTable">Bytes joined by method table, to a full dump's types.</param>
/// <param name="ByName">Bytes joined by name, to a gcdump's types.</param>
/// <param name="SharedName">Bytes of names that types of several modules share in a gcdump: no row takes them.</param>
/// <param name="Absent">Bytes of types without an object in either snapshot.</param>
/// <param name="OtherNames">A method table whose names differ in the trace and the dump (joined all the same), for the log.</param>
public sealed record AllocationJoinTotals(long Bytes, int Samples, long ByMethodTable, long ByName, long SharedName, long Absent, int AbsentTypes, int OtherNames, IReadOnlyList<AllocatedType> TopAbsent);

/// <summary>
/// Joins the sampled allocations of a trace to the types of two snapshots of the same process (sprint 4.4, ADR-028): by the
/// tick's method table to a full dump's types, which is exact while the process runs, and by name to a gcdump's, which has no
/// method tables and records names as the runtime's events do. The caller vouches that trace and snapshots are of one process.
/// </summary>
public sealed class AllocationJoin
{
    private const int CheckEvery = 0xFFFF;
    private const int KeptAbsent = 5;

    private AllocationJoin(AllocationJoinTotals totals, long[] baselineBytes, int[] baselineSamples, long[] currentBytes, int[] currentSamples)
    {
        Totals = totals;
        BaselineBytes = baselineBytes;
        BaselineSamples = baselineSamples;
        CurrentBytes = currentBytes;
        CurrentSamples = currentSamples;
    }

    public AllocationJoinTotals Totals { get; }

    /// <summary>Sampled bytes per type index of the baseline: types the current snapshot has no object of. A sample is in one of the two arrays.</summary>
    public long[] BaselineBytes { get; }

    public int[] BaselineSamples { get; }

    /// <summary>Sampled bytes per type index of the current snapshot.</summary>
    public long[] CurrentBytes { get; }

    public int[] CurrentSamples { get; }

    /// <summary>The types allocated in an interval, by name and method table, most bytes first.</summary>
    public static IReadOnlyList<AllocatedType> Types(AllocationTrace trace, AllocationFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trace);
        var (start, end) = AllocationProfile.SampleRange(trace, filter);
        var totals = new Dictionary<(int Type, int Table), (long Bytes, int Samples)>();
        for (var i = start; i < end; i++)
        {
            var key = (trace.SampleTypes[i], trace.MethodTables?.Samples[i] ?? -1);
            var (bytes, samples) = totals.GetValueOrDefault(key);
            totals[key] = (bytes + trace.SampleBytes[i], samples + trace.CountOf(i));
            if ((i & CheckEvery) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        return
        [
            .. totals.Select(t => new AllocatedType(trace.Types[t.Key.Type], t.Key.Table < 0 ? 0 : trace.MethodTables!.Distinct[t.Key.Table], t.Value.Bytes, t.Value.Samples))
                .OrderByDescending(t => t.Bytes).ThenBy(t => t.Name, StringComparer.Ordinal).ThenBy(t => t.MethodTable),
        ];
    }

    /// <summary>Joins <paramref name="allocated"/> to the types that have objects in either snapshot.</summary>
    public static AllocationJoin Join(
        IReadOnlyList<AllocatedType> allocated,
        HeapGraph baselineGraph,
        TypeAggregation baseline,
        HeapGraph currentGraph,
        TypeAggregation current,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allocated);
        ArgumentNullException.ThrowIfNull(baselineGraph);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(currentGraph);
        ArgumentNullException.ThrowIfNull(current);
        var tables = new Dictionary<ulong, Target>();
        var names = new Dictionary<string, Target>(StringComparer.Ordinal);

        // The current snapshot first: a type in both is its row all the same, but a name is joined to one type only.
        Index(currentGraph, current, isCurrent: true, tables, names);
        cancellationToken.ThrowIfCancellationRequested();
        Index(baselineGraph, baseline, isCurrent: false, tables, names);
        cancellationToken.ThrowIfCancellationRequested();

        var baselineBytes = new long[baseline.TypeCount];
        var baselineSamples = new int[baseline.TypeCount];
        var currentBytes = new long[current.TypeCount];
        var currentSamples = new int[current.TypeCount];
        long total = 0, byTable = 0, byName = 0, shared = 0, absent = 0;
        var samples = 0;
        var otherNames = 0;
        var absentTypes = new List<AllocatedType>();
        foreach (var type in allocated)
        {
            total += type.Bytes;
            samples += type.Samples;
            if (type.MethodTable != 0 && tables.TryGetValue(type.MethodTable, out var target))
            {
                byTable += type.Bytes;
                if (Form(type.Name) != TypeNameForms.AsGcDumpName((target.IsCurrent ? currentGraph : baselineGraph).Types.GetName(target.Type)))
                {
                    otherNames++;
                }
            }
            else if (names.TryGetValue(Form(type.Name), out target))
            {
                if (target.Shared)
                {
                    shared += type.Bytes;
                    continue;
                }

                byName += type.Bytes;
            }
            else
            {
                absent += type.Bytes;
                absentTypes.Add(type);
                continue;
            }

            (target.IsCurrent ? currentBytes : baselineBytes)[target.Type] += type.Bytes;
            (target.IsCurrent ? currentSamples : baselineSamples)[target.Type] += type.Samples;
        }

        return new AllocationJoin(
            new AllocationJoinTotals(total, samples, byTable, byName, shared, absent, absentTypes.Count, otherNames, [.. absentTypes.OrderByDescending(t => t.Bytes).Take(KeptAbsent)]),
            baselineBytes,
            baselineSamples,
            currentBytes,
            currentSamples);
    }

    /// <summary>A name as the trace records it, in the one form both are compared in.</summary>
    private static string Form(string recorded) => TypeNameForms.AsGcDumpName(TypeNameForms.FromRuntimeEvent(recorded));

    // Types with a method table are found by it alone: the dump would have the type under the sample's table, so a name that
    // matches another table's type is another type (two nested types of one name).
    private static void Index(HeapGraph graph, TypeAggregation aggregation, bool isCurrent, Dictionary<ulong, Target> tables, Dictionary<string, Target> names)
    {
        var methodTables = graph.Types.MethodTables.Span;
        for (var t = 0; t < aggregation.TypeCount; t++)
        {
            if (aggregation.Counts[t] == 0)
            {
                continue;
            }

            if (methodTables[t] != 0)
            {
                tables.TryAdd(methodTables[t], new Target(isCurrent, t, string.Empty, Shared: false));
                continue;
            }

            var module = TypeGroups.ModuleKey(graph.Types.GetModule(t));
            var name = TypeNameForms.AsGcDumpName(graph.Types.GetName(t));
            if (!names.TryAdd(name, new Target(isCurrent, t, module, Shared: false))
                && names[name] is { Shared: false } known && !string.Equals(known.Module, module, StringComparison.OrdinalIgnoreCase))
            {
                names[name] = known with { Shared = true };
            }
        }
    }

    private readonly record struct Target(bool IsCurrent, int Type, string Module, bool Shared);
}
