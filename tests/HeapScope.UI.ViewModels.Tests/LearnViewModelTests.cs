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

using System.IO;
using HeapScope.Application.Jobs;
using HeapScope.Application.Tutorial;
using HeapScope.Core;
using HeapScope.Tests.Shared;
using HeapScope.UI.ViewModels;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>
/// The Learn page (sprint 3.13, task 8 and its review): a lesson starts its sample with its scenarios and shows its start page,
/// its choices are the sample's own types, a wrong answer is refused, a right one is saved and read back by a new page, a
/// refused second lesson leaves none running rather than grading the first against the second, a sample that exits is
/// reported, stopping ends the sample, and where capture is not available no lesson starts.
/// </summary>
public sealed class LearnViewModelTests : IDisposable
{
    private const string TimerType = "Fixtures.Leaks.TimerTarget";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));
    private readonly FakeSampleStarter _starter = new();
    private readonly SampleService _samples;

    public LearnViewModelTests()
    {
        _samples = new SampleService(_starter, () => false, () => new SampleLaunch("heapscope-sample.exe", [], "heapscope-sample.exe"), folders: new HeapScopeFolders(_root));
    }

    private LessonProgress Progress => new(_root);

    [Fact]
    public async Task A_lesson_starts_its_sample_shows_its_page_and_saves_a_right_answer()
    {
        var learn = new LearnViewModel(_samples, Progress);
        learn.Lessons.Count.ShouldBe(LessonCatalog.All.Count);
        learn.ProgressText.ShouldBe($"0 of {LessonCatalog.All.Count} done");
        learn.Unavailable.ShouldBeNull();
        LessonPage? shown = null;
        learn.NavigateRequested += (_, page) => shown = page;

        learn.Selected = learn.Lessons.Single(l => l.Lesson.Id == "timers");
        await learn.StartLessonCommand.ExecuteAsync(null);

        _starter.Last.Id.ShouldBe(learn.SampleProcessId.ShouldNotBeNull());
        _starter.Started.Last().Arguments.ShouldContain("Timer");
        learn.Current.ShouldBe(learn.Selected);
        shown.ShouldBe(LessonPage.Processes);
        learn.Choices.Count.ShouldBe(Lesson.DecoyCount + 1);
        learn.Choices.ShouldContain(TimerType);
        learn.Choices.ShouldContain("Fixtures.Leaks.TimerLeak", "the scenario's own decoy");
        learn.Choices.ShouldAllBe(c => c.StartsWith("Fixtures.Leaks.", StringComparison.Ordinal), "the answer cannot be told by its name");

        learn.CheckAnswerCommand.Execute(null);
        learn.Status.ShouldBe("Choose an answer first.");

        learn.ChosenAnswer = "Fixtures.Leaks.TimerLeak";
        learn.CheckAnswerCommand.Execute(null);
        learn.Current!.IsDone.ShouldBeFalse();
        learn.Status.ShouldStartWith("Not this one.");

        learn.ChosenAnswer = TimerType;
        learn.CheckAnswerCommand.Execute(null);
        learn.Current.IsDone.ShouldBeTrue();
        learn.Status.ShouldBe($"Right: the sample planted {TimerType}. Lesson done; stop its sample, or start the next lesson.");
        learn.ProgressText.ShouldBe($"1 of {LessonCatalog.All.Count} done");
        Progress.Done.ShouldBe(["timers"]);

        var again = new LearnViewModel(_samples, Progress);
        again.Lessons.Single(l => l.Lesson.Id == "timers").IsDone.ShouldBeTrue("read back from the progress file");

        learn.StopLessonCommand.Execute(null);
        _starter.Last.Killed.ShouldBeTrue();
        learn.Current.ShouldBeNull();
        learn.SampleProcessId.ShouldBeNull();
        learn.Choices.ShouldBeEmpty();
        learn.CheckAnswerCommand.CanExecute(null).ShouldBeFalse();
        learn.Status.ShouldBe("Lesson stopped.");
    }

    [Fact]
    public async Task A_refused_second_lesson_leaves_none_running_instead_of_grading_the_first_against_it()
    {
        var learn = new LearnViewModel(_samples, Progress) { Selected = LessonNamed("timers") };
        await learn.StartLessonCommand.ExecuteAsync(null);
        var first = _starter.Last;

        learn.Selected = LessonNamed("static-cache");
        _starter.NextExitsWith = "boom";
        await learn.StartLessonCommand.ExecuteAsync(null);

        first.Killed.ShouldBeTrue();
        learn.Current.ShouldBeNull();
        learn.Choices.ShouldBeEmpty();
        learn.CheckAnswerCommand.CanExecute(null).ShouldBeFalse("nothing is graded against the other lesson's answer");
        learn.Status.ShouldBe("The lesson \"Timers never stopped\" was stopped, and \"A cache that never lets go\" could not start: The sample did not start: boom");
    }

    [Fact]
    public async Task A_sample_that_exits_is_reported_and_its_lesson_can_still_be_answered()
    {
        var learn = new LearnViewModel(_samples, Progress) { Selected = LessonNamed("timers") };
        await learn.StartLessonCommand.ExecuteAsync(null);

        // The fake reports the exit at once, and without a dispatcher the page hears of it at once.
        _starter.Last.Exit(code: 1);

        learn.SampleProcessId.ShouldBeNull();
        learn.Status.ShouldStartWith("The lesson's sample is no longer running.");
        learn.ChosenAnswer = TimerType;
        learn.CheckAnswerCommand.Execute(null);
        learn.Current.ShouldNotBeNull().IsDone.ShouldBeTrue();
    }

    [Fact]
    public async Task A_scenario_missing_from_the_sample_is_refused_as_not_available_here()
    {
        var learn = new LearnViewModel(_samples, Progress) { Selected = LessonNamed("large-objects") };

        await learn.StartLessonCommand.ExecuteAsync(null);

        learn.Current.ShouldBeNull();
        learn.Status.ShouldBe("This HeapScope's sample has no scenario LargeObjects, so this lesson is not available here.");
        _starter.Started.ShouldHaveSingleItem("only the catalog was read");
    }

    [Fact]
    public void Without_the_sample_or_without_capture_no_lesson_starts_and_the_page_says_why()
    {
        var noSample = new LearnViewModel(null, Progress) { Selected = new LessonItem(LessonCatalog.All[0], false) };
        noSample.StartLessonCommand.CanExecute(null).ShouldBeFalse();
        noSample.Unavailable.ShouldNotBeNull().ShouldContain("not in this installation");

        var capture = new FakeCaptureService(_root, new JobTracker(TimeProvider.System)) { UnavailableReason = "Capturing from HeapScope runs on Windows for now." };
        var noCapture = new LearnViewModel(_samples, Progress, capture: capture) { Selected = new LessonItem(LessonCatalog.All[0], false) };
        noCapture.StartLessonCommand.CanExecute(null).ShouldBeFalse();
        noCapture.Unavailable.ShouldBe("Capturing from HeapScope runs on Windows for now. Every lesson starts with a capture, so the lessons cannot be done here.");
    }

    [Fact]
    public void A_damaged_progress_file_counts_as_none_and_is_kept_beside_when_replaced()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "learn-progress.json");
        File.WriteAllText(file, "not json");

        Progress.Done.ShouldBeEmpty();
        Progress.MarkDone("closure");

        Progress.Done.ShouldBe(["closure"]);
        File.ReadAllText(file + ".bak").ShouldBe("not json");
        File.Exists(file + ".tmp").ShouldBeFalse();
    }

    public void Dispose()
    {
        _samples.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static LessonItem LessonNamed(string id) => new(LessonCatalog.Find(id)!, false);
}
