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
            File.Exists(Path.Combine(RepoRoot.Path, "fixtures", "KnownAnswers", scenario.KnownAnswer + ".json"))
                .ShouldBeTrue($"{scenario.Name}'s known answer {scenario.KnownAnswer}");
        }
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
        SampleArguments.Parse(["--scenarios", "Timer", "--count", "-1"]).Error.ShouldBe("--count takes a whole number from 1 to 1000000, not -1.");
        SampleArguments.Parse(["--list"]).Arguments.ShouldNotBeNull().List.ShouldBeTrue();
    }
}
