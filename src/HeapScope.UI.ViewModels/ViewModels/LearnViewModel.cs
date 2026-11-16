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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeapScope.Application.Sessions;
using HeapScope.Application.Tutorial;
using HeapScope.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// The Learn page (sprint 3.13, ADR-042): the lessons with their progress; a lesson started runs its sample and shows the page it
/// starts on, its steps stay beside every page (the shell's lesson panel), and its question is answered here, checked against
/// the type the sample planted. A new feature's lesson is one entry in <see cref="LessonCatalog"/>.
/// The answer is checked against what was read when the lesson started, and only once its sample is running (review H3); the
/// log has the lesson, its scenarios, the expected type and the choices, so a contested answer can be backed up (review H5).
/// </summary>
public sealed partial class LearnViewModel : ObservableObject
{
    private readonly SampleService? _samples;
    private readonly LessonProgress _progress;
    private readonly ICaptureService? _capture;
    private readonly IUiDispatcher? _dispatcher;
    private readonly ILogger _logger;
    private string? _answerType;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartLessonCommand))]
    private LessonItem? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrent))]
    [NotifyCanExecuteChangedFor(nameof(CheckAnswerCommand), nameof(StopLessonCommand))]
    private LessonItem? _current;

    [ObservableProperty]
    private string? _chosenAnswer;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartLessonCommand))]
    private bool _isBusy;

    /// <param name="capture">Capture, which every lesson starts with: where it is not available, no lesson starts (review M3).</param>
    /// <param name="dispatcher">Where a sample's exit is reported (it is raised on any thread); null runs it at once.</param>
    public LearnViewModel(SampleService? samples, LessonProgress progress, ILogger<LearnViewModel>? logger = null, ICaptureService? capture = null, IUiDispatcher? dispatcher = null)
    {
        _samples = samples;
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _capture = capture;
        _dispatcher = dispatcher;
        _logger = logger ?? NullLogger<LearnViewModel>.Instance;
        var done = progress.Done;
        foreach (var lesson in LessonCatalog.All)
        {
            Lessons.Add(new LessonItem(lesson, done.Contains(lesson.Id)));
        }

        if (_samples is not null)
        {
            _samples.Changed += (_, _) => Post(OnSamplesChanged);
        }
    }

    /// <summary>Raised to show the page a lesson starts on.</summary>
    public event EventHandler<LessonPage>? NavigateRequested;

    public ObservableCollection<LessonItem> Lessons { get; } = [];

    /// <summary>The answers to choose from for the lesson running.</summary>
    public ObservableCollection<string> Choices { get; } = [];

    /// <summary>The running lesson's sample, or null.</summary>
    public int? SampleProcessId { get; private set; }

    public bool HasCurrent => Current is not null;

    /// <summary>"3 of 12 done".</summary>
    public string ProgressText => $"{Lessons.Count(l => l.IsDone)} of {Lessons.Count} done";

    /// <summary>Why no lesson can run here (no sample, HeapScope elevated, no capture on this system), or null.</summary>
    public string? Unavailable => _samples is null
        ? $"The sample is not in this installation. {SampleService.RepairStep}."
        : _samples.UnavailableReason
            ?? (_capture?.UnavailableReason is { } capture ? $"{capture} Every lesson starts with a capture, so the lessons cannot be done here." : null);

    private bool CanStart() => Selected is not null && !IsBusy && Unavailable is null;

    /// <summary>Starts the selected lesson: stops the one running, starts its sample, then shows the page it starts on.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartLessonAsync()
    {
        if (Selected is not { } item || _samples is null)
        {
            return;
        }

        IsBusy = true;
        var previous = Current;
        try
        {
            // The lesson running ends first (its sample frees a slot); if the new one is refused, none runs and the page says so.
            EndLesson("another lesson started");
            var catalog = await _samples.ListScenariosAsync(CancellationToken.None);
            var answer = catalog.FirstOrDefault(s => s.Name == item.Lesson.AnswerScenario)
                ?? throw new SampleRefusedException($"This HeapScope's sample has no scenario {item.Lesson.AnswerScenario}, so this lesson is not available here.");
            var choices = item.Lesson.Choices(answer.AnswerType, answer.Decoys, [.. catalog.Select(s => s.AnswerType)]);
            var sample = await _samples.StartAsync(item.Lesson.Scenarios, item.Lesson.Grows, CancellationToken.None);
            _answerType = answer.AnswerType;
            SampleProcessId = sample.ProcessId;
            Choices.Clear();
            foreach (var choice in choices)
            {
                Choices.Add(choice);
            }

            ChosenAnswer = null;
            Current = item;
            Status = $"Lesson started: sample {sample.ProcessId} runs. Follow the steps; they stay on the right while you work.";
            _logger.LogInformation(
                "Lesson {Lesson} started with sample {Pid}: scenarios {Scenarios}, growing {Grows}, answer {AnswerType} (scenario {AnswerScenario}, sample catalog version {CatalogVersion}, lessons version {LessonsVersion}); choices {Choices}",
                item.Lesson.Id,
                sample.ProcessId,
                string.Join(", ", item.Lesson.Scenarios),
                item.Lesson.Grows,
                answer.AnswerType,
                answer.Name,
                _samples.CatalogVersion,
                LessonCatalog.Version,
                string.Join(", ", choices));
            NavigateRequested?.Invoke(this, item.Lesson.StartPage);

            // A sample that exited before its pid was kept here was reported to no one: say so now.
            OnSamplesChanged();
        }
        catch (SampleRefusedException ex)
        {
            _logger.LogInformation("Lesson {Lesson} could not start: {Reason}", item.Lesson.Id, ex.Message);
            Status = previous is null ? ex.Message : $"The lesson \"{previous.Title}\" was stopped, and \"{item.Title}\" could not start: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Checks the answer chosen against the type the lesson's sample planted; a right one is saved as done.</summary>
    [RelayCommand(CanExecute = nameof(HasCurrent))]
    private void CheckAnswer()
    {
        if (Current is not { } current || _answerType is not { } expected)
        {
            return;
        }

        if (ChosenAnswer is not { } chosen)
        {
            Status = "Choose an answer first.";
            return;
        }

        var right = Lesson.IsRight(chosen, expected);
        _logger.LogInformation(
            "Lesson {Lesson} answered {Answer}; expected {Expected} (sample {Pid}, running {Running}): {Outcome}",
            current.Lesson.Id,
            chosen,
            expected,
            SampleProcessId,
            SampleProcessId is { } pid && _samples?.IsSample(pid) == true,
            right ? "right" : "wrong");
        if (!right)
        {
            Status = "Not this one. Follow the steps again and look for the type the sample planted.";
            return;
        }

        current.IsDone = true;
        OnPropertyChanged(nameof(ProgressText));
        try
        {
            _progress.MarkDone(current.Lesson.Id);
            Status = $"Right: the sample planted {expected}. Lesson done; stop its sample, or start the next lesson.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The lesson progress could not be saved");
            Status = $"Right: the sample planted {expected}. Your progress could not be saved ({ex.Message}); HeapScope's log has the details.";
        }
    }

    /// <summary>Stops the running lesson and its sample.</summary>
    [RelayCommand(CanExecute = nameof(HasCurrent))]
    private void StopLesson()
    {
        EndLesson("Stop lesson");
        Status = "Lesson stopped.";
    }

    private void EndLesson(string reason)
    {
        if (Current is { } current)
        {
            _logger.LogInformation("Lesson {Lesson} ended ({Reason}), sample {Pid}", current.Lesson.Id, reason, SampleProcessId);
        }

        if (SampleProcessId is { } id)
        {
            SampleProcessId = null;
            _samples?.Stop(id, "lesson: " + reason);
        }

        Current = null;
        _answerType = null;
        Choices.Clear();
    }

    // Review H3: a lesson whose sample was stopped elsewhere, or exited, says so; its steps and question stay.
    private void OnSamplesChanged()
    {
        if (SampleProcessId is { } id && _samples?.IsSample(id) != true)
        {
            SampleProcessId = null;
            _logger.LogInformation("Lesson {Lesson}'s sample {Pid} is no longer running", Current?.Lesson.Id, id);
            Status = "The lesson's sample is no longer running. You can still answer from a dump you captured, or press Start lesson to run it again.";
        }
    }

    private void Post(Action action)
    {
        if (_dispatcher is null)
        {
            action();
        }
        else
        {
            _dispatcher.Post(action);
        }
    }
}
