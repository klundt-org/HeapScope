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

using HeapScope.Application.Jobs;
using HeapScope.Application.Sessions;
using HeapScope.Capture;
using HeapScope.Tests.Shared;

namespace HeapScope.Application.Tests;

/// <summary>
/// The per-process capture gate and a cancel while the dump is written, without timing (sprint 3.12, the 3.4 review's M13 and
/// the 3.11 wrap's cancel race): the dump writer is held until the test lets it write, so each step happens in a known order.
/// </summary>
public sealed class CaptureGateTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("heapscope-gate-").FullName;
    private readonly HeldDumpWriter _writer = new(new FakeDumpWriter());
    private readonly CaptureService _service;

    public CaptureGateTests()
    {
        var elevation = new NotElevated();
        _service = new CaptureService(
            new FakeDiscovery(),
            new DumpCapturer(elevation, new PlentyOfDisk(), _writer, static _ => 1024),
            new SessionLibrary(_root),
            new JobTracker(TimeProvider.System),
            elevation);
    }

    // Every wait is bounded, so a broken gate or cancel fails the test instead of hanging it.
    private static readonly TimeSpan s_limit = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Capture runs on Windows only (ADR-025): elsewhere `DumpCapturer` refuses before its writer starts, so the gate these
    // tests hold the writer for is never reached. The gate itself is the same code on every system.
    private static void WindowsOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Capture runs on Windows only (ADR-025); the capturer refuses before the gate is reached.");
        }
    }

    [Fact]
    public async Task A_second_capture_of_a_process_is_refused_while_the_first_writes_and_runs_once_it_has()
    {
        WindowsOnly();
        var first = _service.StartCapture(1);
        await _writer.Started(1).WaitAsync(s_limit, Ct);

        var refused = await Should.ThrowAsync<CaptureFailedException>(() => _service.StartCapture(1).Result.WaitAsync(s_limit, Ct));
        refused.Reason.ShouldBe(CaptureFailure.Busy);
        refused.Message.ShouldContain("A capture of process 1 is running");
        _service.IsBusy(1).ShouldBeTrue();

        _writer.Release(1);
        (await first.Result.WaitAsync(s_limit, Ct)).SizeBytes.ShouldBe(FakeDumpWriter.Bytes.Length);
        _service.IsBusy(1).ShouldBeFalse("the gate is released with the capture");

        var again = _service.StartCapture(1);
        await _writer.Started(1).WaitAsync(s_limit, Ct);
        _writer.Release(1);
        await again.Result.WaitAsync(s_limit, Ct);
    }

    [Fact]
    public async Task Two_processes_are_captured_at_once()
    {
        WindowsOnly();
        var one = _service.StartCapture(1);
        var two = _service.StartCapture(2);

        await Task.WhenAll(_writer.Started(1), _writer.Started(2)).WaitAsync(s_limit, Ct);
        (_service.IsBusy(1), _service.IsBusy(2)).ShouldBe((true, true), "both write before either is released");

        _writer.Release(1);
        _writer.Release(2);
        await Task.WhenAll(one.Result, two.Result).WaitAsync(s_limit, Ct);
    }

    [Fact]
    public async Task A_failed_capture_releases_the_gate_and_leaves_no_dump()
    {
        WindowsOnly();
        var failing = _service.StartCapture(1);
        await _writer.Started(1).WaitAsync(s_limit, Ct);
        _writer.Fail(1, new IOException("The device is not ready."));

        var failure = await Should.ThrowAsync<CaptureFailedException>(() => failing.Result.WaitAsync(s_limit, Ct));
        failure.Message.ShouldContain("The device is not ready.");
        _service.IsBusy(1).ShouldBeFalse();
        Dumps().ShouldBeEmpty();
        Directory.EnumerateDirectories(_root).ShouldBeEmpty("the session made for the failed capture is removed");
    }

    // The 3.11 wrap: a real capture finished before its cancel arrived, so the test saw no cancel. Here the cancel always
    // lands while the runtime writes, which it cannot stop: the dump is written in full, then deleted.
    [Fact]
    public async Task A_cancel_while_the_dump_is_written_waits_for_the_writer_then_deletes_the_dump_and_releases_the_gate()
    {
        WindowsOnly();
        var job = _service.StartCapture(1);
        await _writer.Started(1).WaitAsync(s_limit, Ct);
        job.Cancel();
        await Task.Delay(50, Ct);
        job.Result.IsCompleted.ShouldBeFalse("the runtime's writer cannot be stopped; the capture waits for it");

        _writer.Release(1);

        await Should.ThrowAsync<OperationCanceledException>(() => job.Result.WaitAsync(s_limit, Ct));
        _writer.Completed(1).ShouldBe(1, "the writer finished the dump before HeapScope deleted it");
        Dumps().ShouldBeEmpty();
        _service.IsBusy(1).ShouldBeFalse();
        Directory.EnumerateDirectories(_root).ShouldBeEmpty("the session made for the canceled capture is removed");
    }

    public void Dispose()
    {
        _writer.ReleaseAll();
        Directory.Delete(_root, recursive: true);
    }

    private IEnumerable<string> Dumps() => Directory.EnumerateFiles(_root, "*.dmp*", SearchOption.AllDirectories);

    private sealed class FakeDumpWriter : IDumpWriter
    {
        public static readonly byte[] Bytes = [0x4D, 0x44, 0x4D, 0x50];

        public Task WriteAsync(int processId, string path) => File.WriteAllBytesAsync(path, Bytes);
    }

    private sealed class FakeDiscovery : IProcessDiscovery
    {
        public IReadOnlyList<DotNetProcess> List(CancellationToken cancellationToken = default) => [Find(1)!, Find(2)!];

        public DotNetProcess? Find(int processId) =>
            new(processId, "App" + processId, null, "10.0.0", ElevationState.NotElevated, 1 << 20, 1 << 20, null);
    }

    private sealed class NotElevated : IElevationProbe
    {
        public bool IsCurrentProcessElevated => false;

        public ElevationState GetState(int processId) => ElevationState.NotElevated;
    }

    private sealed class PlentyOfDisk : IDiskSpace
    {
        public long? AvailableBytes(string directory) => 1L << 40;
    }
}
