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

using System.ComponentModel;
using HeapScope.Application.Tutorial;
using HeapScope.Core;
using HeapScope.Tests.Shared;

namespace HeapScope.Application.Tests;

/// <summary>
/// The tutorial's sample as the app starts it (sprint 3.13, task 5 and its review): its catalog read from the sample, the
/// scenarios checked against it, never elevated, never more than three, each watching this process, stopped on request, and
/// every failure a refusal with a next step; exits reported with why, and records that let another process stop a sample.
/// </summary>
public sealed class SampleServiceTests : IDisposable
{
    private static readonly SampleLaunch s_launch = new("sample/heapscope-sample.exe", [], "sample/heapscope-sample.exe");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));
    private readonly FakeSampleStarter _starter = new();
    private readonly List<SampleService> _services = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SampleService Service(bool elevated = false, bool installed = true, TimeSpan? readyTimeout = null)
    {
        var service = new SampleService(_starter, () => elevated, () => installed ? s_launch : null, folders: new HeapScopeFolders(_root), readyTimeout: readyTimeout);
        _services.Add(service);
        return service;
    }

    [Fact]
    public async Task The_catalog_is_read_from_the_sample_once_with_its_version()
    {
        var service = Service();

        var scenarios = await service.ListScenariosAsync(Ct);
        await service.ListScenariosAsync(Ct);

        scenarios.Select(s => s.Name).ShouldBe(["StaticCache", "Timer", "Waste", "FinalizerBacklog"]);
        scenarios[0].Decoys.ShouldBe(["Fixtures.Leaks.EntryMetadata"]);
        service.CatalogVersion.ShouldBe(1);
        _starter.Started.Count(s => s.Arguments.Contains("--list")).ShouldBe(1);
    }

    [Fact]
    public async Task A_catalog_that_cannot_be_read_is_refused_and_read_again_next_time()
    {
        var service = Service();
        _starter.Catalog = null;

        (await Should.ThrowAsync<SampleRefusedException>(() => service.ListScenariosAsync(Ct))).Message.ShouldStartWith("The sample could not list its scenarios.");
        _starter.Catalog = "not json";
        (await Should.ThrowAsync<SampleRefusedException>(() => service.ListScenariosAsync(Ct))).Message.ShouldStartWith("The sample could not list its scenarios.");
        _starter.Catalog = FakeSampleStarter.DefaultCatalog;

        (await service.ListScenariosAsync(Ct)).Count.ShouldBe(4, "a failed read is not kept");
    }

    [Fact]
    public async Task A_sample_starts_with_its_scenarios_watching_this_process_and_stops_on_request()
    {
        var service = Service();
        var changes = 0;
        service.Changed += (_, _) => Interlocked.Increment(ref changes);

        var sample = await service.StartAsync(["staticcache"], grow: true, Ct);

        var started = _starter.Started.Last();
        started.Arguments.ShouldBe(["--scenarios", "StaticCache", "--parent-pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), "--grow-every", "1000"]);
        sample.Scenarios.ShouldBe(["StaticCache"]);
        service.IsSample(sample.ProcessId).ShouldBeTrue();
        var exit = service.ExitAsync(sample.ProcessId).ShouldNotBeNull();

        service.Stop(sample.ProcessId, "a test").ShouldBeTrue();
        started.Process.Killed.ShouldBeTrue();
        service.Running.ShouldBeEmpty();
        (await exit.WaitAsync(Ct)).Reason.ShouldBe(SampleExitReason.Stopped);
        service.Stop(sample.ProcessId, "a test").ShouldBeFalse("not a running sample any more");
        changes.ShouldBe(2);
    }

    [Fact]
    public async Task A_start_is_refused_with_what_to_do()
    {
        (await Refusal(Service(elevated: true), ["StaticCache"]))
            .ShouldBe("HeapScope runs as administrator, so the sample would too. Start HeapScope without administrator rights to run it.");
        (await Refusal(Service(installed: false), ["StaticCache"]))
            .ShouldBe($"The sample is not in this installation. {SampleService.RepairStep}.");
        (await Refusal(Service(), ["Nope"])).ShouldBe("There is no scenario Nope in this HeapScope's sample. The Processes page and 'heapscope-cli sample list' list them.");
        (await Refusal(Service(), ["Waste", "FinalizerBacklog"]))
            .ShouldBe("Waste and FinalizerBacklog cannot run together: both block the finalizer thread. Choose one.");
        (await Refusal(Service(), [])).ShouldBe("Choose at least one scenario.");
        _starter.Started.ShouldAllBe(s => s.Arguments.Contains("--list"), "no sample process was started for a refusal");
    }

    [Fact]
    public async Task No_more_than_three_samples_run_or_start_at_once_and_an_exited_one_frees_its_place()
    {
        var service = Service();
        await service.ListScenariosAsync(Ct);
        _starter.NextHangs = true;
        var hanging = service.StartAsync(["StaticCache"], grow: false, Ct);
        await service.StartAsync(["StaticCache"], grow: false, Ct);
        await service.StartAsync(["StaticCache"], grow: false, Ct);

        (await Refusal(service, ["StaticCache"])).ShouldStartWith("Three samples are running, the most at once.", customMessage: "the one still starting counts");

        _starter.Started.First(s => !s.Arguments.Contains("--list")).Process.Kill();
        await Should.ThrowAsync<SampleRefusedException>(() => hanging);
        _starter.Last.Exit(code: 1);
        (await service.StartAsync(["StaticCache"], grow: false, Ct)).ShouldNotBeNull("neither the failed start nor the exited one counts");
    }

    [Fact]
    public async Task A_sample_that_exits_before_it_is_ready_is_refused_with_its_own_words()
    {
        var service = Service();
        _starter.NextExitsWith = "--count takes a whole number from 1 to 100000, not x.";

        (await Refusal(service, ["StaticCache"])).ShouldBe("The sample did not start: --count takes a whole number from 1 to 100000, not x.");
        service.Running.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_sample_that_cannot_be_started_is_a_refusal_with_a_next_step()
    {
        var service = Service();
        await service.ListScenariosAsync(Ct);
        _starter.NextThrows = new Win32Exception(5, "Access is denied");

        var message = await Refusal(service, ["StaticCache"]);

        message.ShouldStartWith("The sample at sample/heapscope-sample.exe could not be started (Access is denied).");
        message.ShouldContain("If an antivirus or a company policy blocks it, allow it");
    }

    [Fact]
    public async Task A_sample_that_is_not_ready_in_time_is_stopped_and_refused()
    {
        var service = Service(readyTimeout: TimeSpan.FromMilliseconds(200));
        await service.ListScenariosAsync(Ct);
        _starter.NextHangs = true;

        (await Refusal(service, ["StaticCache"])).ShouldBe("The sample did not get ready within 0 seconds and was stopped. The computer may be busy; try again.");

        _starter.Last.Killed.ShouldBeTrue();
        service.Running.ShouldBeEmpty();
    }

    // A real pipe may not honor cancellation: the timeout kills the sample, the read ends, and it is still the timeout.
    [Fact]
    public async Task A_timeout_is_reported_as_one_when_the_read_ends_only_with_the_kill()
    {
        var service = Service(readyTimeout: TimeSpan.FromMilliseconds(200));
        await service.ListScenariosAsync(Ct);
        _starter.NextHangs = true;
        _starter.ReadsIgnoreCancellation = true;

        (await Refusal(service, ["StaticCache"])).ShouldBe("The sample did not get ready within 0 seconds and was stopped. The computer may be busy; try again.");
        _starter.Last.Killed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_canceled_start_stops_the_sample()
    {
        var service = Service();
        await service.ListScenariosAsync(Ct);
        _starter.NextHangs = true;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var start = service.StartAsync(["StaticCache"], grow: false, cancel.Token);
        await cancel.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => start);
        _starter.Last.Killed.ShouldBeTrue();
        service.Running.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_exit_is_reported_with_why_and_frees_the_sample()
    {
        var service = Service();
        var changes = 0;
        service.Changed += (_, _) => Interlocked.Increment(ref changes);
        var crashed = await service.StartAsync(["StaticCache"], grow: false, Ct);
        var stoppedElsewhere = await service.StartAsync(["Timer"], grow: false, Ct);
        var crash = service.ExitAsync(crashed.ProcessId).ShouldNotBeNull();
        var elsewhere = service.ExitAsync(stoppedElsewhere.ProcessId).ShouldNotBeNull();

        _starter.Started.Single(s => s.Process.Id == crashed.ProcessId).Process.Exit(code: 3, error: "Unhandled exception.");
        Service().StopRecorded(stoppedElsewhere.ProcessId).ShouldBe(SampleStopOutcome.Stopped);

        (await crash.WaitAsync(Ct)).ShouldBe(new SampleExit(crashed.ProcessId, SampleExitReason.OnItsOwn, 3, "Unhandled exception."));
        (await elsewhere.WaitAsync(Ct)).Reason.ShouldBe(SampleExitReason.StoppedElsewhere);
        service.Running.ShouldBeEmpty();
        changes.ShouldBe(4, "two starts and two exits");
    }

    [Fact]
    public async Task Another_process_stops_only_a_recorded_sample_and_never_a_reused_pid()
    {
        var service = Service();
        var other = Service();
        var sample = await service.StartAsync(["StaticCache"], grow: false, Ct);

        other.StopRecorded(Environment.ProcessId).ShouldBe(SampleStopOutcome.NotASample, "no record");
        _starter.Others[777] = DateTime.UtcNow;
        await File.WriteAllTextAsync(Path.Combine(_root, "samples", "777.json"), $$"""{"ProcessId":777,"StartTimeUtc":"2020-01-01T00:00:00Z","Scenarios":["Timer"],"OwnerProcessId":1}""", Ct);
        other.StopRecorded(777).ShouldBe(SampleStopOutcome.NotASample, "the pid now names another process");
        _starter.Others.ContainsKey(777).ShouldBeTrue("never stopped");
        File.Exists(Path.Combine(_root, "samples", "777.json")).ShouldBeFalse("the stale record is removed");

        other.StopRecorded(sample.ProcessId).ShouldBe(SampleStopOutcome.Stopped);
        _starter.Last.Killed.ShouldBeTrue();
        other.StopRecorded(sample.ProcessId).ShouldBe(SampleStopOutcome.NotASample, "its record went with it");
    }

    [Fact]
    public async Task Disposing_stops_every_sample_and_removes_their_records()
    {
        var service = Service();
        await service.StartAsync(["StaticCache"], grow: false, Ct);
        await service.StartAsync(["Timer"], grow: false, Ct);

        service.Dispose();

        _starter.Started.Where(s => !s.Arguments.Contains("--list")).ShouldAllBe(s => s.Process.Killed);
        Directory.EnumerateFiles(Path.Combine(_root, "samples")).ShouldBeEmpty();
    }

    [Fact]
    public void The_sample_beside_the_app_is_its_executable_or_its_assembly_through_the_dotnet_host()
    {
        var exe = Path.Combine("app", "sample", OperatingSystem.IsWindows() ? "heapscope-sample.exe" : "heapscope-sample");
        var dll = Path.Combine("app", "sample", "heapscope-sample.dll");

        SampleLaunch.Resolve("app", "dotnet", f => f == exe).ShouldBe(new SampleLaunch(exe, [], exe), new LaunchComparer());
        var tool = SampleLaunch.Resolve("app", "dotnet", f => f == dll).ShouldNotBeNull();
        (tool.FileName, tool.Prefix.Single()).ShouldBe(("dotnet", dll));
        SampleLaunch.Resolve("app", null, f => f == dll).ShouldBeNull("an assembly needs a dotnet host");
        SampleLaunch.Resolve("app", "dotnet", _ => false).ShouldBeNull();
    }

    public void Dispose()
    {
        foreach (var service in _services)
        {
            service.Dispose();
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static async Task<string> Refusal(SampleService service, IReadOnlyList<string> scenarios) =>
        (await Should.ThrowAsync<SampleRefusedException>(() => service.StartAsync(scenarios, grow: false, Ct))).Message;

    private sealed class LaunchComparer : IEqualityComparer<SampleLaunch>
    {
        public bool Equals(SampleLaunch? x, SampleLaunch? y) => x is not null && y is not null && x.FileName == y.FileName && x.SampleFile == y.SampleFile && x.Prefix.SequenceEqual(y.Prefix);

        public int GetHashCode(SampleLaunch obj) => obj.FileName.GetHashCode(StringComparison.Ordinal);
    }
}
