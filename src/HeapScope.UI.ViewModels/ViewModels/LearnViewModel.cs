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
using HeapScope.Application.Tutorial;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// The Learn page (sprint 3.13, ADR-042): the lessons with their progress; a lesson started runs its sample and shows the page it
/// starts on, its steps stay beside every page (the shell's lesson panel), and its question is answered here, checked against
/// the type the sample planted. A new feature's lesson is one entry in <see cref="LessonCatalog"/>.
/// </summary>
public sealed partial class LearnViewModel : ObservableObject
{
    private readonly SampleService? _samples;
    private readonly LessonProgress _progress;
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

    public LearnViewModel(SampleService? samples, LessonProgress progress, ILogger<LearnViewModel>? logger = null)
    {
        _samples = samples;
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _logger = logger ?? NullLogger<LearnViewModel>.Instance;
        var done = progress.Done;
        foreach (var lesson in LessonCatalog.All)
        {
            Lessons.Add(new LessonItem(lesson, done.Contains(lesson.Id)));
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

    /// <summary>Why no lesson can run here (no sample, HeapScope elevated), or null.</summary>
    public string? Unavailable => _samples is null
        ? "The sample is not in this installation. Reinstall HeapScope from its release archive."
        : _samples.UnavailableReason;

    private bool CanStart() => Selected is not null && !IsBusy && _samples is not null;

    /// <summary>Starts the selected lesson: stops the one running, starts its sample, then shows the page it starts on.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartLessonAsync()
    {
        var item = Selected!;
        IsBusy = true;
        try
        {
            StopSample();
            var catalog = await _samples!.ListScenariosAsync(CancellationToken.None);
            _answerType = catalog.FirstOrDefault(s => s.Name == item.Lesson.AnswerScenario)?.AnswerType
                ?? throw new SampleRefusedException($"This HeapScope's sample has no scenario {item.Lesson.AnswerScenario}. Reinstall HeapScope from its release archive.");
            var sample = await _samples.StartAsync(item.Lesson.Scenarios, item.Lesson.Grows, CancellationToken.None);
            SampleProcessId = sample.ProcessId;
            Choices.Clear();
            foreach (var choice in item.Lesson.Choices(_answerType))
            {
                Choices.Add(choice);
            }

            ChosenAnswer = null;
            Current = item;
            Status = $"Lesson started: sample {sample.ProcessId} runs. Follow the steps; they stay on the right while you work.";
            _logger.LogInformation("Lesson {Lesson} started with sample {Pid}", item.Lesson.Id, sample.ProcessId);
            NavigateRequested?.Invoke(this, item.Lesson.StartPage);
        }
        catch (SampleRefusedException ex)
        {
            Status = ex.Message;
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
        if (ChosenAnswer is null)
        {
            Status = "Choose an answer first.";
            return;
        }

        var right = Lesson.IsRight(ChosenAnswer, _answerType!);
        _logger.LogInformation("Lesson {Lesson} answered {Answer}: {Outcome}", Current!.Lesson.Id, ChosenAnswer, right ? "right" : "wrong");
        if (!right)
        {
            Status = "Not this one. Follow the steps again and look for the type the sample planted.";
            return;
        }

        Current.IsDone = true;
        OnPropertyChanged(nameof(ProgressText));
        try
        {
            _progress.MarkDone(Current.Lesson.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The lesson progress could not be saved");
        }

        Status = $"Right: the sample planted {_answerType}. Lesson done; stop its sample, or start the next lesson.";
    }

    /// <summary>Stops the running lesson and its sample.</summary>
    [RelayCommand(CanExecute = nameof(HasCurrent))]
    private void StopLesson()
    {
        StopSample();
        Current = null;
        Choices.Clear();
        Status = "Lesson stopped.";
    }

    private void StopSample()
    {
        if (SampleProcessId is { } id)
        {
            _samples?.Stop(id);
            SampleProcessId = null;
        }
    }
}
