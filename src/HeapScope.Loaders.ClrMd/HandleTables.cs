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

    /// <summary>The most processors a Windows dump records: those of one group.</summary>
    private const int ProcessorGroup = 64;

    /// <summary>The handles left out, and what was done, for the log.</summary>
    internal sealed record Result(IReadOnlyList<LeftOutHandle> LeftOut, HandleWalk Walk);

    /// <param name="processors">The processors the dump records, or null.</param>
    public static Result Read(ClrRuntime runtime, string? dacPath, int? processors, IProgress<LoadProgress>? progress, CancellationToken cancellationToken)
    {
        // Windows records the processors of one group, 64 at most: that many may be fewer than the process had.
        var limit = processors is >= ProcessorGroup ? null : processors;
        if (!runtime.Heap.IsServer)
        {
            // Workstation GC has one heap and one table.
            return new Result([], new HandleWalk(HandleWalkOutcome.Complete, 1, 1, processors, 0, "workstation GC, one table"));
        }

        var heaps = runtime.Heap.SubHeaps.Length;
        if (heaps >= limit)
        {
            return new Result([], new HandleWalk(HandleWalkOutcome.Complete, heaps, heaps, processors, 0, "a heap for each processor"));
        }

        if (dacPath is null)
        {
            return new Result([], new HandleWalk(HandleWalkOutcome.Unknown, heaps, heaps, processors, 0, "no second walk without the DAC's file"));
        }

        try
        {
            progress?.Report(new LoadProgress("Reading GC handles", 0, null));
            return Walk(runtime, dacPath, heaps, limit, processors, cancellationToken);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            return new Result([], new HandleWalk(HandleWalkOutcome.Unknown, heaps, heaps, processors, 0, ex.GetType().Name));
        }
    }

    private static Result Walk(ClrRuntime runtime, string dacPath, int heaps, int? limit, int? processors, CancellationToken cancellationToken)
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
        var candidates = reader.Seen().Where(a => modules.Any(m => a >= m.ImageBase && a < m.ImageBase + (ulong)Math.Max(0, m.ImageSize))).Order().ToList();
        if (candidates.Where(Drives).Select(static a => (ulong?)a).FirstOrDefault() is not { } counted)
        {
            return new Result([], new HandleWalk(HandleWalkOutcome.Unknown, heaps, heaps, processors, 0, string.Create(CultureInfo.InvariantCulture, $"none of {candidates.Count} places is the heap count")));
        }

        var (found, tables) = Largest();
        var outcome = Judge(tables, limit, found.LeftOut.Count);
        return new Result(
            found.LeftOut,
            new HandleWalk(outcome, heaps, tables, processors, found.LeftOut.Count, string.Create(CultureInfo.InvariantCulture, $"walked again at 0x{counted:x}, the walk ends at {tables} tables; {found.Dropped} handles of no object dropped")));

        // Shown as none, the heap count leaves no handle: nothing else does.
        bool Drives(ulong candidate) => Count(candidate, 0) == 0;

        int Count(ulong candidate, int shown)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                reader.Show(candidate, shown);
                second.FlushCachedData();
                return second.EnumerateHandles().Take(1).Count();
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

        // The largest count that works, from the processors where they are known: past them too, as long as it adds handles.
        (Found Found, int Tables) Largest()
        {
            var works = heaps;
            var best = new Found([], 0);
            if (limit is { } all && At(all) is { } atAll)
            {
                (best, works) = (atAll, all);
                if (At(Math.Min(all * 2, MostHeaps)) is not { } past || past.LeftOut.Count <= best.LeftOut.Count)
                {
                    return (best, works);
                }

                (best, works) = (past, Math.Min(all * 2, MostHeaps));
            }

            var fails = MostHeaps + 1;
            for (var step = 1; works + step < fails; step *= 2)
            {
                if (At(works + step) is { } more)
                {
                    (best, works) = (more, works + step);
                }
                else
                {
                    fails = works + step;
                }
            }

            while (fails - works > 1)
            {
                var middle = works + ((fails - works) / 2);
                if (At(middle) is { } more)
                {
                    (best, works) = (more, middle);
                }
                else
                {
                    fails = middle;
                }
            }

            return (best, works);
        }

        // The handles with the count shown, or null when it does not work: the walk fails, loses a handle, or gives mostly no handles.
        Found? At(int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var leftOut = new List<LeftOutHandle>();
            var (kept, dropped, read) = (0, 0, 0);
            try
            {
                reader.Show(counted, count);
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
                    }
                    else
                    {
                        var (target, dependent) = (handle.Object.Address, handle.Dependent.Address);
                        if (Enum.IsDefined(handle.HandleKind) && IsObject(target) && IsObject(dependent))
                        {
                            leftOut.Add(new LeftOutHandle(handle.Address, handle.HandleKind, target, dependent, handle.IsStrong));
                        }
                        else if (Enum.IsDefined(handle.HandleKind))
                        {
                            // A kind ClrMD does not name is no root of HeapScope's either (weak interior pointers since .NET 9).
                            dropped++;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                return null;
            }
            finally
            {
                reader.ShowNothing();
            }

            return kept < known.Length || dropped > leftOut.Count ? null : new Found(leftOut, dropped);
        }

        bool IsObject(ulong address) => address == 0 || runtime.Heap.GetObject(address) is { IsValid: true };
    }

    /// <summary>What is said of a walk that found the heap count: whole when it reached every table there can be.</summary>
    internal static HandleWalkOutcome Judge(int tables, int? limit, int added) =>
        tables < limit ? HandleWalkOutcome.Unknown : added > 0 ? HandleWalkOutcome.Recovered : HandleWalkOutcome.Complete;

    private sealed record Found(IReadOnlyList<LeftOutHandle> LeftOut, int Dropped);
}
