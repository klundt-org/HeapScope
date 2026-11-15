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

using HeapScope.Application.Tutorial;

namespace HeapScope.Application.Tests;

/// <summary>
/// The tutorial's sample as the app starts it (sprint 3.13, task 5): its catalog read from the sample, the scenarios checked
/// against it, never elevated, never more than three, each watching this process, and stopped on request.
/// </summary>
public sealed class SampleServiceTests
{
    private const string Catalog = """[{"Name":"StaticCache","Title":"A static cache","Plants":"p","KnownAnswer":"StaticCache","AnswerType":"Fixtures.Leaks.StaticCacheEntry","CannotRunWith":[],"Grows":true},{"Name":"Waste","Title":"Waste","Plants":"p","KnownAnswer":"Waste","AnswerType":"Fixtures.Leaks.WasteHolder","CannotRunWith":["FinalizerBacklog"],"Grows":false},{"Name":"FinalizerBacklog","Title":"Backlog","Plants":"p","KnownAnswer":"FinalizerBacklog","AnswerType":"Fixtures.Leaks.FinalizableItem","CannotRunWith":["Waste"],"Grows":false}]""";

    private static readonly SampleLaunch s_launch = new("sample/heapscope-sample.exe", [], "sample/heapscope-sample.exe");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeStarter _starter = new();

    private SampleService Service(bool elevated = false, SampleLaunch? launch = null, bool installed = true) =>
        new(_starter, () => elevated, () => installed ? launch ?? s_launch : null);

    [Fact]
    public async Task The_catalog_is_read_from_the_sample_once()
    {
        var service = Service();

        var scenarios = await service.ListScenariosAsync(Ct);
        await service.ListScenariosAsync(Ct);

        scenarios.Select(s => s.Name).ShouldBe(["StaticCache", "Waste", "FinalizerBacklog"]);
        scenarios[0].AnswerType.ShouldBe("Fixtures.Leaks.StaticCacheEntry");
        _starter.Started.Count(s => s.Arguments.Contains("--list")).ShouldBe(1);
    }

    [Fact]
    public async Task A_sample_starts_with_its_scenarios_watching_this_process_and_stops_on_request()
    {
        var service = Service();
        var changes = 0;
        service.Changed += (_, _) => changes++;

        var sample = await service.StartAsync(["staticcache"], grow: true, Ct);

        var started = _starter.Started.Last();
        started.Arguments.ShouldBe(["--scenarios", "StaticCache", "--parent-pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), "--grow-every", "1000"]);
        sample.Scenarios.ShouldBe(["StaticCache"]);
        sample.Grows.ShouldBeTrue();
        service.IsSample(sample.ProcessId).ShouldBeTrue();
        service.Running.ShouldHaveSingleItem();

        service.Stop(sample.ProcessId).ShouldBeTrue();
        started.Process.Killed.ShouldBeTrue();
        service.Running.ShouldBeEmpty();
        service.Stop(sample.ProcessId).ShouldBeFalse("not a running sample any more");
        changes.ShouldBe(2);
    }

    [Fact]
    public async Task A_start_is_refused_with_what_to_do()
    {
        (await Refusal(Service(elevated: true), ["StaticCache"]))
            .ShouldBe("HeapScope runs as administrator, so the sample would too. Start HeapScope without administrator rights to run it.");
        (await Refusal(Service(installed: false), ["StaticCache"]))
            .ShouldBe("The sample is not in this installation. Reinstall HeapScope from its release archive.");
        (await Refusal(Service(), ["Nope"])).ShouldBe("There is no scenario Nope in this HeapScope's sample.");
        (await Refusal(Service(), ["Waste", "FinalizerBacklog"]))
            .ShouldBe("Waste and FinalizerBacklog cannot run together: both block the finalizer thread. Choose one.");
        (await Refusal(Service(), [])).ShouldBe("Choose at least one scenario.");
        _starter.Started.ShouldAllBe(s => s.Arguments.Contains("--list"), "no sample process was started for a refusal");
    }

    [Fact]
    public async Task No_more_than_three_samples_run_at_once_and_an_exited_one_frees_its_place()
    {
        var service = Service();
        for (var i = 0; i < SampleService.MaxRunning; i++)
        {
            await service.StartAsync(["StaticCache"], grow: false, Ct);
        }

        (await Refusal(service, ["StaticCache"])).ShouldBe("3 samples are running. Stop one first.");

        _starter.Started.Last().Process.Exit();
        (await service.StartAsync(["StaticCache"], grow: false, Ct)).ShouldNotBeNull("the exited one no longer counts");
    }

    [Fact]
    public async Task A_sample_that_exits_before_it_is_ready_is_refused_with_its_own_words()
    {
        var service = Service();
        _starter.NextExitsWith = "--count takes a whole number from 1 to 1000000, not x.";

        (await Refusal(service, ["StaticCache"])).ShouldBe("The sample did not start: --count takes a whole number from 1 to 1000000, not x.");
        service.Running.ShouldBeEmpty();
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

    private static async Task<string> Refusal(SampleService service, IReadOnlyList<string> scenarios) =>
        (await Should.ThrowAsync<SampleRefusedException>(() => service.StartAsync(scenarios, grow: false, Ct))).Message;

    private sealed class LaunchComparer : IEqualityComparer<SampleLaunch>
    {
        public bool Equals(SampleLaunch? x, SampleLaunch? y) => x is not null && y is not null && x.FileName == y.FileName && x.SampleFile == y.SampleFile && x.Prefix.SequenceEqual(y.Prefix);

        public int GetHashCode(SampleLaunch obj) => obj.FileName.GetHashCode(StringComparison.Ordinal);
    }

    private sealed class FakeStarter : ISampleProcessStarter
    {
        private int _nextId = 1000;

        public List<(IReadOnlyList<string> Arguments, FakeProcess Process)> Started { get; } = [];

        public string? NextExitsWith { get; set; }

        public ISampleProcess Start(string fileName, IReadOnlyList<string> arguments)
        {
            FakeProcess process;
            if (arguments.Contains("--list"))
            {
                process = new FakeProcess(_nextId++, [Catalog], null);
            }
            else
            {
                process = NextExitsWith is { } error ? new FakeProcess(_nextId++, [], error) : new FakeProcess(_nextId++, ["planting", $"READY pid={_nextId}"], null);
                NextExitsWith = null;
            }

            Started.Add((arguments, process));
            return process;
        }
    }

    private sealed class FakeProcess(int id, IReadOnlyList<string> lines, string? error) : ISampleProcess
    {
        private int _next;
        private bool _exited = error is not null;

        public int Id { get; } = id;

        public bool HasExited => _exited || Killed;

        public bool Killed { get; private set; }

        public string Error { get; } = error ?? string.Empty;

        public Task<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_next < lines.Count ? lines[_next++] : null);

        public void Exit() => _exited = true;

        public void Kill() => Killed = true;

        public void Dispose()
        {
        }
    }
}
