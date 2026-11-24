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

using System.Buffers.Binary;
using System.Text;
using HeapScope.Core;

namespace HeapScope.Loaders.Trace;

/// <summary>
/// Checks that what a trace file states fits in it, before TraceEvent reads it (4.2.1): the size of each block, and the
/// fields each event description states. Walks the block headers and reads only the description blocks, so a long trace
/// costs little. It covers the format the .NET runtime writes (nettrace version 4); a file cut short is left to the reader,
/// which keeps what was recorded.
/// </summary>
internal static class TraceFileCheck
{
    /// <summary>The runtime's blocks hold about 100 KB: one that states more than this and ends past the file was never written.</summary>
    private const int LargestTornBlock = 64 * 1024 * 1024;

    /// <summary>Structs inside structs: the runtime's own events have none, and event sources a few levels.</summary>
    internal const int DeepestType = 64;

    /// <summary>"Nettrace", then the length and text of the serialization header.</summary>
    private static readonly byte[] s_serialization = Encoding.ASCII.GetBytes("!FastSerialization.1");

    /// <param name="stream">The trace, at its start.</param>
    /// <exception cref="SnapshotFormatException">The file states what it cannot hold.</exception>
    public static void Check(Stream stream, string path, CancellationToken cancellationToken)
    {
        var length = stream.Length;
        Span<byte> head = stackalloc byte[8 + 4 + 20];
        if (!TryRead(stream, head) || BinaryPrimitives.ReadInt32LittleEndian(head[8..]) != s_serialization.Length || !head[12..].SequenceEqual(s_serialization))
        {
            // Another version of the format: its counts are 16 bits, and the reader's thread has room for what it nests.
            return;
        }

        Span<byte> word = stackalloc byte[4];
        Span<byte> name = stackalloc byte[64];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Begin object (5), begin type (5), null reference (1), version, minimum reader version, the name's length.
            if (!TryRead(stream, name[..15]) || name[0] != 5)
            {
                // The end of the file (1), a file cut short, or what TraceEvent itself refuses.
                return;
            }

            var nameLength = BinaryPrimitives.ReadInt32LittleEndian(name[11..]);
            if (nameLength <= 0 || nameLength > name.Length)
            {
                throw Refuse(path, $"has a block whose name is stated to be {nameLength:N0} bytes long");
            }

            // The name and the type's end tag.
            var type = name[..nameLength];
            if (!TryRead(stream, type) || !TryRead(stream, word[..1]))
            {
                return;
            }

            var description = type.SequenceEqual("MetadataBlock"u8);
            if (type.SequenceEqual("Trace"u8))
            {
                stream.Position += 48 + 1;
                continue;
            }

            if (!TryRead(stream, word))
            {
                return;
            }

            var size = BinaryPrimitives.ReadInt32LittleEndian(word);
            stream.Position = (stream.Position + 3) & ~3L;
            var left = length - stream.Position;
            if (size < 0 || (size > left && size > LargestTornBlock))
            {
                throw Refuse(path, $"has a block stated to be {size:N0} bytes long, with {Math.Max(0, left):N0} bytes left in the file");
            }

            if (size > left)
            {
                return;
            }

            if (description)
            {
                var block = new byte[size];
                stream.ReadExactly(block);
                CheckDescriptions(block, path);
                stream.Position++;
            }
            else
            {
                stream.Position += size + 1;
            }
        }
    }

    /// <summary>A block of event descriptions: every field count must fit in its record.</summary>
    internal static void CheckDescriptions(ReadOnlySpan<byte> block, string path)
    {
        var reader = new Reader(block, path);
        var headerSize = (int)reader.UInt16();
        if (headerSize < 20)
        {
            throw Refuse(path, $"has a block whose header is stated to be {headerSize:N0} bytes long");
        }

        var compressed = (reader.UInt16() & 1) != 0;
        reader.Bytes(headerSize - 4);
        var payloadSize = 0;
        while (reader.Left > 0)
        {
            payloadSize = compressed ? CompressedHeader(ref reader, payloadSize) : PlainHeader(ref reader);
            var record = new Reader(reader.Bytes(payloadSize), path);
            CheckDescription(ref record);
        }
    }

    private static int PlainHeader(ref Reader reader)
    {
        // Size, description id, sequence number, thread, capturing thread, processor, stack, time, two activity ids.
        reader.Bytes(4 + 4 + 4 + 8 + 8 + 4 + 4 + 8 + 16 + 16);
        return reader.Int32();
    }

    private static int CompressedHeader(ref Reader reader, int previousPayloadSize)
    {
        var flags = reader.Byte();
        if ((flags & 1) != 0)
        {
            reader.VarUInt();
        }

        if ((flags & 2) != 0)
        {
            reader.VarUInt();
            reader.VarUInt();
            reader.VarUInt();
        }

        if ((flags & 4) != 0)
        {
            reader.VarUInt();
        }

        if ((flags & 8) != 0)
        {
            reader.VarUInt();
        }

        reader.VarUInt();
        if ((flags & 16) != 0)
        {
            reader.Bytes(16);
        }

        if ((flags & 32) != 0)
        {
            reader.Bytes(16);
        }

        return (flags & 128) != 0 ? (int)(uint)reader.VarUInt() : previousPayloadSize;
    }

    private static void CheckDescription(ref Reader reader)
    {
        // Id, provider, event id, event name, keywords, version, level.
        reader.Int32();
        reader.Text();
        reader.Int32();
        reader.Text();
        reader.Bytes(8 + 4 + 4);
        if (reader.Left == 0)
        {
            return;
        }

        CheckFields(ref reader, sized: false, depth: 0);
        while (reader.Left > 0)
        {
            var tagLength = reader.Int32();
            var tag = reader.Byte();
            var body = new Reader(reader.Bytes(tagLength), reader.Path);
            if (tag == 2)
            {
                CheckFields(ref body, sized: true, depth: 0);
            }
        }
    }

    /// <summary>A list of fields. Sized fields (an optional later list) state their length and come name first.</summary>
    /// <returns>False at a type this version of the format does not have: TraceEvent reads no further in the list.</returns>
    private static bool CheckFields(ref Reader reader, bool sized, int depth)
    {
        if (depth > DeepestType)
        {
            throw Refuse(reader.Path, $"describes an event whose types are nested more than {DeepestType} deep");
        }

        // The least a field takes: its type and an empty name, and its length when it states one.
        var least = sized ? 4 + 2 + 4 : 4 + 2;
        var count = reader.Int32();
        if (count < 0 || count > reader.Left / least)
        {
            throw Refuse(reader.Path, $"describes an event of {count:N0} fields in {reader.Left:N0} bytes");
        }

        for (var i = 0; i < count; i++)
        {
            if (sized)
            {
                var field = new Reader(reader.Bytes(reader.Int32() - 4), reader.Path);
                field.Text();
                if (!CheckType(ref field, sized, depth))
                {
                    return false;
                }
            }
            else
            {
                if (!CheckType(ref reader, sized, depth))
                {
                    return false;
                }

                reader.Text();
            }
        }

        return true;
    }

    private static bool CheckType(ref Reader reader, bool sized, int depth)
    {
        var type = reader.Int32();
        switch (type)
        {
            case 1:
                return CheckFields(ref reader, sized, depth + 1);
            case 19 when sized:
                // An array: the type of its elements follows.
                return depth < DeepestType
                    ? CheckType(ref reader, sized, depth + 1)
                    : throw Refuse(reader.Path, $"describes an event whose types are nested more than {DeepestType} deep");
            default:
                return type is >= 3 and <= 18;
        }
    }

    private static SnapshotFormatException Refuse(string path, string what) =>
        new($"{path} {what}. The file is damaged or crafted; record the trace again.");

    private static bool TryRead(Stream stream, Span<byte> into) =>
        stream.ReadAtLeast(into, into.Length, throwOnEndOfStream: false) == into.Length;

    /// <summary>Reads a record and refuses the file when it ends early.</summary>
    private ref struct Reader(ReadOnlySpan<byte> bytes, string path)
    {
        private ReadOnlySpan<byte> _bytes = bytes;

        public readonly int Left => _bytes.Length;

        public readonly string Path => path;

        public ReadOnlySpan<byte> Bytes(int count)
        {
            if (count < 0 || count > _bytes.Length)
            {
                throw Refuse(path, $"has an event description that needs {count:N0} bytes where {_bytes.Length:N0} are left");
            }

            var taken = _bytes[..count];
            _bytes = _bytes[count..];
            return taken;
        }

        public byte Byte() => Bytes(1)[0];

        public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(2));

        public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Bytes(4));

        /// <summary>A number in groups of 7 bits; only its length matters here, except a payload's size.</summary>
        public ulong VarUInt()
        {
            var value = 0UL;
            for (var shift = 0; shift < 70; shift += 7)
            {
                var part = Byte();
                value |= (ulong)(part & 0x7F) << shift;
                if ((part & 0x80) == 0)
                {
                    return value;
                }
            }

            throw Refuse(path, "has an event description with a number that does not end");
        }

        /// <summary>UTF-16 text up to its terminating zero.</summary>
        public void Text()
        {
            while (UInt16() != 0)
            {
                // Only its end matters.
            }
        }
    }
}
