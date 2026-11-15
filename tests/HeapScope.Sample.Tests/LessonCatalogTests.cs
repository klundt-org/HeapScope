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

using HeapScope.UI.ViewModels;

namespace HeapScope.Sample.Tests;

/// <summary>
/// The Learn page's lessons against the sample's scenarios (sprint 3.13, ADR-042): every lesson's scenarios exist, every
/// scenario has a lesson or a reason, every lesson's answer is a scenario it starts, and its choices hold the answer once.
/// </summary>
public sealed class LessonCatalogTests
{
    [Fact]
    public void Every_lesson_starts_scenarios_the_sample_has_and_every_scenario_has_a_lesson_or_a_reason()
    {
        foreach (var lesson in LessonCatalog.All)
        {
            lesson.Scenarios.ShouldNotBeEmpty(lesson.Id);
            lesson.Scenarios.ShouldAllBe(s => ScenarioCatalog.Find(s) != null, lesson.Id);
            lesson.Scenarios.ShouldContain(lesson.AnswerScenario, lesson.Id);
            if (lesson.Grows)
            {
                lesson.Scenarios.ShouldAllBe(s => ScenarioCatalog.Find(s)!.Grow != null, $"{lesson.Id} grows only what can grow");
            }
        }

        var taught = LessonCatalog.All.SelectMany(l => l.Scenarios).ToHashSet(StringComparer.Ordinal);
        foreach (var scenario in ScenarioCatalog.All)
        {
            (taught.Contains(scenario.Name) || LessonCatalog.NoLesson.ContainsKey(scenario.Name)).ShouldBeTrue($"{scenario.Name} has no lesson and no reason");
        }

        LessonCatalog.NoLesson.Keys.ShouldAllBe(name => ScenarioCatalog.Find(name) != null);
    }

    [Fact]
    public void Every_lesson_is_whole_and_its_choices_hold_the_planted_type_once_among_the_decoys()
    {
        LessonCatalog.All.Select(l => l.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(LessonCatalog.All.Count);
        foreach (var lesson in LessonCatalog.All)
        {
            lesson.Title.ShouldNotBeNullOrWhiteSpace(lesson.Id);
            lesson.Summary.ShouldNotBeNullOrWhiteSpace(lesson.Id);
            lesson.Question.ShouldEndWith("?", customMessage: lesson.Id);
            lesson.Steps.ShouldNotBeEmpty(lesson.Id);
            var answer = ScenarioCatalog.Find(lesson.AnswerScenario)!.AnswerType;
            var choices = lesson.Choices(answer);
            choices.Count(c => c == answer).ShouldBe(1, lesson.Id);
            choices.ShouldBe(lesson.Choices(answer), "the same order every time");
            Lesson.Decoys.ShouldNotContain(answer, lesson.Id);
            Lesson.IsRight(answer, answer).ShouldBeTrue();
            Lesson.IsRight(Lesson.Decoys[0], answer).ShouldBeFalse();
        }
    }
}
