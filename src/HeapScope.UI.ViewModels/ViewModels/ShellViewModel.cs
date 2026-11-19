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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeapScope.Application;
using HeapScope.Application.Jobs;
using HeapScope.Application.Workers;
using HeapScope.Contracts;
using HeapScope.UI.Services;

namespace HeapScope.UI.ViewModels;

/// <summary>A navigation entry of the shell.</summary>
public sealed record NavigationItem(string Title, ObservableObject Page);

/// <summary>
/// The shell: navigation, the jobs panel, worker status and the crash banner. Application events arrive on any
/// thread and are marshaled to the UI thread through <see cref="IUiDispatcher"/>.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly WorkerOperations _operations;
    private readonly IUiDispatcher _dispatcher;
    private readonly Dictionary<Guid, JobViewModel> _jobsById = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsJobsPanelVisible))]
    [NotifyPropertyChangedFor(nameof(CurrentLesson))]
    private NavigationItem _selectedItem;

    [ObservableProperty]
    private string _workerStatus = "Worker not started";

    /// <summary>The Learn page (sprint 3.13), whose running lesson the shell shows beside every page; null without it.</summary>
    public LearnViewModel? Learn { get; }

    /// <summary>
    /// The lesson running, shown beside every page but the Learn page (which shows its steps itself); null when none, on the
    /// Learn page, or without one.
    /// </summary>
    public LessonItem? CurrentLesson => SelectedItem?.Page is LearnViewModel ? null : Learn?.Current;

    /// <summary>Shows the Learn page, from the lesson panel beside the other pages.</summary>
    [RelayCommand]
    private void ShowLearn()
    {
        if (NavigationItems.FirstOrDefault(i => i.Page == Learn) is { } item)
        {
            SelectedItem = item;
        }
    }

    private void ShowLessonPage(LessonPage page, ProcessesViewModel processes, LiveViewModel? live, AllocationsViewModel? allocations)
    {
        ObservableObject? target = page switch
        {
            LessonPage.Processes => processes,
            LessonPage.Snapshots => Snapshots,
            LessonPage.Live => live,
            LessonPage.Allocations => allocations,
            _ => null,
        };
        if (NavigationItems.FirstOrDefault(i => i.Page == target) is { } item)
        {
            SelectedItem = item;
        }

        if (page == LessonPage.Processes)
        {
            processes.RefreshCommand.Execute(null);
        }
    }

    /// <summary>True while the app builds its pages in the background after start (sprint 3.12, task 4); the status bar shows it.</summary>
    [ObservableProperty]
    private bool _isPreparingViews;

    [ObservableProperty]
    private bool _isCrashBannerVisible;

    [ObservableProperty]
    private string? _crashMessage;

    public ShellViewModel(WorkerOperations operations, IUiDispatcher dispatcher, HomeViewModel home, SnapshotsViewModel snapshots, ProcessesViewModel processes, AboutViewModel about, LiveViewModel? live = null, AllocationsViewModel? allocations = null, GridExportService? exports = null, SettingsViewModel? settings = null, LearnViewModel? learn = null)
    {
        Exports = exports ?? new GridExportService(new AutomaticUserInteraction());
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        Home = home ?? throw new ArgumentNullException(nameof(home));
        Snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        JobsPage = new JobsPageViewModel(Jobs, operations.Jobs);
        Jobs.CollectionChanged += (_, _) => RaiseJobsChanged();
        NavigationItems =
        [
            new NavigationItem("Home", Home),
            new NavigationItem("Snapshots", Snapshots),
            .. allocations is null ? Array.Empty<NavigationItem>() : [new NavigationItem("Allocations", allocations)],
            new NavigationItem("Processes", processes ?? throw new ArgumentNullException(nameof(processes))),
            .. live is null ? Array.Empty<NavigationItem>() : [new NavigationItem("Live", live)],
            new NavigationItem("Jobs", JobsPage),
            .. learn is null ? Array.Empty<NavigationItem>() : [new NavigationItem("Learn", learn)],
            .. settings is null ? Array.Empty<NavigationItem>() : [new NavigationItem("Settings", settings)],
            new NavigationItem("About", about ?? throw new ArgumentNullException(nameof(about))),
        ];
        Live = live;
        Allocations = allocations;
        Learn = learn;
        if (learn is not null)
        {
            // A lesson started shows the page it starts on (sprint 3.13).
            learn.NavigateRequested += (_, page) => ShowLessonPage(page, processes, live, allocations);
            learn.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LearnViewModel.Current))
                {
                    OnPropertyChanged(nameof(CurrentLesson));
                }
            };
        }

        About = about;
        about.Shortcuts = ShortcutList;
        about.Diagnostics = Home;
        if (allocations is not null)
        {
            // A trace opened or recorded shows the Allocations page.
            allocations.TraceShown += OnTraceShown;
        }
        _selectedItem = NavigationItems[0];

        foreach (var job in operations.Jobs.Jobs)
        {
            AddJob(job);
        }

        Home.RefreshSessionsCommand.Execute(null);

        // Opening a snapshot (from Home or the Snapshots page) shows it.
        Snapshots.PropertyChanged += OnSnapshotsPropertyChanged;
        operations.Jobs.JobAdded += OnJobAdded;
        operations.Jobs.JobChanged += OnJobChanged;
        operations.Worker.StateChanged += OnWorkerStateChanged;
        UpdateWorkerStatus(operations.Worker.State, null);
    }

    public string Title { get; } = $"{ProductInfo.Name} {ProductInfo.Version}";

    public IReadOnlyList<NavigationItem> NavigationItems { get; }

    public HomeViewModel Home { get; }

    public SnapshotsViewModel Snapshots { get; }

    public JobsPageViewModel JobsPage { get; }

    /// <summary>The Live page (sprint 1.7), when the host provides monitoring.</summary>
    public LiveViewModel? Live { get; }

    /// <summary>The Allocations page (sprint 2.5), when the host has one.</summary>
    public AllocationsViewModel? Allocations { get; }

    /// <summary>Exports grids to CSV (sprint 2.7); the window hands it every grid's export request.</summary>
    public GridExportService Exports { get; }

    /// <summary>The About page, which lists the keyboard shortcuts (F1, sprint 2.7).</summary>
    public AboutViewModel About { get; }

    /// <summary>Shows the page at <paramref name="index"/> of the sidebar (Command+1 and on); false when there is none.</summary>
    public bool ShowPage(int index)
    {
        if (index < 0 || index >= NavigationItems.Count)
        {
            return false;
        }

        SelectedItem = NavigationItems[index];
        return true;
    }

    /// <summary>Shows a tab of the Snapshots page (Alt+1 to 4).</summary>
    public void ShowSnapshotTab(SnapshotTabs tab)
    {
        SelectedItem = NavigationItems.First(i => i.Page == Snapshots);
        Snapshots.Tab = tab;
    }

    /// <summary>Shows the keyboard shortcuts (F1): the About page lists them.</summary>
    public void ShowHelp() => SelectedItem = NavigationItems.First(i => i.Page == About);

    /// <summary>Refreshes the shown page (F5): the processes, the sessions or the live counters.</summary>
    public void RefreshPage() => Refresh(SelectedItem?.Page);

    /// <summary>The keyboard shortcuts, for the shortcuts list (sprint 2.7).</summary>
    public IReadOnlyList<Shortcut> ShortcutList => Shortcuts.List([.. NavigationItems.Select(i => i.Title)]);

    /// <summary>
    /// The jobs panel shows only while a job runs, and not on the Jobs page, which lists them anyway. Finished jobs stay on
    /// the Jobs page; <see cref="FinishedJobsText"/> says so in the status line.
    /// </summary>
    public bool IsJobsPanelVisible => SelectedItem?.Page != JobsPage && Jobs.Any(j => j.IsRunning);

    /// <summary>The status line's note on finished jobs and how many failed; empty when there are none.</summary>
    public string FinishedJobsText
    {
        get
        {
            var finished = Jobs.Count(j => !j.IsRunning);
            var failed = Jobs.Count(j => j.Status == JobStatus.Failed);
            var text = finished switch { 0 => string.Empty, 1 => "1 job finished", _ => $"{finished} jobs finished" };
            return failed > 0 ? $"{text}, {failed} failed" : text;
        }
    }

    /// <summary>All jobs, newest first; the jobs panel and the Jobs page bind to it.</summary>
    public ObservableCollection<JobViewModel> Jobs { get; } = [];

    public void Dispose()
    {
        Snapshots.PropertyChanged -= OnSnapshotsPropertyChanged;
        if (Allocations is not null)
        {
            Allocations.TraceShown -= OnTraceShown;
        }

        Snapshots.Dispose();
        _operations.Jobs.JobAdded -= OnJobAdded;
        _operations.Jobs.JobChanged -= OnJobChanged;
        _operations.Worker.StateChanged -= OnWorkerStateChanged;
    }

    [RelayCommand]
    private async Task RestartWorkerAsync(CancellationToken cancellationToken)
    {
        IsCrashBannerVisible = false;
        try
        {
            await _operations.Worker.RestartAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is WorkerException or OperationCanceledException)
        {
            CrashMessage = ex.Message;
            IsCrashBannerVisible = true;
        }
    }

    [RelayCommand]
    private void DismissCrashBanner() => IsCrashBannerVisible = false;

    [RelayCommand]
    private void ShowJobs() => SelectedItem = NavigationItems.First(i => i.Page == JobsPage);

    [RelayCommand]
    private void ShowProcesses() => SelectedItem = NavigationItems.First(i => i.Page is ProcessesViewModel);

    private bool HasLive() => Live is not null;

    [RelayCommand(CanExecute = nameof(HasLive))]
    private void ShowLive() => SelectedItem = NavigationItems.First(i => i.Page == Live);

    private void RaiseJobsChanged()
    {
        OnPropertyChanged(nameof(IsJobsPanelVisible));
        OnPropertyChanged(nameof(FinishedJobsText));
    }

    /// <summary>Pages that show live data refresh when they are shown.</summary>
    partial void OnSelectedItemChanged(NavigationItem value) => Refresh(value?.Page);

    private static void Refresh(ObservableObject? page)
    {
        switch (page)
        {
            case ProcessesViewModel processes when processes.RefreshCommand.CanExecute(null):
                processes.RefreshCommand.Execute(null);
                break;
            case HomeViewModel home when home.RefreshSessionsCommand.CanExecute(null):
                home.RefreshSessionsCommand.Execute(null);
                break;
            case LiveViewModel live when !live.IsRunning && live.RefreshCommand.CanExecute(null):
                live.RefreshCommand.Execute(null);
                break;
            case SettingsViewModel settings:
                settings.Reload();
                break;
        }
    }

    private void OnSnapshotsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SnapshotsViewModel.Selected) && Snapshots.Selected is not null)
        {
            SelectedItem = NavigationItems[1];
        }
    }

    private void OnTraceShown(object? sender, EventArgs e) =>
        SelectedItem = NavigationItems.First(i => i.Page == Allocations);

    private void OnJobAdded(object? sender, TrackedJob job) => _dispatcher.Post(() => AddJob(job));

    private void OnJobChanged(object? sender, TrackedJob job) => _dispatcher.Post(() =>
    {
        if (_jobsById.TryGetValue(job.Id, out var viewModel))
        {
            viewModel.Refresh();
            RaiseJobsChanged();
        }
    });

    private void OnWorkerStateChanged(object? sender, WorkerStateChangedEventArgs e) =>
        _dispatcher.Post(() => UpdateWorkerStatus(e.State, e.Message));

    private void AddJob(TrackedJob job)
    {
        if (_jobsById.ContainsKey(job.Id))
        {
            return;
        }

        var viewModel = new JobViewModel(job);
        _jobsById.Add(job.Id, viewModel);
        Jobs.Insert(0, viewModel);
    }

    private void UpdateWorkerStatus(WorkerState state, string? message)
    {
        WorkerStatus = state switch
        {
            WorkerState.NotStarted => "Worker not started",
            WorkerState.Starting => "Worker starting",
            WorkerState.Running => _operations.Worker.Info is { } info ? $"Worker running (pid {info.ProcessId})" : "Worker running",
            WorkerState.Crashed => "Worker stopped unexpectedly",
            WorkerState.Stopped => "Worker stopped",
            _ => state.ToString(),
        };

        if (state == WorkerState.Crashed)
        {
            CrashMessage = message ?? "The analysis worker stopped unexpectedly.";
            IsCrashBannerVisible = true;
        }
        else if (state == WorkerState.Running)
        {
            IsCrashBannerVisible = false;
        }
    }
}
