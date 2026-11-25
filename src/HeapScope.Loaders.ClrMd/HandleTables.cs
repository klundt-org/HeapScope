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
using HeapScope.Core;
using HeapScope.Core.Loading;
using Microsoft.Diagnostics.Runtime;

namespace HeapScope.Loaders.ClrMd;

/// <summary>A GC handle the runtime's own walk leaves out, by address only: it was read through another target.</summary>
/// <param name="IsStrong">Whether a ref-counted handle holds its object.</param>
internal readonly record struct LeftOutHandle(ulong Address, ClrHandleKind Kind, ulong Object, ulong Dependent, bool IsStrong);

/// <summary>
/// The GC handles of a Server GC process whose heap count changed (peer review 4.5). The runtime keeps one handle table per
/// processor and the debugging library walks one per heap the process has at that moment; since DATAS (on by default in .NET 9 and 10
/// with Server GC) the heap count goes up and down, so a handle made while there were more heaps was left out, with everything only
/// it holds (a quarter of 12,000, measured at 18 of 24 heaps). The walk is repeated with a reader that shows a larger heap count for
/// its length. That number is found by what it does: shown as none it leaves no handle. A count past the tables fails and is not used, and
/// a handle whose objects are none of the heap's is dropped. Complete is said only of a walk that reached the last table.
/// </summary>
internal static class HandleTables
{
    /// <summary>More heaps than any target has, for a dump that does not record its processors.</summary>
    private const int MostHeaps = 1024;

    /// <summary>Handles of no object a walk may give and still be one: a damaged object, not a place that is no table.</summary>
    private const int MostBadHandles = 16;

    /// <summary>The handles left out, and what was done, for the log.</summary>
    internal sealed record Result(IReadOnlyList<LeftOutHandle> LeftOut, HandleWalk Walk);

    /// <param name="processors">The processors the dump records, or null.</param>
    public static Result Read(ClrRuntime runtime, string? dacPath, int? processors, IProgress<LoadProgress>? progress, CancellationToken cancellationToken)
    {
        if (!runtime.Heap.IsServer)
        {
            // Workstation GC has one heap and one table.
            return new Result([], new HandleWalk(HandleWalkOutcome.Complete, 1, 1, processors, 0, "workstation GC, one table"));
        }

        var heaps = runtime.Heap.SubHeaps.Length;
        if (dacPath is null)
        {
            return new Result([], new HandleWalk(HandleWalkOutcome.Unknown, heaps, heaps, processors, 0, "no second walk without the DAC's file"));
        }

        try
        {
            progress?.Report(new LoadProgress("Reading GC handles", 0, null));
            return Walk(runtime, dacPath, heaps, processors, cancellationToken);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            return new Result([], new HandleWalk(HandleWalkOutcome.Unknown, heaps, heaps, processors, 0, ex.GetType().Name));
        }
    }

    private static Result Walk(ClrRuntime runtime, string dacPath, int heaps, int? processors, CancellationToken cancellationToken)
    {
        var reader = new HeapCountReader(runtime.DataTarget.DataReader);
        using var target = new DataTarget(reader, new DataTargetOptions { FileLocator = LocalOnlyFileLocator.Instance });
        var clr = target.ClrVersions[0];
        using var second = clr.CreateRuntime(dacPath, ignoreMismatch: true);
        reader.Watch(heaps);
        second.FlushCachedData();
        ulong[] known = [.. second.EnumerateHandles().Select(static h => h.Address).Distinct().Order()];

        // The heap count is a number of the runtime's own module, or of its GC's, that the walk reads.
        var modules = target.DataReader.EnumerateModules().Where(static m => Path.GetFileName(m.FileName) is { } name
            && (name.Contains("coreclr", StringComparison.OrdinalIgnoreCase) || name.Contains("clrgc", StringComparison.OrdinalIgnoreCase))).Append(clr.ModuleInfo).ToList();
        var places = reader.Seen().Where(a => modules.Any(m => a >= m.ImageBase && a < m.ImageBase + (ulong)Math.Max(0, m.ImageSize))).Order().ToList();
        var driving = places.Where(Drives).ToList();
        if (known.Length == 0 || driving.Count == 0)
        {
            return new Result([], new HandleWalk(HandleWalkOutcome.Unknown, heaps, heaps, processors, 0, string.Create(CultureInfo.InvariantCulture, $"none of {places.Count} places is the heap count ({known.Length} handles)")));
        }

        var (counted, search) = driving.Select(a => (Place: a, Search: Search(heaps, processors, count => At(a, count)))).MaxBy(static s => s.Search.Best.LeftOut.Count);
        var leftOut = search.Best.LeftOut;
        var outcome = Judge(search.Tables, processors, leftOut.Count, search.Doubt || (leftOut.Count == 0 && driving.Count > 1));
        return new Result(
            leftOut,
            new HandleWalk(outcome, heaps, search.Tables, processors, leftOut.Count, string.Create(
                CultureInfo.InvariantCulture,
                $"walked again at 0x{counted:x} ({driving.Count} of {places.Count} places), the walk ends at {search.Tables} tables{(search.Doubt ? ", in doubt" : string.Empty)}; {search.Best.Dropped} handles of no object dropped")));

        // Shown as none, the heap count leaves no handle; shown as one of several, it leaves some.
        bool Drives(ulong place) => Count(place, 0) == 0 && (heaps == 1 || Count(place, 1) is > 0 and var some && some <= known.Length);

        int Count(ulong place, int shown)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                reader.Show(place, shown);
                second.FlushCachedData();
                return second.EnumerateHandles().Count();
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                return -1;
            }
            finally
            {
                reader.ShowNothing();
            }
        }

        // The handles with the count shown. Past the tables the walk gives nothing; anything else that fails leaves a doubt.
        Attempt At(ulong place, int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var leftOut = new List<LeftOutHandle>();
            var (kept, dropped, read) = (0, 0, 0);
            try
            {
                reader.Show(place, count);
                second.FlushCachedData();
                foreach (var handle in second.EnumerateHandles())
                {
                    if ((++read & 0xFFFF) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (Array.BinarySearch(known, handle.Address) >= 0)
                    {
                        kept++;
                        continue;
                    }

                    var (held, dependent) = (handle.Object.Address, handle.Dependent.Address);
                    if (!Enum.IsDefined(handle.HandleKind))
                    {
                        // A kind ClrMD does not name is no root of HeapScope's either (weak interior pointers since .NET 9).
                    }
                    else if (IsObject(held) && IsObject(dependent))
                    {
                        leftOut.Add(new LeftOutHandle(handle.Address, handle.HandleKind, held, dependent, handle.IsStrong));
                    }
                    else
                    {
                        dropped++;
                    }
                }
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                return new Attempt(AttemptOutcome.Doubt, Found.None);
            }
            finally
            {
                reader.ShowNothing();
            }

            return kept == 0 ? new Attempt(AttemptOutcome.End, Found.None)
                : kept < known.Length || (dropped > MostBadHandles && dropped > leftOut.Count) ? new Attempt(AttemptOutcome.Doubt, Found.None)
                : new Attempt(AttemptOutcome.Works, new Found(leftOut, dropped));
        }

        bool IsObject(ulong address) => address == 0 || runtime.Heap.GetObject(address) is { IsValid: true };
    }

    /// <summary>
    /// The largest heap count that works, from the dump's processors where it records them and by doubling and halving from there:
    /// a process can have more tables than one processor group has processors. A count with fewer handles than a smaller one is in doubt.
    /// </summary>
    internal static (Found Best, int Tables, bool Doubt) Search(int heaps, int? processors, Func<int, Attempt> at)
    {
        var (best, works, fails, doubt) = (Found.None, heaps, MostHeaps + 1, false);
        if (processors > heaps)
        {
            Try(Math.Min(processors.Value, MostHeaps));
        }

        for (var step = 1; works + step < fails; step *= 2)
        {
            Try(works + step);
        }

        while (fails - works > 1)
        {
            Try(works + ((fails - works) / 2));
        }

        return (best, works, doubt);

        void Try(int count)
        {
            var attempt = at(count);
            if (attempt.Outcome == AttemptOutcome.Works && attempt.Found.LeftOut.Count >= best.LeftOut.Count)
            {
                (best, works) = (attempt.Found, count);
            }
            else
            {
                fails = count;
                doubt = attempt.Outcome != AttemptOutcome.End;
            }
        }
    }

    /// <summary>What is said of a walk that found the heap count: whole when it ended where the tables do, past the processors.</summary>
    internal static HandleWalkOutcome Judge(int tables, int? processors, int added, bool doubt) =>
        doubt || tables < processors ? HandleWalkOutcome.Unknown : added > 0 ? HandleWalkOutcome.Recovered : HandleWalkOutcome.Complete;

    internal enum AttemptOutcome
    {
        /// <summary>The walk gave every known handle.</summary>
        Works,

        /// <summary>The walk gave nothing: past the tables.</summary>
        End,

        /// <summary>The walk failed, lost some, or gave mostly no handles.</summary>
        Doubt,
    }

    internal readonly record struct Attempt(AttemptOutcome Outcome, Found Found);

    /// <summary>The handles a walk added, and those it gave whose objects are none of the heap's.</summary>
    internal sealed record Found(IReadOnlyList<LeftOutHandle> LeftOut, int Dropped)
    {
        public static Found None { get; } = new([], 0);
    }
}
