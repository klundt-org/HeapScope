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


using HeapScope.Application.Monitoring;
using HeapScope.Application.Sessions;
using HeapScope.Capture;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeapScope.Application.Tests;

/// <summary>A monitoring run's allocation trace, apart from the run (sprint 3.8, peer review P11).</summary>
public sealed class AllocationRecordingTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("heapscope-recording-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Disposing_waits_for_the_watch_which_waits_for_running_captures_before_stopping_the_trace()
    {
        static async Task<TraceResult> Recorder(AllocationRecordingRequest request, CancellationToken ct)
        {
            await File.WriteAllBytesAsync(request.Path, new byte[4096], CancellationToken.None);
            await Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => { }, TaskScheduler.Default);
            return new TraceResult(request.Path, 4096, TimeSpan.FromSeconds(1), StoppedEarly: true);
        }

        var capture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var problems = new List<string>();
        var limitReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recording = AllocationRecording.Start(
            _folder,
            Guid.NewGuid(),
            42,
            limitBytes: 1024,
            Recorder,
            TimeProvider.System,
            NullLogger.Instance,
            () => [capture.Task],
            (message, _) =>
            {
                lock (problems)
                {
                    problems.Add(message);
                }

                limitReached.TrySetResult();
            },
            TimeSpan.FromSeconds(1));

        await limitReached.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var disposing = recording.DisposeAsync().AsTask();
        await Task.Delay(300, Ct);
        disposing.IsCompleted.ShouldBeFalse("the watch waits for the running capture before it stops the trace");

        capture.SetResult();
        await disposing.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        problems.ShouldHaveSingleItem().ShouldContain("limit");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // Peer review 4.5 H11: the disk of the trace's own folder, on every system; a check that fails is tried again, five in a row stop it.
    [Theory]
    [InlineData(4, false)]
    [InlineData(AllocationRecording.MostFailedChecks, true)]
    public async Task The_free_space_of_the_trace_s_folder_is_checked_and_failed_checks_are_tried_again(int failures, bool stops)
    {
        var disk = new Disk(failures);
        var problems = new List<string>();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recording = AllocationRecording.Start(
            _folder,
            Guid.NewGuid(),
            42,
            limitBytes: 1L << 40,
            static async (request, ct) =>
            {
                await File.WriteAllBytesAsync(request.Path, new byte[16], CancellationToken.None);
                await Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => { }, TaskScheduler.Default);
                return new TraceResult(request.Path, 16, TimeSpan.FromSeconds(1), StoppedEarly: true);
            },
            new FastTime(),
            NullLogger.Instance,
            () => [],
            (message, _) =>
            {
                lock (problems)
                {
                    problems.Add(message);
                }

                stopped.TrySetResult();
            },
            TimeSpan.FromSeconds(1),
            disk: disk);

        await Task.WhenAny(stopped.Task, Task.Delay(TimeSpan.FromSeconds(3), Ct));
        await recording.DisposeAsync();

        disk.Asked.ShouldAllBe(f => f == Path.GetFullPath(_folder));
        disk.Asked.Count.ShouldBeGreaterThanOrEqualTo(stops ? failures : failures + 1);
        problems.Count.ShouldBe(stops ? 1 : 0);
        if (stops)
        {
            problems[0].ShouldContain("could not be checked");
        }
    }

    /// <summary>Free space that cannot be read the first <paramref name="failures"/> times, then plenty.</summary>
    private sealed class Disk(int failures) : IDiskSpace
    {
        public List<string> Asked { get; } = [];

        public long? AvailableBytes(string directory)
        {
            lock (Asked)
            {
                Asked.Add(directory);
                return Asked.Count <= failures ? null : 1L << 40;
            }
        }
    }

    /// <summary>Time whose delays pass a hundred times faster.</summary>
    private sealed class FastTime : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            base.CreateTimer(callback, state, dueTime == Timeout.InfiniteTimeSpan ? dueTime : dueTime / 100, period == Timeout.InfiniteTimeSpan ? period : period / 100);
    }
}
