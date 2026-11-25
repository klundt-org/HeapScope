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

using System.Diagnostics;
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

    /// <summary>Walks at most: a place that is no heap count costs one or two, the search of the one that is about twenty.</summary>
    private const int MostWalks = 1024;

    /// <summary>The handles left out, and what was done, for the log.</summary>
    internal sealed record Result(IReadOnlyList<LeftOutHandle> LeftOut, HandleWalk Walk);

    /// <param name="processors">The processors the dump records, or null.</param>
    /// <param name="off">Whether the second walk is turned off (<see cref="ClrMdLoaderOptions.SecondHandleWalk"/>).</param>
    public static Result Read(ClrRuntime runtime, string? dacPath, int? processors, bool off, IProgress<LoadProgress>? progress, CancellationToken cancellationToken)
    {
        if (!runtime.Heap.IsServer)
        {
            // Workstation GC has one heap and one table.
            return new Result([], new HandleWalk(HandleWalkOutcome.Complete, 1, 1, processors, 0, "workstation GC, one table"));
        }

        var heaps = runtime.Heap.SubHeaps.Length;
        if (off || dacPath is null)
        {
            return new Result([], new HandleWalk(HandleWalkOutcome.Unknown, heaps, heaps, processors, 0, off ? "the second walk is turned off (Worker:SecondHandleWalk)" : "no second walk without the DAC's file"));
        }

        try
        {
            progress?.Report(new LoadProgress("Reading GC handles", 0, null));
            return Walk(runtime, dacPath, heaps, processors, cancellationToken);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            return new Result([], new HandleWalk(HandleWalkOutcome.Unknown, heaps, heaps, processors, 0, "failed: " + Describe(ex)));
        }
    }

    private static Result Walk(ClrRuntime runtime, string dacPath, int heaps, int? processors, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var walks = 0;
        var reader = new HeapCountReader(runtime.DataTarget.DataReader);
        using var target = new DataTarget(reader, new DataTargetOptions { FileLocator = LocalOnlyFileLocator.Instance });
        var clr = target.ClrVersions[0];

        // The file the first runtime was created with, which accepted it: the version check was done, or skipped for a build id, there.
        using var second = clr.CreateRuntime(dacPath, ignoreMismatch: true);
        reader.Watch(heaps);
        ulong[] known = [.. Handles().Select(static h => h.Address).Distinct().Order()];

        // The heap count is a number of the runtime's own module, or of its GC's, that the walk reads.
        var modules = target.DataReader.EnumerateModules().Where(static m => Path.GetFileName(m.FileName) is { } name
            && (name.Contains("coreclr", StringComparison.OrdinalIgnoreCase) || name.Contains("clrgc", StringComparison.OrdinalIgnoreCase))).Append(clr.ModuleInfo).ToList();
        var places = reader.Seen().Where(a => modules.Any(m => a >= m.ImageBase && a < m.ImageBase + (ulong)Math.Max(0, m.ImageSize))).Order().ToList();
        var driving = places.Where(Drives).ToList();
        if (known.Length == 0 || driving.Count == 0)
        {
            return new Result([], new HandleWalk(HandleWalkOutcome.Unknown, heaps, heaps, processors, 0, string.Create(CultureInfo.InvariantCulture, $"none of {places.Count} places is the heap count ({known.Length} handles by the runtime's own walk, {walks} walks, {clock.Elapsed.TotalSeconds:N1} s)")));
        }

        // A place no count ends and that adds nothing (a number that only turns the walk on) is not the heap count when another can be.
        var searches = driving.ConvertAll(a => (Place: a, Search: Search(heaps, processors, count => At(a, count))));
        if (searches.Exists(static s => !s.Search.Endless))
        {
            searches.RemoveAll(static s => s.Search.Endless && s.Search.Best.LeftOut.Count == 0);
        }

        var (counted, search) = searches.MaxBy(static s => s.Search.Best.LeftOut.Count);
        var leftOut = search.Best.LeftOut;
        var several = leftOut.Count == 0 && searches.Count > 1;
        var outcome = Judge(search.Tables, processors, leftOut.Count, search.Doubt || several);
        var doubt = search.Doubt ? ", in doubt: " + (search.Why ?? "no count ends the walk") : several ? ", in doubt: more than one place can be the heap count" : string.Empty;
        return new Result(
            leftOut,
            new HandleWalk(outcome, heaps, search.Tables, processors, leftOut.Count, string.Create(
                CultureInfo.InvariantCulture,
                $"{known.Length} by the runtime's own walk, walked again at 0x{counted:x} ({places.Count} places, {driving.Count} drive the walk, {searches.Count} can be the heap count), ending at {search.Tables} tables{doubt}; added {Kinds(leftOut)}; {search.Best.Unnamed} of a kind without a name and {search.Best.Dropped} of no object not used; {walks} walks, {clock.Elapsed.TotalSeconds:N1} s")));

        // Every handle by the count shown, the walk and the token counted.
        IEnumerable<ClrHandle> Handles()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++walks > MostWalks)
            {
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"more than {MostWalks} walks"));
            }

            second.FlushCachedData();
            var read = 0;
            foreach (var handle in second.EnumerateHandles())
            {
                if ((++read & 0xFFFF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                yield return handle;
            }
        }

        // Shown as none, the heap count leaves no handle; shown as one of several, it leaves some.
        bool Drives(ulong place) => Count(place, 0, most: 1) == 0 && (heaps == 1 || Count(place, 1, most: int.MaxValue) is > 0 and var some && some <= known.Length);

        int Count(ulong place, int shown, int most)
        {
            try
            {
                reader.Show(place, shown);
                return Handles().Take(most).Count();
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException) && walks <= MostWalks)
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
            var leftOut = new List<LeftOutHandle>();
            var (kept, dropped, unnamed) = (0, 0, 0);
            try
            {
                reader.Show(place, count);
                foreach (var handle in Handles())
                {
                    if (Array.BinarySearch(known, handle.Address) >= 0)
                    {
                        kept++;
                        continue;
                    }

                    var (held, dependent) = (handle.Object.Address, handle.Dependent.Address);
                    if (!Enum.IsDefined(handle.HandleKind))
                    {
                        // A kind ClrMD does not name is no root of HeapScope's either (weak interior pointers since .NET 9).
                        unnamed++;
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
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException) && walks <= MostWalks)
            {
                return new Attempt(AttemptOutcome.Doubt, Found.None, string.Create(CultureInfo.InvariantCulture, $"at {count}: {Describe(ex)}"));
            }
            finally
            {
                reader.ShowNothing();
            }

            return kept == 0 ? new Attempt(AttemptOutcome.End, Found.None)
                : kept < known.Length || (dropped > MostBadHandles && dropped > leftOut.Count) ? new Attempt(AttemptOutcome.Doubt, Found.None, string.Create(CultureInfo.InvariantCulture, $"at {count}: {kept} of the runtime's own {known.Length}, {leftOut.Count} more, {dropped} of no object"))
                : new Attempt(AttemptOutcome.Works, new Found(leftOut, dropped, unnamed));
        }

        bool IsObject(ulong address) => address == 0 || runtime.Heap.GetObject(address) is { IsValid: true };
    }

    // The handles by kind, for the log.
    private static string Kinds(IReadOnlyList<LeftOutHandle> handles) => handles.Count == 0
        ? "none"
        : string.Join(", ", handles.GroupBy(static h => h.Object == 0 ? "empty" : h.Kind.ToString()).OrderBy(static g => g.Key, StringComparer.Ordinal).Select(static g => string.Create(CultureInfo.InvariantCulture, $"{g.Count()} {g.Key}")));

    // Where an exception came from too: its type alone does not tell an odd dump from a defect of HeapScope's.
    private static string Describe(Exception ex) => $"{ex.GetType().Name} from {ex.TargetSite?.DeclaringType?.Name}.{ex.TargetSite?.Name}: {ex.Message}";

    /// <summary>
    /// The largest heap count that works, from the dump's processors where it records them and by doubling and halving from there:
    /// a process can have more tables than one processor group has processors. A count with fewer handles than a smaller one is in doubt.
    /// </summary>
    internal static Searched Search(int heaps, int? processors, Func<int, Attempt> at)
    {
        var (best, works, fails, doubt) = (Found.None, heaps, MostHeaps + 1, false);
        string? why = null;
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

        // A walk that no count ends is not driven by the heap count.
        return new Searched(best, works, doubt || fails > MostHeaps, fails > MostHeaps, why);

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
                why = !doubt ? null : attempt.Why ?? string.Create(CultureInfo.InvariantCulture, $"at {count}: {attempt.Found.LeftOut.Count} more handles, at {works}: {best.LeftOut.Count}");
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

    /// <param name="Why">What left a doubt, for the log.</param>
    internal readonly record struct Attempt(AttemptOutcome Outcome, Found Found, string? Why = null);

    /// <param name="Endless">No count ended the walk.</param>
    /// <param name="Why">What left a doubt, for the log; null when it was not one count.</param>
    internal sealed record Searched(Found Best, int Tables, bool Doubt, bool Endless, string? Why);

    /// <summary>The handles a walk added, those it gave whose objects are none of the heap's, and those of a kind without a name.</summary>
    internal sealed record Found(IReadOnlyList<LeftOutHandle> LeftOut, int Dropped, int Unnamed)
    {
        public static Found None { get; } = new([], 0, 0);
    }
}
