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
using Microsoft.Diagnostics.Runtime;

namespace HeapScope.Loaders.ClrMd.Tests;

/// <summary>A dump's memory with one number shown as another (peer review 4.5).</summary>
public sealed class HeapCountReaderTests
{
    private const ulong Start = 0x1000;

    private readonly HeapCountReader _reader;

    public HeapCountReaderTests()
    {
        var memory = new byte[64];
        MemoryMarshal.Write(memory.AsSpan(16), 4);
        MemoryMarshal.Write(memory.AsSpan(32), 4);
        _reader = new HeapCountReader(new Memory(memory));
    }

    [Fact]
    public void Places_where_the_watched_number_is_read_as_one_are_recorded()
    {
        _reader.Read<int>(Start + 32).ShouldBe(4);
        _reader.Watch(4);
        _reader.Read<int>(Start + 16);
        _reader.Read<int>(Start + 20);
        _reader.Read<long>(Start + 32);

        _reader.Seen().ShouldBe([Start + 16]);
        _reader.Read<int>(Start + 32);
        _reader.Seen().ShouldBe([Start + 16], "watching has ended");
    }

    [Fact]
    public void The_number_shown_is_read_at_its_place_only_whole_or_within_a_larger_read()
    {
        _reader.Show(Start + 16, 7);

        _reader.Read<int>(Start + 16).ShouldBe(7);
        _reader.Read<int>(Start + 32).ShouldBe(4);
        _reader.Read<long>(Start + 12).ShouldBe(7L << 32);
        _reader.ReadPointer(Start + 16).ShouldBe(7UL);
        Span<byte> all = stackalloc byte[64];
        _reader.Read(Start, all).ShouldBe(64);
        MemoryMarshal.Read<int>(all[16..]).ShouldBe(7);
        MemoryMarshal.Read<int>(all[32..]).ShouldBe(4);

        // A read that holds only a part of it, and one cut short before it, are the dump's.
        _reader.Read<int>(Start + 18).ShouldBe(0);
        _reader.Read<short>(Start + 16).ShouldBe((short)4);
        _reader.Read(Start + 60, all).ShouldBe(4);

        _reader.ShowNothing();
        _reader.Read<int>(Start + 16).ShouldBe(4);
    }

    private sealed class Memory(byte[] bytes) : IDataReader
    {
        public string DisplayName => "made up";

        public bool IsThreadSafe => true;

        public OSPlatform TargetPlatform => OSPlatform.Linux;

        public Architecture Architecture => Architecture.X64;

        public int ProcessId => 1;

        public int PointerSize => 8;

        public void FlushCachedData()
        {
        }

        public IEnumerable<ModuleInfo> EnumerateModules() => [];

        public bool GetThreadContext(uint threadID, uint contextFlags, Span<byte> context) => false;

        public int Read(ulong address, Span<byte> buffer)
        {
            if (address < Start || address >= Start + (ulong)bytes.Length)
            {
                return 0;
            }

            var offset = (int)(address - Start);
            var length = Math.Min(buffer.Length, bytes.Length - offset);
            bytes.AsSpan(offset, length).CopyTo(buffer);
            return length;
        }

        public bool Read<T>(ulong address, out T value)
            where T : unmanaged => throw new NotSupportedException();

        public T Read<T>(ulong address)
            where T : unmanaged => throw new NotSupportedException();

        public bool ReadPointer(ulong address, out ulong value) => throw new NotSupportedException();

        public ulong ReadPointer(ulong address) => throw new NotSupportedException();
    }
}
