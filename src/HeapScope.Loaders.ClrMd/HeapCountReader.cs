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

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;

namespace HeapScope.Loaders.ClrMd;

/// <summary>
/// A dump's memory with one number shown as another, and a record of where a number was read (<see cref="HandleTables"/>). Everything
/// else is the reader's own. Used by one thread.
/// </summary>
internal sealed class HeapCountReader(IDataReader reader) : IDataReader
{
    private readonly HashSet<ulong> _seen = [];
    private int _watched;
    private ulong _shownAt;
    private int _shown;

    public string DisplayName => reader.DisplayName;

    public bool IsThreadSafe => false;

    public OSPlatform TargetPlatform => reader.TargetPlatform;

    public Architecture Architecture => reader.Architecture;

    public int ProcessId => reader.ProcessId;

    public int PointerSize => reader.PointerSize;

    /// <summary>Records from now on where <paramref name="value"/> is read as a 32-bit number.</summary>
    public void Watch(int value) => _watched = value;

    /// <summary>The addresses recorded since <see cref="Watch"/>, which ends.</summary>
    public IReadOnlyCollection<ulong> Seen()
    {
        _watched = 0;
        return [.. _seen];
    }

    /// <summary>Shows <paramref name="value"/> at <paramref name="address"/> from now on.</summary>
    public void Show(ulong address, int value) => (_shownAt, _shown) = (address, value);

    public void ShowNothing() => _shownAt = 0;

    public IEnumerable<ModuleInfo> EnumerateModules() => reader.EnumerateModules();

    public bool GetThreadContext(uint threadID, uint contextFlags, Span<byte> context) => reader.GetThreadContext(threadID, contextFlags, context);

    public void FlushCachedData() => reader.FlushCachedData();

    public int Read(ulong address, Span<byte> buffer)
    {
        var read = reader.Read(address, buffer);
        if (_watched != 0 && read == sizeof(int) && buffer.Length == sizeof(int) && MemoryMarshal.Read<int>(buffer) == _watched)
        {
            _seen.Add(address);
        }

        if (_shownAt >= address && _shownAt != 0 && read >= sizeof(int) && _shownAt - address <= (ulong)(read - sizeof(int)))
        {
            MemoryMarshal.Write(buffer[(int)(_shownAt - address)..], in _shown);
        }

        return read;
    }

    public bool Read<T>(ulong address, out T value)
        where T : unmanaged
    {
        Span<byte> bytes = stackalloc byte[Unsafe.SizeOf<T>()];
        var whole = Read(address, bytes) == bytes.Length;
        value = whole ? MemoryMarshal.Read<T>(bytes) : default;
        return whole;
    }

    public T Read<T>(ulong address)
        where T : unmanaged
    {
        Read(address, out T value);
        return value;
    }

    public bool ReadPointer(ulong address, out ulong value)
    {
        if (PointerSize == sizeof(ulong))
        {
            return Read(address, out value);
        }

        var whole = Read(address, out uint narrow);
        value = narrow;
        return whole;
    }

    public ulong ReadPointer(ulong address)
    {
        ReadPointer(address, out var value);
        return value;
    }
}
