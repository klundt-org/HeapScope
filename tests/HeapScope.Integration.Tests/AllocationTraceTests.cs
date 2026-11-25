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

using System.Globalization;
using HeapScope.Analysis.Allocations;
using HeapScope.Application.Allocations;
using HeapScope.Application.Jobs;
using HeapScope.Application.Sessions;
using HeapScope.Application.Workers;
using HeapScope.Capture;
using HeapScope.Contracts;
using HeapScope.Core;
using HeapScope.Core.Allocations;
using HeapScope.Integration.Tests.Infrastructure;
using HeapScope.Loaders.Trace;
using Microsoft.Extensions.Options;

namespace HeapScope.Integration.Tests;

/// <summary>One allocations trace of LeakyApi under LoadGen (sprint 2.5), shared by the tests that read it.</summary>
public sealed class LeakyApiAllocations : IAsyncLifetime
{
    private readonly TempFolder _root = new();

    public FixtureProcess Api { get; private set; } = null!;

    public string TracePath => _root.File("allocations.nettrace");

    public TraceRecording Recording { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Api = await FixtureProcess.StartApiAsync();

        // The leaks' methods are compiled before recording starts, so only the rundown names them (the 2.5 review).
        await Api.PostWorkAsync(1, CancellationToken.None);
        using var stop = new CancellationTokenSource();
        var recording = TraceRecorder.RecordAsync(Api.ProcessId, TracePath, TraceProfile.Allocations, TimeSpan.FromMinutes(5), stop.Token);

        // Load starts once the session streams (its partial file has bytes), not after a fixed second: under a full test run the
        // session's start took longer than the load, and the stop then canceled the start (sprint 3.11's wrap, and 3.10's gate).
        var folder = Path.GetDirectoryName(TracePath)!;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (!Directory.EnumerateFiles(folder, TraceRecorder.PartialPattern(TracePath)).Any(static f => new FileInfo(f).Length > 0))
        {
            if (recording.IsCompleted)
            {
                await recording;
                throw new InvalidOperationException("The recording ended before its session started.");
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The allocation recording's session did not start within 60 seconds.");
            }

            await Task.Delay(50);
        }

        using (var load = ChildProcess.Start(TestPaths.LoadGenExe, "--url", Api.Url!.ToString(), "--requests", "2000", "--concurrency", "8"))
        {
            if (!await load.WaitForExitAsync(TimeSpan.FromMinutes(3)) || load.Process.ExitCode != 0)
            {
                throw new InvalidOperationException("LoadGen did not finish: " + load.Output);
            }
        }

        await stop.CancelAsync();
        Recording = await recording;
    }

    public ValueTask DisposeAsync()
    {
        Api?.Dispose();
        _root.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Sprint 2.5: reading allocation traces, the queries through the real worker, and the acceptance criterion on LeakyApi.</summary>
[Trait("Category", "Integration")]
public sealed class AllocationTraceTests : IClassFixture<LeakyApiAllocations>, IAsyncDisposable
{
    private readonly LeakyApiAllocations _shared;
    private readonly TempFolder _scratch = new();
    private readonly WorkerHost _host = new(Options.Create(new WorkerHostOptions { WorkerPath = TestPaths.WorkerExe }), new ListLoggerProvider());
    private readonly AllocationSession _session;
    private readonly SessionLibrary _library;

    public AllocationTraceTests(LeakyApiAllocations shared)
    {
        _shared = shared;

        // Never the user's session folders (tests must not write there).
        _library = new SessionLibrary(_scratch.File("sessions"));
        _session = new AllocationSession(_host, new JobTracker(TimeProvider.System), new TraceService(), _library);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Sprint 4.3 (ADR-037): a live reader beside a recording adds up to what the finished file holds, type by type and stack by
    /// stack, however the file grows.
    /// </summary>
    [Theory]
    [InlineData(1 << 20)]
    [InlineData(100_003)]
    public async Task A_recording_replayed_in_pieces_reads_as_the_finished_file(int piece)
    {
        var finished = AllocationTraceReader.Read(_shared.TracePath, null, _scratch.Path, Ct);
        var bytes = await File.ReadAllBytesAsync(_shared.TracePath, Ct);
        var path = _scratch.File($"replayed-{piece}.nettrace");
        await using var writer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete, 1);
        using var reader = LiveTraceReader.Start(path, namesPath: null);
        for (var at = 0; at < bytes.Length; at += piece)
        {
            await writer.WriteAsync(bytes.AsMemory(at, Math.Min(piece, bytes.Length - at)), Ct);
            await Task.Delay(1, Ct);
        }

        reader.End();
        await reader.Completion.WaitAsync(TimeSpan.FromSeconds(60), Ct);

        var status = reader.Status();
        (status.Problem, status.StackProblem, status.Completed, reader.Failure).ShouldBe((null, null, true, null));
        status.Checked.ShouldNotBeNull().ShouldContain("whole");
        var live = reader.View().Trace;
        Types(live).ShouldBe(Types(finished));
        var (liveStacks, finishedStacks) = (Stacks(live), Stacks(finished));
        liveStacks.Keys.Except(finishedStacks.Keys).Take(3).ShouldBeEmpty("stacks only the live reader has");
        finishedStacks.Keys.Except(liveStacks.Keys).Take(3).ShouldBeEmpty("stacks only the finished file has");
        liveStacks.Where(s => finishedStacks[s.Key] != s.Value).Take(3).ShouldBeEmpty();
        (live.Info.ProcessId, live.Info.LostEvents, live.Info.HasRundown, live.Info.SamplesWithoutStack).ShouldBe(
            (finished.Info.ProcessId, finished.Info.LostEvents, finished.Info.HasRundown, finished.Info.SamplesWithoutStack));

        static Dictionary<string, (long Bytes, int Samples)> Types(AllocationTrace trace) =>
            AllocationProfile.Types(trace, AllocationFilter.All).ToDictionary(t => trace.Types[t.Type], t => (t.Bytes, t.Samples), StringComparer.Ordinal);

        static Dictionary<string, (long Bytes, int Samples)> Stacks(AllocationTrace trace)
        {
            var stacks = new Dictionary<string, (long Bytes, int Samples)>(StringComparer.Ordinal);
            foreach (var (bytes, node, samples) in AllocationProfile.TopStacks(trace, AllocationFilter.All, int.MaxValue))
            {
                // Unnamed frames read the same whatever module the finished file knows for them.
                var key = string.Join(" < ", AllocationProfile.FramesOf(trace, node).Select(f => trace.Frames[f].StartsWith("[unresolved frame", StringComparison.Ordinal) ? "?" : trace.Frames[f]));
                var (b, n) = stacks.GetValueOrDefault(key);
                stacks[key] = (b + bytes, n + samples);
            }

            return stacks;
        }
    }

    [Fact]
    public void The_trace_holds_samples_of_LeakyApi_with_named_stacks_and_leaves_no_temporary_file()
    {
        var trace = AllocationTraceReader.Read(_shared.TracePath, null, _scratch.Path, Ct);

        trace.SampleCount.ShouldBeGreaterThan(100, "2,000 requests allocate megabytes, a sample about every 100 KB");
        trace.Info.ProcessId.ShouldBe(_shared.Api.ProcessId);
        trace.SampleBytes.Sum().ShouldBeGreaterThan(10_000_000);
        trace.Types.ShouldContain("System.Byte[]");
        trace.Frames.ShouldContain(f => f.Contains("Leak.Grow", StringComparison.Ordinal), "methods compiled before recording are named from the rundown");
        trace.Info.HasRundown.ShouldBeTrue();
        trace.Info.ConversionIssues.ShouldBe(0, string.Join(" | ", trace.Info.FirstIssues ?? []));
        trace.Info.SamplesWithUnresolvedFrames.ShouldBeLessThan(trace.SampleCount / 20, "nearly every frame is named");
        trace.SampleKinds.ShouldContain(AllocationKind.Small);
        Directory.GetFiles(_scratch.Path).ShouldBeEmpty("the .etlx is deleted after reading");
    }

    [Fact]
    public void Acceptance_the_leaking_allocation_site_is_in_the_top_call_stacks()
    {
        var trace = AllocationTraceReader.Read(_shared.TracePath, null, _scratch.Path, Ct);

        var top = AllocationProfile.TopStacks(trace, AllocationFilter.All, 10, Ct);
        var stacks = top.Select(s => AllocationProfile.FramesOf(trace, s.Node).Select(f => trace.Frames[f]).ToArray()).ToArray();
        var report = string.Join(Environment.NewLine, top.Zip(stacks, (s, frames) => string.Create(CultureInfo.InvariantCulture, $"{s.Bytes:N0} B: {string.Join(" < ", frames.Take(4))}")));
        stacks.ShouldContain(frames => frames.Any(f => f.StartsWith("Fixtures.Leaks.", StringComparison.Ordinal) && f.Contains("Leak.Grow", StringComparison.Ordinal)), report);
    }

    [Fact]
    public void The_check_walks_the_whole_of_a_trace_the_runtime_wrote()
    {
        // 4.2.1: every block to the end tag, and the descriptions in them. A walk that stopped early would pass quietly.
        using var stream = new FileStream(_shared.TracePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var result = TraceFileCheck.Check(stream, _shared.TracePath, TestContext.Current.CancellationToken);

        result.End.ShouldBe(TraceCheckResult.Whole);
        result.Bytes.ShouldBe(stream.Length - 1);
        result.Descriptions.ShouldBeGreaterThan(10);
    }

    [Theory]
    [InlineData(0.9)]
    [InlineData(0.6)]
    [InlineData(0.35)]
    public void A_trace_cut_short_still_opens_with_what_came_before(double part)
    {
        var bytes = File.ReadAllBytes(_shared.TracePath);
        var path = _scratch.File($"cut-{part.ToString(CultureInfo.InvariantCulture)}.nettrace");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes[..(int)(bytes.Length * part)]);

        var cut = AllocationTraceReader.Read(path, null, _scratch.File("etlx"), TestContext.Current.CancellationToken);

        cut.SampleCount.ShouldBeGreaterThan(0);
        cut.Info.IsIncomplete.ShouldBeTrue();
        cut.Info.Checked.ShouldNotBeNull().ShouldContain(TraceCheckResult.CutShort);
    }

    [Fact]
    public async Task The_check_accepts_what_an_event_source_describes()
    {
        // Numbers, text, a Guid, structs, arrays, arrays of structs, as the runtime describes them.
        var path = _scratch.File("source.nettrace");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var source = new System.Diagnostics.Tracing.EventSource("HeapScope-Check-Test", System.Diagnostics.Tracing.EventSourceSettings.EtwSelfDescribingEventFormat);
        using (var manifest = new DescribedEvents())
        {
            var client = new Microsoft.Diagnostics.NETCore.Client.DiagnosticsClient(Environment.ProcessId);
            Microsoft.Diagnostics.NETCore.Client.EventPipeProvider[] providers =
            [
                new("HeapScope-Check-Test", System.Diagnostics.Tracing.EventLevel.Verbose),
                new("HeapScope-Check-Manifest", System.Diagnostics.Tracing.EventLevel.Verbose),
            ];
            using var session = await client.StartEventPipeSessionAsync(providers, requestRundown: false, token: TestContext.Current.CancellationToken);
            await using var file = File.Create(path);
            var copy = session.EventStream.CopyToAsync(file, TestContext.Current.CancellationToken);
            source.Write("Rich", new { Count = 1, Name = "n", Inner = new { A = 2L, Deep = new { B = 3.5 } }, Numbers = new[] { 1, 2 }, Many = new[] { new { C = 1 } } });
            manifest.Plain(1, "text", 2.5, Guid.Empty);
            manifest.Bytes([1, 2, 3]);
            await Task.Delay(300, TestContext.Current.CancellationToken);
            await session.StopAsync(TestContext.Current.CancellationToken);
            await copy;
        }

        await using var written = File.OpenRead(path);
        var result = TraceFileCheck.Check(written, path, TestContext.Current.CancellationToken);

        result.End.ShouldBe(TraceCheckResult.Whole);
        result.Descriptions.ShouldBeGreaterThanOrEqualTo(3);
        // Plain's 4 and the 3 of EventPipe's own ProcessInfo. .NET 10 describes the self-describing and the array event with no
        // fields, so the later list of sized fields is tested by hand only (TraceFileCheckTests).
        result.Fields.ShouldBeGreaterThanOrEqualTo(7);
    }

    [System.Diagnostics.Tracing.EventSource(Name = "HeapScope-Check-Manifest")]
    private sealed class DescribedEvents : System.Diagnostics.Tracing.EventSource
    {
        [System.Diagnostics.Tracing.Event(1)]
        public void Plain(int count, string name, double amount, Guid id) => WriteEvent(1, count, name, amount, id);

        [System.Diagnostics.Tracing.Event(2)]
        public void Bytes(byte[] data) => WriteEvent(2, data);
    }

    [Fact]
    public async Task A_recording_whose_process_exits_has_no_rundown_and_says_so()
    {
        using var api = await FixtureProcess.StartApiAsync();
        await api.PostWorkAsync(1, Ct);
        var path = _scratch.File("exited.nettrace");
        var recording = TraceRecorder.RecordAsync(api.ProcessId, path, TraceProfile.Allocations, TimeSpan.FromMinutes(2), Ct);
        await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        var before = PartialBytes(path);
        await api.PostWorkAsync(200, Ct);

        // The allocations reach the file only when the session flushes its buffers: wait until they did, so killing the
        // process loses the rundown and nothing else (a burst killed before its first flush recorded no samples at all).
        for (var waited = 0; PartialBytes(path) < before + 16_384 && waited < 150; waited++)
        {
            await Task.Delay(100, Ct);
        }

        api.Dispose();
        (await recording).StoppedEarly.ShouldBeTrue();

        var handle = await _session.StartOpen(path).Result;

        handle.Descriptor.HasRundown.ShouldBeFalse();
        AllocationTexts.NotesFor(handle.Descriptor).ShouldContain("before the runtime named its methods");
    }

    /// <summary>The bytes a recording into <paramref name="path"/> has written so far (its partial file).</summary>
    private static long PartialBytes(string path) =>
        Directory.EnumerateFiles(Path.GetDirectoryName(path)!, HeapScope.Capture.TraceRecorder.PartialPattern(path))
            .Select(f => new FileInfo(f).Length).DefaultIfEmpty(0).Max();

    [Fact]
    public async Task A_trace_with_no_allocation_samples_is_refused_with_the_profile_to_use()
    {
        var path = _scratch.File("cpu.nettrace");
        var recording = TraceRecorder.RecordAsync(_shared.Api.ProcessId, path, TraceProfile.Cpu, TimeSpan.FromSeconds(2), Ct);
        await recording;

        var ex = await Should.ThrowAsync<WorkerOperationException>(() => _session.StartOpen(path).Result);

        ex.ErrorCode.ShouldBe(HeapScopeErrorCode.SnapshotFormat);
        ex.Message.ShouldContain("has no allocation samples");
    }

    [Fact]
    public void A_file_that_is_not_a_trace_is_refused_with_a_plain_message()
    {
        var path = _scratch.File("notes.nettrace");
        File.WriteAllText(path, "These are notes, not a trace; long enough to read a header from.");

        var ex = Should.Throw<SnapshotFormatException>(() => AllocationTraceReader.Read(path, null, _scratch.File("etlx"), Ct));

        ex.Message.ShouldContain("is not an EventPipe trace");
    }

    [Fact]
    public void A_canceled_read_stops_and_leaves_no_temporary_file()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var folder = _scratch.File("canceled");

        Should.Throw<OperationCanceledException>(() => AllocationTraceReader.Read(_shared.TracePath, null, folder, cancel.Token));

        (Directory.Exists(folder) ? Directory.GetFiles(folder) : []).ShouldBeEmpty();
    }

    [Fact]
    public async Task Acceptance_through_the_worker_the_top_call_stacks_hold_the_leaking_allocation_site()
    {
        var handle = await _session.StartOpen(_shared.TracePath).Result;

        handle.Descriptor.Samples.ShouldBeGreaterThan(100);
        handle.Descriptor.ProcessId.ShouldBe(_shared.Api.ProcessId);
        var stacks = await _session.QueryStacksAsync(handle, AllocationScope.All, 10, Ct);
        stacks.SampledBytes.ShouldBe(handle.Descriptor.SampledBytes);
        stacks.Stacks.ShouldContain(s => s.Frames.Any(f => f.StartsWith("Fixtures.Leaks.", StringComparison.Ordinal) && f.Contains("Leak.Grow", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Types_trees_and_the_timeline_add_up_to_the_same_sampled_bytes()
    {
        var handle = await _session.StartOpen(_shared.TracePath).Result;
        var total = handle.Descriptor.SampledBytes;

        var types = await _session.QueryTypesAsync(handle, AllocationScope.All, Ct);
        var typeRows = await RetainedSizesTests.AllRowsAsync(types.Rows);
        typeRows.Sum(r => long.Parse(r[AllocationTypeColumns.Bytes]!, CultureInfo.InvariantCulture)).ShouldBe(total);
        types.Rows.Info.Columns.Single(c => c.Name == AllocationTypeColumns.Bytes).Bound.ShouldBe(ValueBound.Estimate);

        var timeline = await _session.QueryTimelineAsync(handle, 1, Ct);
        timeline.Buckets.Sum(b => b.Bytes).ShouldBe(total);

        var callTree = await _session.QueryTreeAsync(handle, AllocationScope.All, AllocationTreeKindDto.CallTree, [], null, null, Ct);
        var roots = await RetainedSizesTests.AllRowsAsync(callTree.Rows);
        roots.Where(r => r[AllocationTreeColumns.Kind] == AllocationRowKinds.Frame).Sum(r => long.Parse(r[AllocationTreeColumns.Total]!, CultureInfo.InvariantCulture))
            .ShouldBe(total, "the top level of the call tree covers every sample (no more rows on a small trace)");

        // Back traces: the first allocating frame's callers, revealed in place.
        var top = await _session.QueryTreeAsync(handle, AllocationScope.All, AllocationTreeKindDto.BackTraces, [], null, null, Ct);
        var first = (await RetainedSizesTests.AllRowsAsync(top.Rows))[0];
        var key = first[AllocationTreeColumns.Key]!;
        var open = await _session.QueryTreeAsync(handle, AllocationScope.All, AllocationTreeKindDto.BackTraces, [key], key, null, Ct);
        open.Result.RevealIndex.ShouldBe(0);
        var rows = await RetainedSizesTests.AllRowsAsync(open.Rows);
        rows.Where(r => r[AllocationTreeColumns.ParentKey] == key).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_type_or_an_interval_narrows_the_answer_and_bad_ones_are_refused()
    {
        var handle = await _session.StartOpen(_shared.TracePath).Result;
        var half = handle.Descriptor.DurationSeconds / 2;

        var bytes = await _session.QueryStacksAsync(handle, new AllocationScope(TypeName: "System.Byte[]"), 5, Ct);
        bytes.SampledBytes.ShouldBeLessThan(handle.Descriptor.SampledBytes);
        var early = await _session.QueryStacksAsync(handle, new AllocationScope(0, half), 5, Ct);
        var late = await _session.QueryStacksAsync(handle, new AllocationScope(half), 5, Ct);
        (early.SampledBytes + late.SampledBytes).ShouldBe(handle.Descriptor.SampledBytes);

        var unknown = await Should.ThrowAsync<WorkerOperationException>(() => _session.QueryStacksAsync(handle, new AllocationScope(TypeName: "No.Such.Type"), 5, Ct));
        unknown.ErrorCode.ShouldBe(HeapScopeErrorCode.InvalidArgument);
        var empty = await Should.ThrowAsync<WorkerOperationException>(() => _session.QueryStacksAsync(handle, new AllocationScope(2, 1), 5, Ct));
        empty.ErrorCode.ShouldBe(HeapScopeErrorCode.InvalidArgument);
    }

    [Fact]
    public async Task A_trace_the_worker_no_longer_has_is_read_again()
    {
        var handle = await _session.StartOpen(_shared.TracePath).Result;
        var id = handle.Descriptor.TraceId;
        await _host.InvokeAsync((service, ct) => service.CloseTraceAsync(id, ct), Ct);

        var timeline = await _session.QueryTimelineAsync(handle, 1, Ct);

        timeline.Buckets.ShouldNotBeEmpty();
        handle.Descriptor.TraceId.ShouldNotBe(id);
    }

    /// <summary>Sprint 4.3 (ADR-037): the worker follows a recording, answers for it, follows it again after a restart, and ends with it.</summary>
    [Fact]
    public async Task A_recording_is_followed_while_it_goes_on_across_a_worker_restart_to_its_end()
    {
        var path = _scratch.File("followed.nettrace");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var started = new TaskCompletionSource<LiveTraceStart>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recording = new TraceService().RecordLiveAsync(_shared.Api.ProcessId, path, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1), noNames: null, started.SetResult, stop.Token);
        var start = await started.Task.WaitAsync(TimeSpan.FromSeconds(60), Ct);
        var live = await _session.OpenLiveAsync(path, start.NamesPath, Ct);
        _session.Traces.ShouldNotContain(live, "it is the Live page's own");

        var first = await FollowAsync(live, s => s.Samples > 20);
        (first.ProcessId, first.HasStacks, first.Completed, first.Problem).ShouldBe((_shared.Api.ProcessId, true, false, null));
        first.DelaySeconds.ShouldNotBeNull();
        var types = await _session.QueryTypesAsync(live, AllocationScope.All, Ct);
        types.Result.Samples.ShouldBeGreaterThan(20);
        var stacks = await _session.QueryStacksAsync(live, AllocationScope.All, 20, Ct);
        stacks.Stacks.SelectMany(s => s.Frames).ShouldContain(f => f.Contains("Leak.Grow", StringComparison.Ordinal), "named by the rundown before the recording");
        (await _session.QueryTimelineAsync(live, 1, Ct)).Buckets.Sum(b => b.Samples).ShouldBeGreaterThan(20);

        // The worker is gone: the next question follows the file again, from its start.
        using (var worker = System.Diagnostics.Process.GetProcessById((await _host.InvokeAsync((service, ct) => service.GetInfoAsync(WorkerProtocol.Version, ct), Ct)).ProcessId))
        {
            worker.Kill();
            await worker.WaitForExitAsync(Ct);
        }

        var again = await FollowAsync(live, s => s.Samples >= first.Samples);
        again.StartUtc.ShouldBe(first.StartUtc, "the same recording");

        await stop.CancelAsync();
        var written = await recording;
        await _session.EndLiveAsync(live, Ct);
        var last = await FollowAsync(live, s => s.Completed, work: false);
        last.Problem.ShouldBeNull();
        var finished = await _session.StartOpen(written.Path).Result;
        (last.Samples, last.SampledBytes, last.LostEvents).ShouldBe((finished.Descriptor.Samples, finished.Descriptor.SampledBytes, finished.Descriptor.LostEvents));

        await _session.CloseLiveAsync(live, Ct);
        await Should.ThrowAsync<WorkerOperationException>(() => _session.QueryLiveAsync(live, Ct));

        async Task<LiveTraceDto> FollowAsync(TraceHandle handle, Func<LiveTraceDto, bool> until, bool work = true)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
            while (true)
            {
                LiveTraceDto? state = null;
                try
                {
                    state = await _session.QueryLiveAsync(handle, Ct);
                    if (until(state))
                    {
                        return state;
                    }
                }
                catch (WorkerCrashedException)
                {
                    // The killed worker's connection was still being used: the next question starts another.
                }

                DateTime.UtcNow.ShouldBeLessThan(deadline, $"the live trace stayed at {state}");
                if (work)
                {
                    await _shared.Api.PostWorkAsync(5, Ct);
                }

                await Task.Delay(100, Ct);
            }
        }
    }

    [Fact]
    public async Task What_is_not_live_is_refused_as_such_and_a_live_trace_that_cannot_open_says_why()
    {
        var finished = await _session.StartOpen(_shared.TracePath).Result;
        var refused = await Should.ThrowAsync<WorkerOperationException>(() => _host.InvokeAsync((service, ct) => service.QueryLiveTraceAsync(new LiveTraceRequest(finished.Descriptor.TraceId), ct), Ct));
        refused.ErrorCode.ShouldBe(HeapScopeErrorCode.InvalidArgument);
        var unknown = await Should.ThrowAsync<WorkerOperationException>(() => _host.InvokeAsync((service, ct) => service.QueryLiveTraceAsync(new LiveTraceRequest(Guid.NewGuid()), ct), Ct));
        unknown.ErrorCode.ShouldBe(HeapScopeErrorCode.TraceNotLoaded);
        var gone = await Should.ThrowAsync<WorkerOperationException>(() => _session.OpenLiveAsync(_scratch.File("gone.nettrace"), null, Ct));
        gone.ErrorCode.ShouldBe(HeapScopeErrorCode.SnapshotFormat);

        // Another recording's file under the same name: its rows would be another process's.
        var path = _scratch.File("replaced.nettrace");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(_shared.TracePath, path);
        var live = await _session.OpenLiveAsync(path, null, Ct);
        await _session.EndLiveAsync(live, Ct);
        var state = await _session.QueryLiveAsync(live, Ct);
        for (var i = 0; i < 100 && !state.Completed; i++)
        {
            await Task.Delay(100, Ct);
            state = await _session.QueryLiveAsync(live, Ct);
        }

        state.StartUtc.ShouldNotBeNull();
        var tree = await Should.ThrowAsync<WorkerOperationException>(() => _session.QueryTreeAsync(live, AllocationScope.All, AllocationTreeKindDto.BackTraces, [], null, null, Ct));
        tree.ErrorCode.ShouldBe(HeapScopeErrorCode.InvalidArgument);
        var types = await _session.QueryTypesAsync(live, new AllocationScope(0.5, 1.5), Ct);
        types.Result.Interval.ShouldBe(new AllocationIntervalDto(0, 2, state.Version, state.DurationSeconds, 1), "whole stretches");
        (await _session.QueryTimelineAsync(live, 1.5, Ct)).BucketSeconds.ShouldBe(2);
        (await _session.QueryTypesAsync(finished, new AllocationScope(0.5, 1.5), Ct)).Result.Interval.ShouldBeNull("a finished trace counts what is asked");
        var id = live.Descriptor.TraceId;
        await _host.InvokeAsync((service, ct) => service.CloseTraceAsync(id, ct), Ct);
        File.Delete(path);
        await TraceRecorder.RecordAsync(_shared.Api.ProcessId, path, TraceProfile.Allocations, TimeSpan.FromSeconds(1), Ct);
        var changed = await Should.ThrowAsync<WorkerOperationException>(async () =>
        {
            for (var i = 0; i < 100; i++)
            {
                await _session.QueryLiveAsync(live, Ct);
                await Task.Delay(100, Ct);
            }
        });
        changed.ErrorCode.ShouldBe(HeapScopeErrorCode.TraceChanged);
    }

    [Fact]
    public async Task Record_allocations_writes_into_the_session_folder_and_opens_the_trace()
    {
        using var stop = new CancellationTokenSource();
        var job = _session.StartRecord(_shared.Api.ProcessId, "LeakyApi", TimeSpan.FromSeconds(30), stop.Token);
        await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        await _shared.Api.PostWorkAsync(50, Ct);
        await stop.CancelAsync();

        var handle = await job.Result;

        Path.GetDirectoryName(handle.Path).ShouldBe(_library.Current.Folder);
        handle.Path.ShouldEndWith("-allocations.nettrace");
        handle.Descriptor.Samples.ShouldBeGreaterThan(0);
        _session.Traces.ShouldContain(handle);
    }

    [Fact]
    public async Task Cli_allocations_prints_the_top_types_and_stacks_in_every_format()
    {
        var (tableCode, table, _) = await RunCliAsync("allocations", _shared.TracePath, "--stacks", "5");
        tableCode.ShouldBe(0, table);
        table.ShouldContain("System.Byte[]");
        table.ShouldContain("Leak.Grow(");
        table.ShouldContain("Note: Estimated from samples");

        var (jsonCode, output, json) = await RunCliAsync("allocations", _shared.TracePath, "--format", "json", "--stacks", "3", "--types", "2");
        jsonCode.ShouldBe(0, output);

        // The notes go to stderr; the JSON is read from stdout alone.
        using (var document = System.Text.Json.JsonDocument.Parse(json[json.IndexOf('{', StringComparison.Ordinal)..(json.LastIndexOf('}') + 1)]))
        {
            var root = document.RootElement;
            root.GetProperty("BytesAre").GetString().ShouldBe("Estimate");
            root.GetProperty("Types").GetArrayLength().ShouldBe(2);
            root.GetProperty("Stacks").GetArrayLength().ShouldBe(3);
        }

        var (csvCode, csv, _) = await RunCliAsync("allocations", _shared.TracePath, "--format", "csv", "--view", "stacks", "--stacks", "2");
        csvCode.ShouldBe(0, csv);
        csv.ShouldContain("Rank,Bytes,Samples,Percent,Frames");

        var (typesCode, typesOnly, _) = await RunCliAsync("allocations", _shared.TracePath, "--view", "types");
        typesCode.ShouldBe(0, typesOnly);
        typesOnly.ShouldContain("All types from");

        var (usageCode, usage, _) = await RunCliAsync("allocations", _shared.TracePath, "--format", "csv");
        usageCode.ShouldBe(64, usage);

        var notTrace = _scratch.File("notes.txt");
        await File.WriteAllTextAsync(notTrace, "These are notes, not a trace; long enough to read a header from.", Ct);
        var (badCode, bad, _) = await RunCliAsync("allocations", notTrace);
        badCode.ShouldBe(1, bad);
        bad.ShouldContain("is not an EventPipe trace");
    }

    private static async Task<(int Code, string Output, string StandardOutput)> RunCliAsync(params string[] arguments)
    {
        using var cli = ChildProcess.Start(TestPaths.CliExe, arguments);
        (await cli.WaitForExitAsync(TimeSpan.FromSeconds(180))).ShouldBeTrue(cli.Output);
        return (cli.Process.ExitCode, cli.Output, cli.StandardOutput);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        _scratch.Dispose();
    }
}
