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


using System.Runtime;
using System.Runtime.InteropServices;

namespace Fixtures.Leaks;

/// <summary>
/// GC handles made by many threads while they allocate, so that a Server GC process with DATAS makes them on many heaps, and then the
/// heap count going down again (peer review 4.5): the runtime's debugging library leaves out those of the heaps that are gone.
/// </summary>
public static class HandleFixtures
{
    private static readonly List<DependentHandle> s_dependent = [];

    /// <summary>Makes <paramref name="perThread"/> handles of each kind on a thread per processor. Returns how many of each kind that is.</summary>
    public static int Create(int perThread)
    {
        var threads = Environment.ProcessorCount;
        var stop = false;
        using var made = new CountdownEvent(threads);
        var running = new List<Thread>();
        for (var t = 0; t < threads; t++)
        {
            var thread = new Thread(() =>
            {
                var kept = new Queue<byte[]>();
                var started = Environment.TickCount64;
                while (Environment.TickCount64 - started < 5_000)
                {
                    Allocate(kept);
                }

                for (var i = 0; i < perThread; i++)
                {
                    GCHandle.Alloc(new HandleHeld());
                    GCHandle.Alloc(new HandlePinned[1], GCHandleType.Pinned);
                    var key = new HandleKey();
                    GCHandle.Alloc(key);
                    lock (s_dependent)
                    {
                        s_dependent.Add(new DependentHandle(key, new HandleValue()));
                    }
                }

                made.Signal();
                while (!Volatile.Read(ref stop))
                {
                    Allocate(kept);
                }
            });
            thread.Start();
            running.Add(thread);
        }

        made.Wait();
        Volatile.Write(ref stop, true);
        running.ForEach(static t => t.Join());

        // Quiet collections, for the heap count to go down.
        for (var i = 0; i < 40; i++)
        {
            GC.Collect();
            Thread.Sleep(50);
        }

        return threads * perThread;
    }

    private static void Allocate(Queue<byte[]> kept)
    {
        kept.Enqueue(new byte[20_000]);
        if (kept.Count > 500)
        {
            kept.Dequeue();
        }
    }
}

/// <summary>Held by a strong handle only.</summary>
public sealed class HandleHeld
{
    public int Value { get; init; }
}

/// <summary>In an array held by a pinned handle only.</summary>
public struct HandlePinned
{
    public long Value { get; set; }
}

/// <summary>The key of a dependent handle, held by a strong handle.</summary>
public sealed class HandleKey
{
    public int Value { get; init; }
}

/// <summary>Held by a dependent handle only.</summary>
public sealed class HandleValue
{
    public int Value { get; init; }
}
