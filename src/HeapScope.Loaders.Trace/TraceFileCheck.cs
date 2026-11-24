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

using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using HeapScope.Core;

namespace HeapScope.Loaders.Trace;

/// <summary>
/// Checks that what a trace file states fits in it, before TraceEvent reads it (4.2.1): the size of each block, and the
/// fields each event description states. Walks the block headers and reads only the description blocks, so a long trace
/// costs little. It follows TraceEvent's own reading of nettrace version 4, the one the .NET runtime writes, tag by tag:
/// where TraceEvent stops (a file cut short, another tag) the walk stops, and what it accepts here it has been shown to fit.
/// Descriptions are read from their own blocks only: in version 4 TraceEvent 4.0 takes none from a block of events (an event
/// whose description is not known, 0 included, is an error there). Any other version is refused, since nothing checks it.
/// Look at it again when TraceEvent or the runtime's format changes.
/// </summary>
internal static class TraceFileCheck
{
    /// <summary>Changes when what is refused changes (the worker's versions line).</summary>
    public const int Version = 1;

    /// <summary>Structs inside structs: the runtime's own events have none, and event sources a few levels.</summary>
    internal const int DeepestType = 64;

    /// <summary>The runtime's description blocks hold about 100 KB at most.</summary>
    internal const int LargestDescriptions = 1024 * 1024;

    /// <summary>A block that states more than this and ends past the file was never written (the runtime's hold about 100 KB).</summary>
    private const int LargestTornBlock = 64 * 1024 * 1024;

    private const byte EndOfTrace = 1;
    private const byte BeginObject = 5;
    private const byte EndObject = 6;
    private const byte NullReference = 1;
    private const int TraceVersion = 4;
    private const int TraceObjectBytes = 48;

    /// <summary>Begin object, begin type, null reference, version, minimum reader version, the length of the type's name.</summary>
    private const int PreambleBytes = 3 + 4 + 4 + 4;

    private const int StructType = 1;
    private const int ArrayType = 19;

    /// <summary>Bool to a string.</summary>
    private const int FirstPlainType = 3;
    private const int LastPlainType = 18;
    private const byte FieldsTag = 2;

    /// <param name="stream">The trace, at its start.</param>
    /// <exception cref="SnapshotFormatException">The file states what it cannot hold, or is another version of the format.</exception>
    public static TraceCheckResult Check(Stream stream, string path, CancellationToken cancellationToken)
    {
        var length = stream.Length;
        Span<byte> head = stackalloc byte[8 + 4 + 20];
        if (!TryRead(stream, head) || BinaryPrimitives.ReadInt32LittleEndian(head[8..]) != 20 || !head[12..].SequenceEqual("!FastSerialization.1"u8))
        {
            // Version 6 and later: a reserved 0, then the version. Anything else is damage.
            var newer = BinaryPrimitives.ReadInt32LittleEndian(head[12..]);
            throw BinaryPrimitives.ReadInt32LittleEndian(head[8..]) == 0 && newer is > TraceVersion and < 100
                ? OtherVersion(path, newer)
                : Refuse(path, 8, "head", "it does not start as a trace");
        }

        Span<byte> word = stackalloc byte[4];
        Span<byte> preamble = stackalloc byte[PreambleBytes];
        Span<byte> nameBuffer = stackalloc byte[64];
        var (blocks, descriptions, fields) = (0, 0, 0L);
        var first = true;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var at = stream.Position;
            TraceCheckResult Stopped(string end) => new(end, at, blocks, descriptions, fields);
            if (!TryRead(stream, preamble[..1]))
            {
                return Stopped(TraceCheckResult.CutShort);
            }

            if (preamble[0] == EndOfTrace)
            {
                return Stopped(TraceCheckResult.Whole);
            }

            if (!TryRead(stream, preamble[1..]))
            {
                return Stopped(preamble[0] == BeginObject ? TraceCheckResult.CutShort : TraceCheckResult.OtherTag);
            }

            if (preamble[0] != BeginObject || preamble[1] != BeginObject || preamble[2] != NullReference)
            {
                return Stopped(TraceCheckResult.OtherTag);
            }

            var version = BinaryPrimitives.ReadInt32LittleEndian(preamble[3..]);
            var nameLength = BinaryPrimitives.ReadInt32LittleEndian(preamble[11..]);
            if (nameLength <= 0 || nameLength > nameBuffer.Length)
            {
                throw Refuse(path, at, "block name", $"a block's name is stated to be {nameLength:N0} bytes long");
            }

            var type = nameBuffer[..nameLength];
            if (!TryRead(stream, type) || !TryRead(stream, word[..1]))
            {
                return Stopped(TraceCheckResult.CutShort);
            }

            if (word[0] != EndObject)
            {
                return Stopped(TraceCheckResult.OtherTag);
            }

            if (first != type.SequenceEqual("Trace"u8))
            {
                throw Refuse(path, at, "start", first ? "it does not start as a trace" : "it has a second start");
            }

            if (first && version != TraceVersion)
            {
                throw version is > 0 and < 100 ? OtherVersion(path, version) : Refuse(path, at, "version", $"its version is stated to be {version:N0}");
            }

            if (first)
            {
                first = false;
                if (length - stream.Position < TraceObjectBytes + 1)
                {
                    return Stopped(TraceCheckResult.CutShort);
                }

                stream.Position += TraceObjectBytes;
            }
            else
            {
                if (!TryRead(stream, word))
                {
                    return Stopped(TraceCheckResult.CutShort);
                }

                var size = BinaryPrimitives.ReadInt32LittleEndian(word);
                stream.Position = (stream.Position + 3) & ~3L;
                var left = Math.Max(0, length - stream.Position);
                if (size < 0 || (size > left && size > LargestTornBlock))
                {
                    throw Refuse(path, at, "block size", $"a block is stated to be {size:N0} bytes long, with {left:N0} bytes left in the file");
                }

                if (size > left)
                {
                    return Stopped(TraceCheckResult.CutShort);
                }

                if (type.SequenceEqual("MetadataBlock"u8))
                {
                    if (size > LargestDescriptions)
                    {
                        throw Refuse(path, at, "description block size", $"a block of event descriptions is {size:N0} bytes long, more than HeapScope reads ({LargestDescriptions:N0})");
                    }

                    var start = stream.Position;
                    var block = ArrayPool<byte>.Shared.Rent(size);
                    try
                    {
                        if (!TryRead(stream, block.AsSpan(0, size)))
                        {
                            return Stopped(TraceCheckResult.CutShort);
                        }

                        CheckDescriptions(block.AsSpan(0, size), path, start, ref descriptions, ref fields, cancellationToken);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(block);
                    }
                }
                else
                {
                    stream.Position += size;
                }
            }

            blocks++;
            if (!TryRead(stream, word[..1]))
            {
                return Stopped(TraceCheckResult.CutShort);
            }

            if (word[0] != EndObject)
            {
                return Stopped(TraceCheckResult.OtherTag);
            }
        }
    }

    /// <summary>A block of event descriptions: every field count must fit in its record.</summary>
    private static void CheckDescriptions(ReadOnlySpan<byte> block, string path, long start, ref int descriptions, ref long fields, CancellationToken cancellationToken)
    {
        var reader = new Reader(block, path, start);
        var headerSize = (int)reader.UInt16("block header");
        if (headerSize < 20)
        {
            throw Refuse(path, start, "block header", $"a block's header is stated to be {headerSize:N0} bytes long");
        }

        var compressed = (reader.UInt16("block header") & 1) != 0;
        reader.Bytes(headerSize - 4, "block header");
        var payloadSize = 0;
        while (reader.Left > 0)
        {
            if ((++descriptions & 0xFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            payloadSize = compressed ? CompressedHeader(ref reader, payloadSize) : PlainHeader(ref reader);
            var record = reader.Part(payloadSize, "record size");

            // Every field takes six bytes of its record at least (a type and an empty name), so all the lists of a
            // description together can state no more than that: one budget, since the reader allocates for each list.
            var all = record.Left / LeastFieldBytes;
            var budget = all;
            CheckDescription(ref record, ref budget);
            fields += all - budget;
        }
    }

    private const int LeastFieldBytes = 4 + 2;

    private static int PlainHeader(ref Reader reader)
    {
        // Size, description id, sequence number, thread, capturing thread, processor, stack, time, two activity ids. TraceEvent
        // steps by the payload's size in a block of descriptions, not by the record's, and so does this.
        reader.Bytes(4 + 4 + 4 + 8 + 8 + 4 + 4 + 8 + 16 + 16, "record header");
        return reader.Int32("record header");
    }

    private static int CompressedHeader(ref Reader reader, int previousPayloadSize)
    {
        const string rule = "record header";
        var flags = reader.Byte(rule);
        if ((flags & 1) != 0)
        {
            // The description's id.
            reader.VarUInt(5);
        }

        if ((flags & 2) != 0)
        {
            // Sequence number, capturing thread, processor.
            reader.VarUInt(5);
            reader.VarUInt(10);
            reader.VarUInt(5);
        }

        if ((flags & 4) != 0)
        {
            // Thread.
            reader.VarUInt(10);
        }

        if ((flags & 8) != 0)
        {
            // Stack.
            reader.VarUInt(5);
        }

        // Time.
        reader.VarUInt(10);
        if ((flags & 16) != 0)
        {
            // Activity.
            reader.Bytes(16, rule);
        }

        if ((flags & 32) != 0)
        {
            // Related activity.
            reader.Bytes(16, rule);
        }

        return (flags & 128) != 0 ? (int)(uint)reader.VarUInt(5) : previousPayloadSize;
    }

    private static void CheckDescription(ref Reader reader, ref int budget)
    {
        const string rule = "description";

        // Id, provider, event id, event name, keywords, version, level.
        reader.Int32(rule);
        reader.Text(rule);
        reader.Int32(rule);
        reader.Text(rule);
        reader.Bytes(8 + 4 + 4, rule);
        if (reader.Left == 0)
        {
            return;
        }

        CheckFields(ref reader, sized: false, depth: 0, ref budget);
        while (reader.Left > 0)
        {
            var tagLength = reader.Int32("tag");
            var tag = reader.Byte("tag");
            var body = reader.Part(tagLength, "tag");
            if (tag == FieldsTag)
            {
                CheckFields(ref body, sized: true, depth: 0, ref budget);
            }
        }
    }

    /// <summary>A list of fields. Sized fields (an optional later list) state their length and come name first.</summary>
    private static void CheckFields(ref Reader reader, bool sized, int depth, ref int budget)
    {
        if (depth > DeepestType)
        {
            throw TooDeep(reader);
        }

        var count = reader.Int32("field count");
        if (count < 0 || count > budget)
        {
            throw Refuse(reader.Path, reader.Offset - 4, "field count", $"an event is described with {count:N0} fields, with room for {budget:N0}");
        }

        budget -= count;
        for (var i = 0; i < count; i++)
        {
            if (sized)
            {
                var stated = reader.Int32("field size");
                var field = reader.Part(stated >= 4 ? stated - 4 : -1, "field size");
                field.Text("field name");
                CheckType(ref field, sized, depth, ref budget);
            }
            else
            {
                CheckType(ref reader, sized, depth, ref budget);
                reader.Text("field name");
            }
        }
    }

    /// <summary>
    /// The types version 4 has, and no other: what TraceEvent does at one it does not know, or at one of a later version, decides
    /// where it reads next, so such a description is refused (the runtime writes none; 4.2.1 review).
    /// </summary>
    private static void CheckType(ref Reader reader, bool sized, int depth, ref int budget)
    {
        var at = reader.Offset;
        var type = reader.Int32("field type");
        if (type == StructType)
        {
            CheckFields(ref reader, sized, depth + 1, ref budget);
        }
        else if (type == ArrayType && sized)
        {
            // The type of its elements follows.
            if (depth >= DeepestType)
            {
                throw TooDeep(reader);
            }

            CheckType(ref reader, sized, depth + 1, ref budget);
        }
        else if (type is < FirstPlainType or > LastPlainType)
        {
            throw Refuse(reader.Path, at, "field type", $"a field has type {type:N0}, which version {TraceVersion} does not have");
        }
    }

    private static SnapshotFormatException TooDeep(Reader reader) =>
        Refuse(reader.Path, reader.Offset, "nesting", $"an event is described with types nested deeper than HeapScope reads ({DeepestType} levels)");

    /// <summary>A sound file may be of another version: no advice to copy it again.</summary>
    private static SnapshotFormatException OtherVersion(string path, int version) =>
        new($"{path} is a trace of format version {version} (version); HeapScope reads version {TraceVersion}, which the .NET runtime writes. Record it with HeapScope, or with dotnet-trace collect.");

    private static SnapshotFormatException Refuse(string path, long offset, string rule, string what) =>
        new(string.Create(CultureInfo.CurrentCulture, $"{path} does not match the trace format at byte {offset:N0} ({rule}): {what}. If it was copied or downloaded, copy it again and compare its size with the original; otherwise record the trace again."));

    private static bool TryRead(Stream stream, Span<byte> into) =>
        stream.ReadAtLeast(into, into.Length, throwOnEndOfStream: false) == into.Length;

    /// <summary>Reads a record and refuses the file when it ends early.</summary>
    private ref struct Reader(ReadOnlySpan<byte> bytes, string path, long offset)
    {
        private ReadOnlySpan<byte> _bytes = bytes;
        private long _offset = offset;

        public readonly int Left => _bytes.Length;

        public readonly string Path => path;

        /// <summary>Where in the file the next byte is.</summary>
        public readonly long Offset => _offset;

        public ReadOnlySpan<byte> Bytes(int count, string rule)
        {
            if (count < 0 || count > _bytes.Length)
            {
                throw Refuse(path, _offset, rule, $"{count:N0} bytes are needed where {_bytes.Length:N0} are left");
            }

            var taken = _bytes[..count];
            _bytes = _bytes[count..];
            _offset += count;
            return taken;
        }

        /// <summary>The next bytes as a record of their own.</summary>
        public Reader Part(int count, string rule)
        {
            var start = _offset;
            return new Reader(Bytes(count, rule), path, start);
        }

        public byte Byte(string rule) => Bytes(1, rule)[0];

        public ushort UInt16(string rule) => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(2, rule));

        public int Int32(string rule) => BinaryPrimitives.ReadInt32LittleEndian(Bytes(4, rule));

        /// <summary>A number in groups of 7 bits, at most <paramref name="groups"/>; only its length matters, except a payload's size.</summary>
        public ulong VarUInt(int groups)
        {
            var start = _offset;
            var value = 0UL;
            for (var i = 0; i < groups; i++)
            {
                var part = Byte("number");
                value |= (ulong)(part & 0x7F) << (7 * i);
                if ((part & 0x80) == 0)
                {
                    return value;
                }
            }

            throw Refuse(path, start, "number", $"a number is longer than {groups} bytes");
        }

        /// <summary>UTF-16 text up to its terminating zero.</summary>
        public void Text(string rule)
        {
            while (UInt16(rule) != 0)
            {
                // Only its end matters.
            }
        }
    }
}
