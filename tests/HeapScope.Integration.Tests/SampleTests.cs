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

using System.Globalization;
using HeapScope.Integration.Tests.Infrastructure;

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
        read.Output.ShouldContain("HeapScope.Sample.", customMessage: "and its types among the top types");
    }

    public void Dispose() => _root.Dispose();
}
