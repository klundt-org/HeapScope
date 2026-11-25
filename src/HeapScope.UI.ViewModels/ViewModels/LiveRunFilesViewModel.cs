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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeapScope.Application.Sessions;
using HeapScope.UI.Services;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// The files of the run the Live page shows (split from <see cref="LiveViewModel"/> in sprint 4.3): its timeline, its allocation
/// trace and its snapshots. Files are looked at off the UI thread.
/// </summary>
public sealed partial class LiveRunFilesViewModel : ObservableObject
{
    private readonly LiveRunState _shown;
    private readonly IUserInteraction? _interaction;
    private readonly AllocationsViewModel? _allocations;
    private readonly ILogger _logger;

    /// <summary>The run's timeline file once it stopped, while it is there; null otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTimeline))]
    [NotifyCanExecuteChangedFor(nameof(OpenTimelineCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowTimelineCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteTimelineCommand))]
    private string? _timelinePath;

    /// <summary>The run's allocation trace, once it ended with one.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenTraceCommand))]
    private string? _tracePath;

    public LiveRunFilesViewModel(LiveRunState shown, IUserInteraction? interaction, AllocationsViewModel? allocations, ILogger logger)
    {
        _shown = shown ?? throw new ArgumentNullException(nameof(shown));
        _interaction = interaction;
        _allocations = allocations;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Whether the timeline row (its path, Open, Show in folder, Delete) is shown.</summary>
    public bool HasTimeline => TimelinePath is not null;

    /// <summary>The files of the run now shown; none while one is watched.</summary>
    public void Show(string? timeline, string? trace)
    {
        TimelinePath = timeline;
        TracePath = trace;
    }

    private bool CanOpenTrace() => TracePath is not null && _allocations is not null;

    /// <summary>Opens the run's allocation trace on the Allocations page (sprint 3.4).</summary>
    [RelayCommand(CanExecute = nameof(CanOpenTrace))]
    private async Task OpenTraceAsync()
    {
        if (TracePath is { } path && _allocations is { } allocations)
        {
            await allocations.OpenPathAsync(path);
        }
    }

    private bool CanUseTimeline() => TimelinePath is not null && _interaction is not null;

    [RelayCommand(CanExecute = nameof(CanUseTimeline))]
    private async Task OpenTimelineAsync()
    {
        if (await TimelineOrGoneAsync() is { } path)
        {
            var opened = await _interaction!.OpenFileAsync(path);
            _logger.LogInformation("Timeline open asked: {Outcome}", opened ? "opened" : "no app");
            if (!opened)
            {
                _shown.Status = "No app opened the timeline. It is a CSV file; Show in folder finds it.";
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseTimeline))]
    private async Task ShowTimelineAsync()
    {
        if (await TimelineOrGoneAsync() is { } path)
        {
            var shown = await _interaction!.ShowInFolderAsync(path);
            _logger.LogInformation("Timeline show in folder asked: {Outcome}", shown ? "shown" : "failed");
            if (!shown)
            {
                _shown.Status = "The folder could not be opened.";
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseTimeline))]
    private async Task DeleteTimelineAsync()
    {
        if (await TimelineOrGoneAsync() is not { } path
            || !await _interaction!.ConfirmAsync("Delete the timeline", $"Delete {Path.GetFileName(path)}? This cannot be undone. The run's snapshots are kept."))
        {
            return;
        }

        try
        {
            await Task.Run(() => File.Delete(path));
            Forget(path);
            _shown.Status = "Timeline deleted.";

            // The path of the user's own file, never its contents (ARCHITECTURE 11): a past run reopens from it.
            _logger.LogInformation("Timeline {Path} deleted after the user confirmed", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Deleting the timeline {Path} failed: {Error}", path, ex.GetType().Name);
            _shown.Status = "The timeline could not be deleted: " + ex.Message;
        }
    }

    /// <summary>Shows a rule's snapshot file in its folder (a failed capture's card has none).</summary>
    [RelayCommand]
    private async Task ShowSnapshotAsync(SnapshotCard? card)
    {
        if (card?.Path is not { } path || _interaction is null)
        {
            return;
        }

        if (!Session.IsLocal(path))
        {
            _logger.LogInformation("Snapshot show in folder refused: {Path} is not on this machine", path);
            _shown.Status = "That snapshot is not on this machine.";
            return;
        }

        if (!await Task.Run(() => File.Exists(path)))
        {
            _shown.Status = "That snapshot file is no longer there.";
            return;
        }

        var shown = await _interaction.ShowInFolderAsync(path);
        _logger.LogInformation("Snapshot show in folder asked: {Outcome}", shown ? "shown" : "failed");
        if (!shown)
        {
            _shown.Status = "The folder could not be opened.";
        }
    }

    /// <summary>The timeline's path, or null after saying it is gone (deleted or moved outside HeapScope).</summary>
    private async Task<string?> TimelineOrGoneAsync()
    {
        var path = TimelinePath;
        if (path is not null && await Task.Run(() => File.Exists(path)))
        {
            return path;
        }

        Forget(path);
        _shown.Status = "The timeline file is no longer there.";
        return null;
    }

    /// <summary>Forgets a timeline that is no longer there, unless another run's is shown by now.</summary>
    private void Forget(string? path)
    {
        if (TimelinePath == path)
        {
            TimelinePath = null;
        }
    }
}
