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

    [Fact]
    public void A_field_that_is_not_there_is_refused() => Refused(Trace(Block(false, Description(Int(1))))).ShouldContain("(field type)");

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(-1)]
    public void A_field_count_the_record_cannot_hold_is_refused(int count)
    {
        Refused(Trace(Block(false, Description(Int(count))))).ShouldContain("(field count): an event is described with");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_count_inside_a_struct_or_a_later_list_is_checked_too(bool sized)
    {
        var fields = sized ? [.. Fields(), .. Tagged(2, Int(500))] : (byte[])[.. Int(1), .. Int(1), .. Int(500)];

        Refused(Trace(Block(true, Description(fields)))).ShouldContain("(field count): an event is described with");
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
            Refused(trace).ShouldContain("(nesting)");
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

        Refused(Trace(Block(false, Description([.. Fields(), .. Tagged(2, SizedFields(Sized("deep", types)))])))).ShouldContain("(nesting)");
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
        Refused(Trace(new Placed("EventBlock", [], size))).ShouldContain("(block size): a block is stated to be");
    }

    [Fact]
    public void A_recording_cut_short_is_left_to_the_reader_wherever_it_ends()
    {
        var whole = Trace(Block(false, Description(Fields(Plain(9, "count")))), new Placed("EventBlock", new byte[200], 200));
        for (var length = 32; length <= whole.Length; length++)
        {
            Check(whole[..length]);
        }
    }

    [Fact]
    public void A_type_this_format_does_not_have_in_the_first_list_ends_at_the_tags_the_reader_would_read()
    {
        // TraceEvent reads the tags from where it gave up the list, so what follows is checked as tags: here they are not.
        Refused(Trace(Block(false, Description([.. Int(1), .. Plain(99, "later")])))).ShouldContain("(tag)");
    }

    [Fact]
    public void Lists_that_fit_alone_but_not_together_are_refused()
    {
        // A description's lists share one budget: the reader keeps room for all of them at once. The later list here states what
        // is left of the record, on top of the first one's fields.
        var first = Fields(Plain(9, "a"), Plain(9, "b"), Plain(9, "c"), Plain(9, "d"));
        Check(Trace(Block(false, Description([.. first, .. Tagged(2, SizedFields(Sized("e", 9)))]))));
        var room = Description([.. first, .. Tagged(2, Int(0))]).Length / 6;
        Refused(Trace(Block(false, Description([.. first, .. Tagged(2, Int(room - 3))])))).ShouldContain("(field count)");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(0)]
    public void Another_version_of_the_format_is_refused(int version)
    {
        // Nothing checks it, so it does not reach the reader.
        Refused(Trace(version, Block(false, Description(Fields())))).ShouldContain("(version)");
    }

    [Fact]
    public void A_head_of_another_format_is_refused()
    {
        Should.Throw<SnapshotFormatException>(() => Check([.. "Nettrace"u8, .. Int(0), .. Int(6), .. new byte[40]])).Message.ShouldContain("version of the trace format HeapScope does not read");
    }

    [Fact]
    public void A_second_start_is_refused()
    {
        Refused(Trace(new Placed("Trace", new byte[48], 48))).ShouldContain("(version)");
    }

    [Fact]
    public void A_block_of_descriptions_larger_than_the_limit_is_refused_before_it_is_read()
    {
        Refused(Trace(new Placed("MetadataBlock", new byte[TraceFileCheck.LargestDescriptions + 4], TraceFileCheck.LargestDescriptions + 4))).ShouldContain("(description block size)");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(0)]
    public void Every_part_of_a_compressed_header_is_stepped_over(int flag)
    {
        // The second record carries no size of its own: the first one's holds.
        var description = Description(Fields(Plain(9, "count")));
        byte[] parts = flag switch
        {
            1 or 8 => [0x85, 1],
            2 => [1, 0x81, 1, 2],
            4 => [0xFF, 0xFF, 1],
            16 or 32 => new byte[16],
            _ => [],
        };
        byte[] before = flag is 16 or 32 ? [] : parts;
        byte[] after = flag is 16 or 32 ? parts : [];
        byte[] records =
        [
            0x80, 7, .. VarUInt(description.Length), .. description,
            (byte)flag, .. before, 7, .. after, .. description,
        ];
        byte[] body = [20, 0, 1, 0, .. new byte[16], .. records];

        var result = Check(Trace(new Placed("MetadataBlock", body, body.Length)));

        result.ShouldBe(result with { End = TraceCheckResult.Whole, Blocks = 2, Descriptions = 2, Fields = 2 });
    }

    [Fact]
    public void A_number_that_does_not_end_is_refused()
    {
        byte[] body = [20, 0, 1, 0, .. new byte[16], 0x81, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 1, 7, 0];

        Refused(Trace(new Placed("MetadataBlock", body, body.Length))).ShouldContain("(number)");
    }

    [Fact]
    public void A_refusal_says_where_in_the_file_it_was()
    {
        var trace = Trace(Block(false, Description(Int(12345))));
        var at = trace.AsSpan().IndexOf(Int(12345));

        Refused(trace).ShouldContain($"at byte {at:N0} ");
    }

    [Fact]
    public void What_was_walked_is_said()
    {
        var whole = Trace(Block(false, Description(Fields(Plain(9, "count")))), new Placed("EventBlock", new byte[200], 200));

        Check(whole).ToString().ShouldBe($"check {TraceFileCheck.Version}: whole at byte {whole.Length - 1}, 3 blocks, 1 descriptions, 1 fields");
        Check(whole[..^50]).End.ShouldBe(TraceCheckResult.CutShort);
        whole[^1] = 9;
        Check(whole).End.ShouldBe(TraceCheckResult.OtherTag);
    }

    [Fact]
    public void A_canceled_check_stops()
    {
        Should.Throw<OperationCanceledException>(() => TraceFileCheck.Check(new MemoryStream(Trace()), Path, new CancellationToken(canceled: true)));
    }

    [Fact]
    public void A_file_refused_by_the_check_never_reaches_the_reader()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"heapscope-check-{Guid.NewGuid():N}.nettrace");
        File.WriteAllBytes(path, Trace(Block(false, Description(Int(1000)))));
        try
        {
            var refusal = Should.Throw<SnapshotFormatException>(() => AllocationTraceReader.Read(path, null, System.IO.Path.GetTempPath(), TestContext.Current.CancellationToken));

            refusal.Message.ShouldStartWith(path);
            refusal.Message.ShouldContain("(field count)");
        }
        finally
        {
            File.Delete(path);
        }
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

    private static TraceCheckResult Check(byte[] trace) => TraceFileCheck.Check(new MemoryStream(trace), Path, TestContext.Current.CancellationToken);

    private static string Refused(byte[] trace)
    {
        var message = Should.Throw<SnapshotFormatException>(() => Check(trace)).Message;
        message.ShouldStartWith(Path);
        message.ShouldContain(" does not match the trace format at byte ");
        message.ShouldContain("otherwise record the trace again");
        return message;
    }

    private static byte[] Trace(params Placed[] objects) => Trace(4, objects);

    private static byte[] Trace(int version, params Placed[] objects)
    {
        var file = new List<byte>([.. "Nettrace"u8, .. Int(20), .. "!FastSerialization.1"u8]);
        foreach (var (type, body, statedSize) in objects.Prepend(new Placed("Trace", new byte[48], 48)))
        {
            var stated = type == "Trace" ? version : 2;
            file.AddRange([5, 5, 1, .. Int(stated), .. Int(stated), .. Int(type.Length), .. Encoding.ASCII.GetBytes(type), 6]);
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
