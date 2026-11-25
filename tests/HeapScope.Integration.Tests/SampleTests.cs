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
using HeapScope.Capture;
using HeapScope.Core.Allocations;
using HeapScope.Integration.Tests.Infrastructure;
using HeapScope.Loaders.Trace;

namespace HeapScope.Integration.Tests;

/// <summary>
/// The tutorial's sample as a real process (sprint 3.13, task 3): a scenario that keeps growing shows growth between two dumps,
/// and the steady allocation scenario's call site is what an allocation trace names first.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SampleTests : IDisposable
{
    private readonly TempFolder _root = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_growing_sample_grows_by_its_step_between_two_dumps()
    {
        const int Count = 200;
        using var sample = await FixtureProcess.StartSampleAsync("--scenarios", "StaticCache", "--count", Count.ToString(CultureInfo.InvariantCulture), "--grow-every", "200");
        var first = _root.File("first.dmp");
        var second = _root.File("second.dmp");

        sample.CaptureDump(first);
        await Task.Delay(TimeSpan.FromSeconds(1.5), Ct);
        sample.CaptureDump(second);

        long Entries(string path)
        {
            using var dump = DumpAnalysis.Open(path);
            return dump.CountTypes(["Fixtures.Leaks.StaticCacheEntry"])["Fixtures.Leaks.StaticCacheEntry"];
        }

        var (before, after) = (Entries(first), Entries(second));
        before.ShouldBeGreaterThanOrEqualTo(Count);
        after.ShouldBeGreaterThan(before, "it kept growing");
        ((after - before) % (Count / 10)).ShouldBe(0, "by whole steps of a tenth of its count");
    }

    [Fact]
    public async Task An_allocation_trace_of_the_steady_scenario_names_its_call_site()
    {
        using var sample = await FixtureProcess.StartSampleAsync("--scenarios", "SteadyAllocation");
        var trace = _root.File("steady.nettrace");

        using (var record = ChildProcess.Start(TestPaths.CliExe, "trace", "--pid", sample.ProcessId.ToString(CultureInfo.InvariantCulture), "--duration", "3", "--profile", "allocations", "--out", trace))
        {
            (await record.WaitForExitAsync(TimeSpan.FromSeconds(120))).ShouldBeTrue(record.Output);
            record.Process.ExitCode.ShouldBe(0, record.Output);
        }

        using var read = ChildProcess.Start(TestPaths.CliExe, "allocations", trace, "--stacks", "3", "--types", "3");
        (await read.WaitForExitAsync(TimeSpan.FromSeconds(120))).ShouldBeTrue(read.Output);
        read.Process.ExitCode.ShouldBe(0, read.Output);
        read.Output.ShouldContain("PriceOrder(", customMessage: "the planted call site is among the top stacks");

        // Review H1: the lesson's answer is the sample's type nearest the top of the types, as the Allocations page orders them.
        using var types = ChildProcess.Start(TestPaths.CliExe, "allocations", trace, "--view", "types", "--types", "20");
        (await types.WaitForExitAsync(TimeSpan.FromSeconds(120))).ShouldBeTrue(types.Output);
        types.StandardOutput.Split('\n').First(l => l.Contains("HeapScope.Sample.", StringComparison.Ordinal)).ShouldContain("HeapScope.Sample.PricedOrder");
    }

    /// <summary>
    /// Sprint 4.3: the lesson's steps on the real sample. Followed live, its heartbeats are allocated most over the whole run, and its
    /// report's rows within a burst, from the burst's call site.
    /// </summary>
    [Fact]
    public async Task The_bursts_scenario_shows_its_answer_only_in_a_burst_s_interval()
    {
        using var sample = await FixtureProcess.StartSampleAsync("--scenarios", "AllocationBursts");
        var path = _root.File("bursts.nettrace");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var started = new TaskCompletionSource<LiveTraceStart>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recording = TraceRecorder.RecordLiveAsync(sample.ProcessId, path, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(1), noNames: null, started.SetResult, stop.Token);
        var start = await started.Task.WaitAsync(TimeSpan.FromSeconds(60), Ct);
        using var live = LiveTraceReader.Start(path, start.NamesPath);

        // A burst every ten seconds: two whole ones, wherever the recording starts.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        while (live.Status().DurationMs < 24_000)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, $"the live trace stayed at {live.Status()}");
            await Task.Delay(200, Ct);
        }

        var trace = live.View().Trace;
        string Top(AllocationFilter filter) => AllocationProfile.Types(trace, filter, Ct).Select(t => trace.Types[t.Type]).First(t => t.StartsWith("HeapScope.Sample.", StringComparison.Ordinal));
        var tallest = AllocationProfile.Timeline(trace, 1000, Ct).MaxBy(b => b.Bytes);
        var burst = new AllocationFilter(tallest.StartMs, tallest.StartMs + 1000);
        Top(AllocationFilter.All).ShouldBe("HeapScope.Sample.Heartbeat");
        Top(burst).ShouldBe("HeapScope.Sample.ReportRow");
        var stack = AllocationProfile.TopStacks(trace, burst, 1, Ct).Single();
        AllocationProfile.FramesOf(trace, stack.Node).Select(f => trace.Frames[f]).ShouldContain(f => f.Contains("AllocationBursts.BuildReport", StringComparison.Ordinal));

        await stop.CancelAsync();
        await recording;
    }

    // Sprint 3.13 task 5: the app's service reads the real sample's catalog, starts it, knows it as a sample and stops it.
    [Fact]
    public async Task The_service_lists_starts_and_stops_the_real_sample()
    {
        var launch = new HeapScope.Application.Tutorial.SampleLaunch(TestPaths.SampleExe, [], TestPaths.SampleExe);
        using var service = new HeapScope.Application.Tutorial.SampleService(new HeapScope.Application.Tutorial.SampleProcessStarter(), () => false, () => launch);

        var scenarios = await service.ListScenariosAsync(Ct);
        scenarios.Select(s => s.Name).ShouldContain("SteadyAllocation");
        scenarios.Single(s => s.Name == "StaticCache").AnswerType.ShouldBe("Fixtures.Leaks.StaticCacheEntry");

        var sample = await service.StartAsync(["StaticCache", "Timer"], grow: true, Ct);
        using var process = System.Diagnostics.Process.GetProcessById(sample.ProcessId);
        service.IsSample(sample.ProcessId).ShouldBeTrue();

        service.Stop(sample.ProcessId, "a test").ShouldBeTrue();
        await process.WaitForExitAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
        process.HasExited.ShouldBeTrue("the stopped sample exits");
        service.Running.ShouldBeEmpty();
    }

    // Sprint 3.13 task 9: the demo without the app. The CLI starts the sample in the foreground, lists it, captures it and stops
    // it from a second command, and the first one then ends on its own.
    [Fact]
    public async Task The_cli_starts_the_sample_captures_it_and_stops_it()
    {
        if (HeapScope.Capture.WindowsElevationProbe.Instance.IsCurrentProcessElevated)
        {
            // CI's Windows runner is elevated: the CLI must refuse, since the sample would run elevated too.
            using var refused = ChildProcess.Start(TestPaths.CliExe, "sample", "start", "--scenarios", "StaticCache");
            (await refused.WaitForExitAsync(TimeSpan.FromSeconds(60))).ShouldBeTrue(refused.Output);
            refused.Process.ExitCode.ShouldBe(1, refused.Output);
            refused.Output.ShouldContain("HeapScope runs as administrator, so the sample would too.");
            Assert.Skip("The test run is elevated: the CLI refused the sample, as it must. Starting, capturing and stopping it is checked in a run without administrator rights.");
        }

        using var start = ChildProcess.Start(TestPaths.CliExe, "sample", "start", "--scenarios", "StaticCache");
        (await start.WaitForOutputAsync("Stop it:", TimeSpan.FromSeconds(60))).ShouldBeTrue(start.Output);
        var match = System.Text.RegularExpressions.Regex.Match(start.StandardOutput, @"^Sample (\d+) runs: StaticCache\.", System.Text.RegularExpressions.RegexOptions.Multiline);
        match.Success.ShouldBeTrue(start.Output);
        var pid = match.Groups[1].Value;

        using (var list = ChildProcess.Start(TestPaths.CliExe, "list"))
        {
            (await list.WaitForExitAsync(TimeSpan.FromSeconds(60))).ShouldBeTrue(list.Output);
            list.StandardOutput.ShouldContain(pid, customMessage: "the sample is HeapScope's but is listed");
        }

        var dump = _root.File("sample.dmp");
        using (var capture = ChildProcess.Start(TestPaths.CliExe, "capture", "--pid", pid, "--out", dump))
        {
            (await capture.WaitForExitAsync(TimeSpan.FromSeconds(120))).ShouldBeTrue(capture.Output);
            capture.Process.ExitCode.ShouldBe(0, capture.Output);
        }

        using (var analysis = DumpAnalysis.Open(dump))
        {
            analysis.CountTypes(["Fixtures.Leaks.StaticCacheEntry"])["Fixtures.Leaks.StaticCacheEntry"].ShouldBeGreaterThan(0);
        }

        using (var stop = ChildProcess.Start(TestPaths.CliExe, "sample", "stop", "--pid", pid))
        {
            (await stop.WaitForExitAsync(TimeSpan.FromSeconds(60))).ShouldBeTrue(stop.Output);
            stop.Process.ExitCode.ShouldBe(0, stop.Output);
        }

        (await start.WaitForExitAsync(TimeSpan.FromSeconds(30))).ShouldBeTrue("the start command ends when its sample stops");
        start.Process.ExitCode.ShouldBe(0, start.Output);
        start.StandardOutput.ShouldContain($"Sample {pid} stopped.");
    }

    public void Dispose() => _root.Dispose();
}
