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
using HeapScope.Application.Tutorial;
using HeapScope.Core;
using HeapScope.UI.ViewModels;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>
/// The Learn page (sprint 3.13, task 8): a lesson starts its sample with its scenarios and shows its start page, a wrong answer
/// is refused, a right one is saved and read back by a new page, and stopping ends the sample.
/// </summary>
public sealed class LearnViewModelTests : IDisposable
{
    private const int SamplePid = 5151;
    private const string TimerType = "HeapScope.Leaks.TimerOwner";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));
    private readonly Starter _starter = new();
    private readonly SampleService _samples;

    public LearnViewModelTests()
    {
        _samples = new SampleService(_starter, () => false, () => new SampleLaunch("heapscope-sample.exe", [], "heapscope-sample.exe"));
    }

    private LessonProgress Progress => new(new HeapScopeFolders(_root));

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

        _starter.Arguments.Last().ShouldContain("Timer");
        learn.SampleProcessId.ShouldBe(SamplePid);
        learn.Current.ShouldBe(learn.Selected);
        shown.ShouldBe(LessonPage.Processes);
        learn.Choices.ShouldBe(learn.Selected.Lesson.Choices(TimerType));

        learn.CheckAnswerCommand.Execute(null);
        learn.Status.ShouldBe("Choose an answer first.");

        learn.ChosenAnswer = Lesson.Decoys[0];
        learn.CheckAnswerCommand.Execute(null);
        learn.Current!.IsDone.ShouldBeFalse();
        learn.Status.ShouldStartWith("Not this one.");

        learn.ChosenAnswer = TimerType;
        learn.CheckAnswerCommand.Execute(null);
        learn.Current.IsDone.ShouldBeTrue();
        learn.ProgressText.ShouldBe($"1 of {LessonCatalog.All.Count} done");
        Progress.Done.ShouldBe(["timers"]);

        var again = new LearnViewModel(_samples, Progress);
        again.Lessons.Single(l => l.Lesson.Id == "timers").IsDone.ShouldBeTrue("read back from the progress file");

        learn.StopLessonCommand.Execute(null);
        _starter.Killed.ShouldBeTrue();
        learn.Current.ShouldBeNull();
        learn.SampleProcessId.ShouldBeNull();
        learn.Choices.ShouldBeEmpty();
        learn.CheckAnswerCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task A_scenario_missing_from_the_sample_is_refused_with_what_to_do()
    {
        var learn = new LearnViewModel(_samples, Progress)
        {
            Selected = new LessonItem(LessonCatalog.Find("waste")!, false),
        };

        await learn.StartLessonCommand.ExecuteAsync(null);

        learn.Current.ShouldBeNull();
        learn.Status.ShouldBe("This HeapScope's sample has no scenario Waste. Reinstall HeapScope from its release archive.");
        _starter.Arguments.Count.ShouldBe(1, "only the catalog was read");
    }

    [Fact]
    public void Without_the_sample_no_lesson_starts_and_the_page_says_why()
    {
        var learn = new LearnViewModel(null, Progress) { Selected = new LessonItem(LessonCatalog.All[0], false) };

        learn.StartLessonCommand.CanExecute(null).ShouldBeFalse();
        learn.Unavailable.ShouldNotBeNull().ShouldContain("Reinstall");
    }

    [Fact]
    public void An_unreadable_progress_file_counts_as_no_progress()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "learn-progress.json"), "not json");

        Progress.Done.ShouldBeEmpty();
        Progress.MarkDone("closure");
        Progress.Done.ShouldBe(["closure"]);
    }

    public void Dispose()
    {
        _samples.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class Starter : ISampleProcessStarter
    {
        public List<IReadOnlyList<string>> Arguments { get; } = [];

        public bool Killed { get; private set; }

        public ISampleProcess Start(string fileName, IReadOnlyList<string> arguments)
        {
            Arguments.Add(arguments);
            return arguments.Contains("--list")
                ? new Process(this, 1, [$$"""[{"Name":"Timer","Title":"Timers","Plants":"p","KnownAnswer":"Timer","AnswerType":"{{TimerType}}","CannotRunWith":[],"Grows":true}]"""])
                : new Process(this, SamplePid, [$"READY pid={SamplePid}"]);
        }

        private sealed class Process(Starter owner, int id, IReadOnlyList<string> lines) : ISampleProcess
        {
            private int _next;

            public int Id { get; } = id;

            public bool HasExited => owner.Killed;

            public string Error => string.Empty;

            public Task<string?> ReadLineAsync(CancellationToken cancellationToken) => Task.FromResult(_next < lines.Count ? lines[_next++] : null);

            public void Kill() => owner.Killed = true;

            public void Dispose()
            {
            }
        }
    }
}
