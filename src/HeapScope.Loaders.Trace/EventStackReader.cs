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

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;

namespace HeapScope.Loaders.Trace;

/// <summary>
/// Reads an event's call stack from the record TraceEvent keeps internally (ADR-037, as amended): its live source exposes
/// none. The record is the one Windows defines (EVENT_RECORD, evntcons.h); its offsets are checked against the library's own
/// struct, once, before anything is read, so another TraceEvent gives no stacks rather than wrong ones.
/// </summary>
internal sealed unsafe class EventStackReader
{
    private const int ExtendedDataCount = 84;
    private const int ExtendedData = 88;
    private const int ItemBytes = 16;
    private const int ItemType = 2;
    private const int ItemSize = 6;
    private const int ItemData = 8;
    private const ushort StackTrace64 = 6;

    private readonly Func<TraceEvent, nint> _record;

    private EventStackReader(Func<TraceEvent, nint> record) => _record = record;

    /// <summary>The reader, or null with why TraceEvent's record is not the one it knows.</summary>
    public static (EventStackReader? Reader, string? Problem) Create() => Create(typeof(TraceEvent));

    /// <summary>As <see cref="Create()"/>, for the type that holds the record (a test's own, too).</summary>
    internal static (EventStackReader? Reader, string? Problem) Create(Type holder)
    {
        if (holder.GetField("eventRecord", BindingFlags.Instance | BindingFlags.NonPublic) is not { } field)
        {
            return (null, $"{holder.Name}.eventRecord is not there");
        }

        if (field.FieldType.GetElementType() is not { } layout)
        {
            return (null, $"{holder.Name}.eventRecord is not a pointer");
        }

        var item = layout.GetField("ExtendedData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.FieldType.GetElementType();
        var problem = Differs(layout, "ExtendedDataCount", ExtendedDataCount)
            ?? Differs(layout, "ExtendedData", ExtendedData)
            ?? (item is null ? $"{layout.Name}.ExtendedData is not a pointer" : null)
            ?? Differs(item!, "ExtType", ItemType)
            ?? Differs(item!, "DataSize", ItemSize)
            ?? Differs(item!, "DataPtr", ItemData);
        if (problem is null && Marshal.SizeOf(item!) != ItemBytes)
        {
            problem = $"{item!.Name} is {Marshal.SizeOf(item)} bytes, not {ItemBytes}";
        }

        if (problem is not null)
        {
            return (null, problem);
        }

        var method = new DynamicMethod("EventRecord", typeof(nint), [typeof(TraceEvent)], holder.Module, skipVisibility: true);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Ret);
        return (new EventStackReader(method.CreateDelegate<Func<TraceEvent, nint>>()), null);
    }

    /// <summary>The instruction pointers of the event's stack, the innermost first; valid only while the event is handled.</summary>
    public ReadOnlySpan<ulong> Stack(TraceEvent e)
    {
        var data = (byte*)_record(e);
        if (data is null)
        {
            return [];
        }

        var count = *(ushort*)(data + ExtendedDataCount);
        var item = *(byte**)(data + ExtendedData);
        for (var i = 0; i < count && item is not null; i++, item += ItemBytes)
        {
            var size = *(ushort*)(item + ItemSize);
            if (*(ushort*)(item + ItemType) == StackTrace64 && size >= 8)
            {
                // A match id, then the instruction pointers.
                return new ReadOnlySpan<ulong>(*(byte**)(item + ItemData) + 8, (size - 8) / 8);
            }
        }

        return [];
    }

    private static string? Differs(Type type, string field, int offset)
    {
        try
        {
            var actual = (int)Marshal.OffsetOf(type, field);
            return actual == offset ? null : $"{type.Name}.{field} is at {actual}, not {offset}";
        }
        catch (ArgumentException)
        {
            return $"{type.Name}.{field} is not there";
        }
    }
}
