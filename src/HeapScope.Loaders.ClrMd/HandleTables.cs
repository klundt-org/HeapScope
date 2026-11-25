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
/// its length, and every handle it adds is checked; a count past the tables fails and is not used.
/// </summary>
internal static class HandleTables
{
    /// <summary>More heaps than any target has, for a dump that does not record its processors.</summary>
    private const int MostHeaps = 1024;

    /// <summary>The handles left out, and what was done, for the log.</summary>
    internal sealed record Result(IReadOnlyList<LeftOutHandle> LeftOut, HandleWalk Walk);

    /// <param name="processors">The processors the dump records, or null.</param>
    public static Result Read(ClrRuntime runtime, string? dacPath, int? processors, CancellationToken cancellationToken)
    {
        var heaps = runtime.Heap.IsServer ? runtime.Heap.SubHeaps.Length : 1;
        if (!runtime.Heap.IsServer || dacPath is null)
        {
            // Workstation GC has one heap and one table; a runtime without a DAC file has no second target.
            return new Result([], new HandleWalk(runtime.Heap.IsServer ? HandleWalkOutcome.Unknown : HandleWalkOutcome.Complete, heaps, heaps, processors, 0, "no second walk"));
        }

        if (processors is { } known && heaps >= known)
        {
            return new Result([], new HandleWalk(HandleWalkOutcome.Complete, heaps, heaps, processors, 0, "a heap for each processor"));
        }

        try
        {
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
        using var second = target.ClrVersions[0].CreateRuntime(dacPath, ignoreMismatch: true);
        var known = Handles(second, cancellationToken);

        // The heap count is a number of the GC's module that the walk reads: patching anything else changes nothing.
        reader.Watch(heaps);
        second.FlushCachedData();
        _ = Handles(second, cancellationToken);
        var modules = target.DataReader.EnumerateModules().Where(static m => Path.GetFileName(m.FileName) is { } name
            && (name.Contains("coreclr", StringComparison.OrdinalIgnoreCase) || name.Contains("clrgc", StringComparison.OrdinalIgnoreCase)));
        var candidates = reader.Seen().Where(a => modules.Any(m => a >= m.ImageBase && a < m.ImageBase + (ulong)Math.Max(0, m.ImageSize))).ToList();
        Dictionary<ulong, LeftOutHandle>? best = null;
        var counted = heaps;
        foreach (var candidate in candidates)
        {
            var (found, count) = Largest(candidate);
            if (found is not null && found.Count > (best?.Count ?? 0))
            {
                (best, counted) = (found, count);
            }
        }

        if (best is null)
        {
            return new Result([], new HandleWalk(HandleWalkOutcome.Unknown, heaps, heaps, processors, 0, string.Create(CultureInfo.InvariantCulture, $"{candidates.Count} places hold the heap count, none could be raised")));
        }

        LeftOutHandle[] leftOut = [.. best.Values.Where(h => !known.ContainsKey(h.Address)).OrderBy(h => h.Address)];
        return new Result(leftOut, new HandleWalk(leftOut.Length > 0 ? HandleWalkOutcome.Recovered : HandleWalkOutcome.Complete, heaps, counted, processors, leftOut.Length, "walked again"));

        // The handles at the largest heap count that works: the processors where they are known, else found by doubling and halving.
        (Dictionary<ulong, LeftOutHandle>? Found, int Count) Largest(ulong candidate)
        {
            if (processors is { } limit && At(candidate, limit) is { } all)
            {
                return (all, limit);
            }

            var (works, fails) = (heaps, Math.Min(processors ?? MostHeaps, MostHeaps) + 1);
            var found = At(candidate, heaps);
            for (var step = 1; works + step < fails; step *= 2)
            {
                if (At(candidate, works + step) is { } more)
                {
                    (found, works) = (more, works + step);
                }
                else
                {
                    fails = works + step;
                    break;
                }
            }

            while (fails - works > 1)
            {
                var middle = works + ((fails - works) / 2);
                if (At(candidate, middle) is { } more)
                {
                    (found, works) = (more, middle);
                }
                else
                {
                    fails = middle;
                }
            }

            return (found, works);
        }

        // The handles with the count shown, or null when it does not work: the walk fails, loses a handle, or gives one of no object.
        Dictionary<ulong, LeftOutHandle>? At(ulong candidate, int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                reader.Show(candidate, count);
                second.FlushCachedData();
                var handles = Handles(second, cancellationToken);
                reader.ShowNothing();
                return known.Keys.All(handles.ContainsKey) && handles.Values.All(h => known.ContainsKey(h.Address) || IsHandle(h)) ? handles : null;
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                return null;
            }
            finally
            {
                reader.ShowNothing();
            }
        }

        bool IsHandle(LeftOutHandle handle) =>
            Enum.IsDefined(handle.Kind)
            && (handle.Object == 0 || runtime.Heap.GetObject(handle.Object) is { IsValid: true })
            && (handle.Dependent == 0 || runtime.Heap.GetObject(handle.Dependent) is { IsValid: true });
    }

    private static Dictionary<ulong, LeftOutHandle> Handles(ClrRuntime runtime, CancellationToken cancellationToken)
    {
        var handles = new Dictionary<ulong, LeftOutHandle>();
        foreach (var handle in runtime.EnumerateHandles())
        {
            if ((handles.Count & 0xFFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            handles[handle.Address] = new LeftOutHandle(handle.Address, handle.HandleKind, handle.Object.Address, handle.Dependent.Address, handle.IsStrong);
        }

        return handles;
    }
}
