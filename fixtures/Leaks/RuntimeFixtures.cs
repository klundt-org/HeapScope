// HeapScope
// Copyright (C) 2026 Ray Klundt
// https://github.com/rklundt/HeapScope
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

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fixtures.Leaks;

/// <summary>
/// Heap kinds and root kinds the full dump loader must label correctly (sprint 0.4 review, ARCHITECTURE.md 6.1):
/// finalizable objects (half with finalization suppressed), pinned object heap arrays, large object heap arrays,
/// pinned GC handles, a frozen string literal, and a parked thread holding a local and an interior pointer.
/// </summary>
public static class RuntimeFixtures
{
    /// <summary>Above the 85,000 byte large object threshold.</summary>
    public const int LargeArrayBytes = 100_000;

    /// <summary>A string literal; on .NET 8 and later literals live in the frozen (NonGC) heap.</summary>
    public const string FrozenLiteral = "HeapScope frozen literal fixture";

    private static readonly List<FinalizableResource> s_finalizable = [];
    private static readonly List<PohHolder> s_poh = [];
    private static readonly List<LohHolder> s_loh = [];
    private static readonly List<GCHandle> s_pinnedHandles = [];
    private static readonly List<object> s_collectible = [];
    private static readonly ManualResetEventSlim s_parked = new(false);
    private static string s_literal = string.Empty;
    private static Thread? s_localThread;
    private static Thread? s_interiorThread;

    public static void Create(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var resource = new FinalizableResource(i);
            if (i % 2 == 0)
            {
                GC.SuppressFinalize(resource);
            }

            s_finalizable.Add(resource);
            s_poh.Add(new PohHolder(GC.AllocateArray<byte>(256, pinned: true)));
            s_loh.Add(new LohHolder(new byte[LargeArrayBytes]));
            s_pinnedHandles.Add(GCHandle.Alloc(new PinnedPoint[4], GCHandleType.Pinned));
        }

        s_collectible.AddRange(CollectibleCounters(count));
        s_literal = FrozenLiteral;
        s_localThread = StartParked(ParkWithLocal, "HeapScope fixture local root");
        s_interiorThread = StartParked(ParkWithInteriorPointer, "HeapScope fixture interior root");
    }

    public static string Literal => s_literal;

    /// <summary>The name of the collectible type below.</summary>
    public const string CollectibleTypeName = "Fixtures.Collectible.Counter";

    /// <summary>
    /// Instances of a type without reference fields from a collectible assembly (sprint 3.12 review H3): the GC keeps the
    /// assembly's LoaderAllocator alive through each of them, so a heap graph must have that edge although the type has no
    /// pointers.
    /// </summary>
    private static List<object> CollectibleCounters(int count)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Fixtures.Collectible"), AssemblyBuilderAccess.RunAndCollect);
        var builder = assembly.DefineDynamicModule("Fixtures.Collectible")
            .DefineType(CollectibleTypeName, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);
        builder.DefineField("Value", typeof(long), FieldAttributes.Public);
        var type = builder.CreateType();
        var result = new List<object>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(Activator.CreateInstance(type)!);
        }

        return result;
    }

    private static Thread StartParked(Action<ManualResetEventSlim> park, string name)
    {
        using var ready = new ManualResetEventSlim(false);
        var thread = new Thread(() => park(ready)) { IsBackground = true, Name = name };
        thread.Start();
        ready.Wait();
        return thread;
    }

    /// <summary>Holds a <see cref="StackRootedObject"/> only in a local, then blocks forever.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ParkWithLocal(ManualResetEventSlim ready)
    {
        var rooted = new StackRootedObject(42);
        ready.Set();
        s_parked.Wait();
        GC.KeepAlive(rooted);
    }

    /// <summary>Holds an <see cref="InteriorElement"/> array only through a ref to its element 3, then blocks forever.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ParkWithInteriorPointer(ManualResetEventSlim ready)
    {
        ref var element = ref CreateInteriorArray()[3];
        ready.Set();
        s_parked.Wait();
        element.Value++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static InteriorElement[] CreateInteriorArray() => new InteriorElement[8];
}

public sealed class FinalizableResource(int id)
{
    public int Id { get; } = id;

    ~FinalizableResource()
    {
        // Never runs while the fixture is alive; exists so the type is finalizable.
    }
}

public sealed class PohHolder(byte[] buffer)
{
    public byte[] Buffer { get; } = buffer;
}

public sealed class LohHolder(byte[] buffer)
{
    public byte[] Buffer { get; } = buffer;
}

[StructLayout(LayoutKind.Sequential)]
public struct PinnedPoint
{
    public int X;
    public int Y;
}

public sealed class StackRootedObject(int id)
{
    public int Id { get; } = id;
}

public struct InteriorElement
{
    public long Value;
}
