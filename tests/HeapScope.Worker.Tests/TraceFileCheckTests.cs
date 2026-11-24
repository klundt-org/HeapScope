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

using System.Text;
using HeapScope.Core;
using HeapScope.Loaders.Trace;

namespace HeapScope.Worker.Tests;

/// <summary>What a trace file states must fit in it (4.2.1). The files are built here, small; none reaches TraceEvent.</summary>
public sealed class TraceFileCheckTests
{
    private const string Path = "crafted.nettrace";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Descriptions_that_fit_pass(bool compressed)
    {
        Check(Trace(Block(compressed, Description(Fields(Plain(9, "count"), Plain(18, "name"), Struct("inner", Plain(8, "a"), Plain(17, "b")))), Description([]), Description(Fields()))));
    }

    [Fact]
    public void An_optional_list_of_sized_fields_passes()
    {
        Check(Trace(Block(false, Description([.. Fields(), .. Tagged(2, SizedFields(Sized("array", 19, 9), Sized("plain", 11))), .. Tagged(1, [7])]))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(-1)]
    public void A_field_count_the_record_cannot_hold_is_refused(int count)
    {
        Refused(Trace(Block(false, Description(Int(count))))).ShouldContain("fields in 0 bytes");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_count_inside_a_struct_or_a_later_list_is_checked_too(bool sized)
    {
        var fields = sized ? [.. Fields(), .. Tagged(2, Int(500))] : (byte[])[.. Int(1), .. Int(1), .. Int(500)];

        Refused(Trace(Block(true, Description(fields)))).ShouldContain("describes an event of");
    }

    [Theory]
    [InlineData(TraceFileCheck.DeepestType, false)]
    [InlineData(TraceFileCheck.DeepestType + 1, true)]
    public void Types_nested_past_the_limit_are_refused(int levels, bool refused)
    {
        // A field whose type is a struct of one field, and so on; the innermost has none. A name ends each.
        byte[] fields = Int(0);
        for (var i = 0; i < levels; i++)
        {
            fields = [.. Int(1), .. Int(1), .. fields, .. Text(string.Empty)];
        }

        var trace = Trace(Block(false, Description(fields)));
        if (refused)
        {
            Refused(trace).ShouldContain("nested more than");
        }
        else
        {
            Check(trace);
        }
    }

    [Fact]
    public void Arrays_of_arrays_past_the_limit_are_refused()
    {
        var types = Enumerable.Repeat(19, TraceFileCheck.DeepestType + 1).Append(9).ToArray();

        Refused(Trace(Block(false, Description([.. Fields(), .. Tagged(2, SizedFields(Sized("deep", types)))])))).ShouldContain("nested more than");
    }

    [Fact]
    public void A_type_this_format_does_not_have_is_left_to_the_reader()
    {
        // As TraceEvent: it ignores the fields of a description it cannot read, so a newer runtime's trace still opens.
        Check(Trace(Block(false, Description([.. Fields(), .. Tagged(2, SizedFields(Sized("later", 99)))]))));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100 * 1024 * 1024)]
    public void A_block_size_the_file_cannot_hold_is_refused(int size)
    {
        Refused(Trace(new Placed("EventBlock", [], size))).ShouldContain("block stated to be");
    }

    [Fact]
    public void A_recording_cut_short_is_left_to_the_reader_wherever_it_ends()
    {
        var whole = Trace(Block(false, Description(Fields(Plain(9, "count")))), new Placed("EventBlock", new byte[200], 200));
        for (var length = 0; length <= whole.Length; length++)
        {
            Check(whole[..length]);
        }
    }

    [Fact]
    public void Another_version_of_the_format_is_not_walked()
    {
        Check([.. "Nettrace"u8, .. Int(0), .. Int(6), .. new byte[40]]);
    }

    [Fact]
    public void Damage_anywhere_is_passed_or_refused_and_never_fails_otherwise()
    {
        var whole = Trace(
            Block(true, Description(Fields(Plain(9, "count"), Struct("inner", Plain(8, "a")))), Description([.. Fields(), .. Tagged(2, SizedFields(Sized("array", 19, 9)))])),
            Block(false, Description(Fields(Plain(18, "n")))));
        var random = new Random(421);
        for (var i = 0; i < 20_000; i++)
        {
            var damaged = (byte[])whole.Clone();
            for (var changes = random.Next(1, 4); changes > 0; changes--)
            {
                damaged[random.Next(damaged.Length)] = (byte)random.Next(256);
            }

            try
            {
                Check(damaged);
            }
            catch (SnapshotFormatException)
            {
                // Refused with a reason.
            }
        }
    }

    private static void Check(byte[] trace) => TraceFileCheck.Check(new MemoryStream(trace), Path, TestContext.Current.CancellationToken);

    private static string Refused(byte[] trace)
    {
        var message = Should.Throw<SnapshotFormatException>(() => Check(trace)).Message;
        message.ShouldStartWith(Path);
        message.ShouldContain("damaged or crafted; record the trace again");
        return message;
    }

    private static byte[] Trace(params Placed[] objects)
    {
        var file = new List<byte>([.. "Nettrace"u8, .. Int(20), .. "!FastSerialization.1"u8]);
        foreach (var (type, body, statedSize) in objects.Prepend(new Placed("Trace", new byte[48], 48)))
        {
            file.AddRange([5, 5, 1, .. Int(4), .. Int(4), .. Int(type.Length), .. Encoding.ASCII.GetBytes(type), 6]);
            if (type != "Trace")
            {
                // A block states its size and starts at a multiple of 4 in the file.
                file.AddRange(Int(statedSize));
                file.AddRange(new byte[(4 - (file.Count % 4)) % 4]);
            }

            file.AddRange([.. body, 6]);
        }

        return [.. file, 1];
    }

    private static Placed Block(bool compressed, params byte[][] descriptions)
    {
        var records = descriptions.SelectMany(d => compressed ? (byte[])[0x83, 0, 5, 9, 1, 3, .. VarUInt(d.Length), .. d] : [.. new byte[76], .. Int(d.Length), .. d]);
        byte[] body = [20, 0, (byte)(compressed ? 1 : 0), 0, .. new byte[16], .. records];
        return new Placed("MetadataBlock", body, body.Length);
    }

    private sealed record Placed(string Type, byte[] Body, int StatedSize);

    private static byte[] Description(byte[] fields) =>
        [.. Int(1), .. Text("Provider"), .. Int(5), .. Text("Event"), .. new byte[16], .. fields];

    private static byte[] Fields(params byte[][] fields) => [.. Int(fields.Length), .. fields.SelectMany(f => f)];

    private static byte[] Plain(int type, string name) => [.. Int(type), .. Text(name)];

    private static byte[] Struct(string name, params byte[][] fields) => [.. Int(1), .. Fields(fields), .. Text(name)];

    private static byte[] SizedFields(params byte[][] fields) => [.. Int(fields.Length), .. fields.SelectMany(f => f)];

    private static byte[] Sized(string name, params int[] types)
    {
        byte[] field = [.. Text(name), .. types.SelectMany(Int)];
        return [.. Int(field.Length + 4), .. field];
    }

    private static byte[] Tagged(byte tag, byte[] body) => [.. Int(body.Length), tag, .. body];

    private static byte[] Int(int value) => BitConverter.GetBytes(value);

    private static byte[] Text(string text) => [.. Encoding.Unicode.GetBytes(text), 0, 0];

    private static byte[] VarUInt(int value)
    {
        var bytes = new List<byte>();
        for (var rest = (uint)value; ; rest >>= 7)
        {
            if (rest < 128)
            {
                bytes.Add((byte)rest);
                return [.. bytes];
            }

            bytes.Add((byte)(rest | 128));
        }
    }
}
