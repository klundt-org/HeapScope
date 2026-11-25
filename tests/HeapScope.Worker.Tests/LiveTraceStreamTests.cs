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

using HeapScope.Core;
using HeapScope.Loaders.Trace;

namespace HeapScope.Worker.Tests;

/// <summary>A growing trace as TraceEvent reads it (sprint 4.3): whole checked objects only, and a clean end wherever it ends.</summary>
public sealed class LiveTraceStreamTests : IDisposable
{
    private static readonly byte[] s_trace = TraceFileCheckTests.Trace(
        TraceFileCheckTests.Block(false, TraceFileCheckTests.Description(TraceFileCheckTests.Fields(TraceFileCheckTests.Plain(9, "count")))),
        TraceFileCheckTests.Block(true, TraceFileCheckTests.Description([])));

    private readonly string _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"heapscope-live-{Guid.NewGuid():N}.nettrace");
    private readonly CancellationTokenSource _stop = new();
    private volatile bool _ended;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_file_written_a_byte_at_a_time_is_handed_out_whole()
    {
        using var writer = Writer();
        using var live = Open();
        var reading = Task.Run(() => ReadAll(live), Ct);
        foreach (var b in s_trace)
        {
            writer.WriteByte(b);
            await writer.FlushAsync(Ct);
        }

        (await reading.WaitAsync(TimeSpan.FromSeconds(30), Ct)).ShouldBe(s_trace);
        live.Checked!.End.ShouldBe(TraceCheckResult.Whole);
        (live.Refusal, live.BytesRead).ShouldBe((null, s_trace.Length - 1), "the end tag is the stream's own");
    }

    [Fact]
    public void A_recording_that_ends_anywhere_ends_with_the_objects_it_holds_whole()
    {
        var ends = new HashSet<int>();
        for (var length = 0; length < s_trace.Length; length++)
        {
            File.WriteAllBytes(_path, s_trace[..length]);
            _ended = true;
            using var live = Open();

            var read = ReadAll(live);

            live.Refusal.ShouldBeNull();
            if (read.Length > 0)
            {
                read[^1].ShouldBe((byte)1, "a clean end");
                read[..^1].ShouldBe(s_trace[..(read.Length - 1)]);
                ends.Add(read.Length - 1);
            }
        }

        ends.Count.ShouldBe(4, "after the head, the start, and each block");
    }

    [Fact]
    public async Task Nothing_of_an_object_is_handed_out_before_all_of_it_is_there()
    {
        using var writer = Writer();
        using var live = Open();
        var half = s_trace.Length - 8;
        await writer.WriteAsync(s_trace.AsMemory(0, half), Ct);
        var reading = Task.Run(() => ReadAll(live), Ct);
        await WaitUntilAsync(() => live.CaughtUp && live.BytesRead > 0);
        live.BytesRead.ShouldBeLessThan(half);
        reading.IsCompleted.ShouldBeFalse("it waits for more");

        await writer.WriteAsync(s_trace.AsMemory(half), Ct);

        (await reading.WaitAsync(TimeSpan.FromSeconds(30), Ct)).ShouldBe(s_trace);
    }

    [Fact]
    public void What_the_check_refuses_ends_the_stream_before_it()
    {
        var sound = TraceFileCheckTests.Trace(TraceFileCheckTests.Block(false, TraceFileCheckTests.Description([])));
        var bad = TraceFileCheckTests.Trace(TraceFileCheckTests.Block(false, TraceFileCheckTests.Description([])), TraceFileCheckTests.Block(false, TraceFileCheckTests.Description(TraceFileCheckTests.Int(1000))));
        File.WriteAllBytes(_path, bad);
        using var live = Open();

        var read = ReadAll(live);

        live.Refusal.ShouldNotBeNull().Message.ShouldContain("(field count)");
        read.ShouldBe(sound, "the blocks before it, and an end");
    }

    [Fact]
    public async Task A_file_that_becomes_shorter_ends_the_stream_with_a_reason()
    {
        await File.WriteAllBytesAsync(_path, s_trace[..^1], Ct);
        using var live = Open();
        var reading = Task.Run(() => ReadAll(live), Ct);
        await WaitUntilAsync(() => live.CaughtUp && live.BytesRead > 0);

        using (var other = new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            other.SetLength(40);
        }

        await reading.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        live.Refusal.ShouldNotBeNull().Message.ShouldContain("became shorter");
    }

    [Fact]
    public async Task Stopping_ends_a_reader_that_waits()
    {
        await File.WriteAllBytesAsync(_path, s_trace[..^1], Ct);
        using var live = Open();
        var reading = Task.Run(() => ReadAll(live), Ct);
        await WaitUntilAsync(() => live.CaughtUp);

        await _stop.CancelAsync();

        await reading.WaitAsync(TimeSpan.FromSeconds(30), Ct);
    }

    [Fact]
    public void Another_version_of_the_format_is_refused_at_once()
    {
        File.WriteAllBytes(_path, TraceFileCheckTests.Trace(6));
        using var live = Open();

        ReadAll(live).Length.ShouldBeLessThanOrEqualTo(33);
        live.Refusal.ShouldBeOfType<SnapshotFormatException>().Message.ShouldContain("format version 6");
    }

    [Fact]
    public void The_call_stack_layout_is_the_one_TraceEvent_has_and_another_gives_no_stacks()
    {
        var (reader, problem) = EventStackReader.Create();
        (reader is null, problem).ShouldBe((false, null));

        EventStackReader.Create(typeof(string)).ShouldBe((null, "String.eventRecord is not there"));
        EventStackReader.Create(typeof(NoPointer)).Problem.ShouldBe("NoPointer.eventRecord is not a pointer");
    }

    /// <summary>A reader with nothing to read yet, ended at once: no problem, no samples.</summary>
    [Fact]
    public async Task A_recording_that_wrote_nothing_is_no_problem()
    {
        await File.WriteAllBytesAsync(_path, [], Ct);
        using var reader = LiveTraceReader.Start(_path, namesPath: _path + ".gone");
        reader.End();
        await reader.Completion.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        var status = reader.Status();
        (status.Completed, status.Problem, status.Samples, status.DelayMs).ShouldBe((true, null, 0L, null));
        status.NamesProblem.ShouldNotBeNull().ShouldContain("FileNotFoundException");
        reader.View().SampleCount.ShouldBe(0);
        reader.Failure.ShouldBeNull();
    }

    public void Dispose()
    {
        _stop.Dispose();
        File.Delete(_path);
    }

    private LiveTraceStream Open() => new(_path, () => _ended, _stop.Token);

    private FileStream Writer() => new(_path, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete, 1);

    private static byte[] ReadAll(LiveTraceStream stream)
    {
        using var all = new MemoryStream();
        stream.CopyTo(all);
        return all.ToArray();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "condition not met in time");
            await Task.Delay(5, Ct);
        }
    }

#pragma warning disable CS0169, IDE0044, IDE0051, IDE1006
    private class NoPointer
    {
        private int eventRecord;
    }
#pragma warning restore CS0169, IDE0044, IDE0051, IDE1006
}
