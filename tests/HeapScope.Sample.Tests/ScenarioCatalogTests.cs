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

using HeapScope.Tests.Shared;

namespace HeapScope.Sample.Tests;

/// <summary>
/// The sample's scenario catalog (sprint 3.13, ADR-042): the names the app and the lessons use are unique, every scenario has
/// the known answer that describes it, and what cannot run together is refused both ways with the reason.
/// </summary>
public sealed class ScenarioCatalogTests
{
    [Fact]
    public void Every_scenario_has_a_unique_name_a_title_and_a_known_answer()
    {
        var names = ScenarioCatalog.All.Select(s => s.Name).ToList();

        names.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(names.Count);
        foreach (var scenario in ScenarioCatalog.All)
        {
            scenario.Title.ShouldNotBeNullOrWhiteSpace(scenario.Name);
            scenario.Plants.ShouldNotBeNullOrWhiteSpace(scenario.Name);
            if (scenario.KnownAnswer is { } answer)
            {
                // The type a lesson's answer names is one the known answer names (a type it counts, or a collection's or
                // a method's type).
                var file = Path.Combine(RepoRoot.Path, "fixtures", "KnownAnswers", answer + ".json");
                File.Exists(file).ShouldBeTrue($"{scenario.Name}'s known answer {answer}");
                File.ReadAllText(file).ShouldContain(scenario.AnswerType, customMessage: scenario.Name);
            }
            else
            {
                // The sample's own scenarios name a type of the sample.
                typeof(ScenarioCatalog).Assembly.GetType(scenario.AnswerType).ShouldNotBeNull(scenario.Name);
            }

            // Review M1: the decoys are the sample's types too, so the answer cannot be told by its name.
            scenario.Decoys.ShouldNotBeEmpty(scenario.Name);
            scenario.Decoys.ShouldNotContain(scenario.AnswerType, scenario.Name);
            scenario.Decoys.ShouldAllBe(d => SampleType(d) != null, $"{scenario.Name}'s decoys are the sample's types");
            (scenario.CannotRunWith.Count == 0 || scenario.ConflictReason != null).ShouldBeTrue($"{scenario.Name} says why it conflicts");
        }
    }

    private static Type? SampleType(string name) =>
        typeof(ScenarioCatalog).Assembly.GetType(name) ?? typeof(Fixtures.Leaks.ILeak).Assembly.GetType(name);

    // Sprint 3.13 task 3: a scenario that keeps growing adds a step of its leak at every tick, and the steady allocation
    // scenario allocates from its one call site.
    [Fact]
    public void A_growing_scenario_adds_its_step_and_the_steady_allocation_prices_orders()
    {
        var cache = new Fixtures.Leaks.StaticCacheLeak();
        var before = cache.ItemCount;
        ScenarioCatalog.Find("StaticCache")!.Grow!.Invoke(7, new ScenarioOptions(70, 8));
        cache.ItemCount.ShouldBe(before + 7);

        var order = SteadyAllocation.PriceOrder(new Random(1));
        order.Lines.ShouldBe(8);
        order.TotalCents.ShouldBeGreaterThan(0);
        order.GetType().FullName.ShouldBe(SteadyAllocation.AnswerType);
        (typeof(SteadyAllocation).FullName + "." + nameof(SteadyAllocation.PriceOrder)).ShouldBe(SteadyAllocation.CallSite);
    }

    [Fact]
    public void The_bursts_allocate_their_answer_from_one_call_site_and_the_decoy_from_another()
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        AllocationBursts.BuildReport(100);
        var rows = GC.GetAllocatedBytesForCurrentThread() - before;
        AllocationBursts.BeatOnce(100);

        rows.ShouldBeGreaterThanOrEqualTo(100 * 56);
        (typeof(ReportRow).FullName, typeof(Heartbeat).FullName).ShouldBe((AllocationBursts.AnswerType, AllocationBursts.SteadyType));
    }

    /// <summary>Sprint 4.4: rates are allocated and not kept, trades are kept.</summary>
    [Fact]
    public void The_churn_allocates_its_answer_and_keeps_the_decoy()
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        Churn.Quote(5_000);

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBeGreaterThanOrEqualTo(5_000 * 56);
        (Churn.Book(5), Churn.Book(5)).ShouldBe((5, 10));
        (typeof(ExchangeRate).FullName, typeof(Trade).FullName).ShouldBe((Churn.AnswerType, Churn.GrowingType));
    }

    [Fact]
    public void What_cannot_run_together_is_named_both_ways()
    {
        foreach (var scenario in ScenarioCatalog.All)
        {
            foreach (var other in scenario.CannotRunWith)
            {
                ScenarioCatalog.Find(other).ShouldNotBeNull($"{scenario.Name} names {other}")
                    .CannotRunWith.ShouldContain(scenario.Name, $"{other} names {scenario.Name} back");
            }
        }
    }

    [Fact]
    public void Scenarios_are_chosen_by_name_and_conflicts_are_refused_with_the_reason()
    {
        var (arguments, error) = SampleArguments.Parse(["--scenarios", "staticcache,EventHandler,StaticCache", "--count", "50", "--grow-every", "500", "--parent-pid", "42"]);

        error.ShouldBeNull();
        arguments!.Scenarios.Select(s => s.Name).ShouldBe(["StaticCache", "EventHandler"]);
        (arguments.Options.Count, arguments.GrowEvery, arguments.ParentPid).ShouldBe((50, TimeSpan.FromMilliseconds(500), (int?)42));

        SampleArguments.Parse(["--scenarios", "Waste,FinalizerBacklog"]).Error
            .ShouldBe("Waste and FinalizerBacklog cannot run together: both block the finalizer thread. Choose one.");
        SampleArguments.Parse(["--scenarios", "Nope"]).Error.ShouldNotBeNull().ShouldStartWith("There is no scenario Nope. The scenarios are: StaticCache, ");
        SampleArguments.Parse([]).Error.ShouldNotBeNull().ShouldStartWith("Choose one or more scenarios with --scenarios");
        SampleArguments.Parse(["--scenarios", "Timer", "--count", "-1"]).Error.ShouldBe("--count takes a whole number from 1 to 100000, not -1.");
        SampleArguments.Parse(["--list"]).Arguments.ShouldNotBeNull().List.ShouldBeTrue();
    }

    // Review M5 and M13: what a scenario plants is bounded, growth is not too fast, and the default is twice the detectors' 1,000.
    [Fact]
    public void What_a_scenario_plants_is_bounded()
    {
        SampleArguments.Parse(["--scenarios", "Timer"]).Arguments.ShouldNotBeNull().Options.Count.ShouldBe(2_000);
        SampleArguments.Parse(["--scenarios", "Timer", "--count", "100000", "--payload", "65536"]).Error
            .ShouldBe("--count times --payload is at most 256 MB: choose fewer or smaller items.");
        SampleArguments.Parse(["--scenarios", "Timer", "--grow-every", "10"]).Error.ShouldBe("--grow-every is at least 100 ms.");
        SampleArguments.Parse(["--scenarios", "Timer", "--count", "4000", "--payload", "65536"]).Error.ShouldBeNull();
    }
}
