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

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeapScope.Application.Settings;
using HeapScope.UI.Services;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// The Settings page (sprint 3.12, B1; owner, 2026-11-12), above About: its Logging section has Include filter text in logs
/// (on by default; the app, the worker and the command line follow it), Include dump values in logs (off by default, and
/// turned on only after the user confirms; owner, 2026-11-19), Purge logs and Purge all, which asks first. The page shows the
/// settings file each time it is shown, since the command line can change it while the app runs.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly IUserInteraction _interaction;
    private readonly ILogger? _logger;
    private bool _reverting;
    private bool _confirming;

    [ObservableProperty]
    private bool _includeFilterTextInLogs;

    [ObservableProperty]
    private bool _includeDumpValuesInLogs;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PurgeLogsCommand), nameof(PurgeAllCommand))]
    private bool _isBusy;

    public SettingsViewModel(SettingsService settings, IUserInteraction interaction, ILogger<SettingsViewModel>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _logger = logger;
        _reverting = true;
        IncludeFilterTextInLogs = settings.IncludeFilterTextInLogs;
        IncludeDumpValuesInLogs = settings.IncludeDumpValuesInLogs;
        _reverting = false;
    }

    /// <summary>The question asked before dump values are turned on, while it is open (tests wait for it).</summary>
    internal Task DumpValuesConfirmation { get; private set; } = Task.CompletedTask;

    /// <summary>Shows the settings as saved now: another HeapScope process or the command line may have changed them.</summary>
    public void Reload()
    {
        if (_confirming)
        {
            return;
        }

        _reverting = true;
        IncludeFilterTextInLogs = _settings.IncludeFilterTextInLogs;
        IncludeDumpValuesInLogs = _settings.IncludeDumpValuesInLogs;
        _reverting = false;
    }

    /// <summary>Where the logs are, shown under the buttons.</summary>
    public string LogFolder => _settings.LogFolder;

    private bool CanPurge => !IsBusy;

    partial void OnIncludeFilterTextInLogsChanged(bool value)
    {
        if (_reverting)
        {
            return;
        }

        try
        {
            _settings.SetIncludeFilterTextInLogs(value);
            Status = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "The setting could not be saved to {File}", _settings.SettingsPath);
            Status = $"The setting could not be saved to {_settings.SettingsPath}: {ex.Message}";
            _reverting = true;
            IncludeFilterTextInLogs = !value;
            _reverting = false;
        }
    }

    partial void OnIncludeDumpValuesInLogsChanged(bool value)
    {
        if (_reverting)
        {
            return;
        }

        if (_confirming)
        {
            // The question is open (review M8): the box shows on until it is answered.
            Revert(true);
            return;
        }

        if (value)
        {
            // Not saved until the user says yes; a no turns the check box off again.
            _confirming = true;
            DumpValuesConfirmation = ConfirmDumpValuesAsync();
            return;
        }

        SaveDumpValues(false);
    }

    private async Task ConfirmDumpValuesAsync()
    {
        bool yes;
        try
        {
            yes = await _interaction.ConfirmAsync(InfoTexts.DumpValuesQuestionTitle, InfoTexts.DumpValuesQuestion, "Turn on", "Cancel");
        }
        catch (Exception ex)
        {
            // Never on without an answer (review M8): the question could not be shown, so nothing is saved.
            _logger?.LogWarning(ex, "The question to include dump values in logs could not be shown; the setting stays off");
            yes = false;
        }
        finally
        {
            _confirming = false;
        }

        _logger?.LogInformation("Asked to include dump values in logs; the user answered {Answer}", yes ? "yes" : "no");
        if (yes)
        {
            SaveDumpValues(true);
        }
        else
        {
            Revert(false);
        }
    }

    private void SaveDumpValues(bool include)
    {
        try
        {
            _settings.SetIncludeDumpValuesInLogs(include);
            Status = include ? InfoTexts.DumpValuesOn : InfoTexts.DumpValuesOff;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "The setting could not be saved to {File}", _settings.SettingsPath);
            Status = $"The setting could not be saved to {_settings.SettingsPath}: {ex.Message}";
            Revert(!include);
        }
    }

    private void Revert(bool include)
    {
        _reverting = true;
        IncludeDumpValuesInLogs = include;
        _reverting = false;
    }

    [RelayCommand(CanExecute = nameof(CanPurge))]
    private async Task PurgeLogsAsync()
    {
        IsBusy = true;
        try
        {
            var deletion = await Task.Run(_settings.PurgeLogs);
            Status = deletion.Complete
                ? string.Create(CultureInfo.CurrentCulture, $"Deleted {deletion.Files.Count} log files ({Format.Bytes(deletion.Bytes)}).")
                : Failed(deletion.Failed!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The review's M10: the log folder could not be listed; shown with a next step, not the last-chance dialog.
            _logger?.LogWarning(ex, "Purge logs could not list {Folder}", _settings.LogFolder);
            Status = Stopped(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPurge))]
    private async Task PurgeAllAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var plan = await Task.Run(_settings.PlanPurgeAll, cancellationToken);
            if (plan.Refused is { } refused)
            {
                Status = "Nothing was deleted: " + refused;
                return;
            }

            var question = string.Create(
                CultureInfo.CurrentCulture,
                $"This deletes HeapScope's logs and its whole library: {plan.Sessions} sessions, {plan.Files.Files.Count} files, {Format.Bytes(plan.Files.Bytes)}. Dumps you opened from your own folders stay. This cannot be undone.");
            var yes = await _interaction.ConfirmAsync("Delete everything HeapScope keeps?", question, "Delete everything", "Cancel");
            _logger?.LogInformation("Asked to purge all ({Sessions} sessions, {Files} files, {Bytes} bytes); the user answered {Answer}", plan.Sessions, plan.Files.Files.Count, plan.Files.Bytes, yes ? "yes" : "no");
            if (!yes)
            {
                return;
            }

            var (done, deleted) = await _settings.PurgeAllAsync(cancellationToken);
            Status = deleted.Complete
                ? string.Create(CultureInfo.CurrentCulture, $"Deleted everything HeapScope keeps: {done.Sessions} sessions, {deleted.Files.Count} files ({Format.Bytes(deleted.Bytes)}).")
                : Failed(deleted.Failed!);
        }
        catch (InvalidOperationException ex)
        {
            Status = "Nothing was deleted: " + ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HeapScope.Application.Workers.WorkerException)
        {
            // The review's M10: a folder that could not be listed, or a snapshot the worker could not close; the service logged it.
            _logger?.LogWarning(ex, "Purge all stopped");
            Status = Stopped(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Stopped(Exception ex) => string.Create(
        CultureInfo.CurrentCulture,
        $"Stopped before deleting anything more: {ex.Message} Try again; HeapScope's log has the details.");

    private static string Failed(IReadOnlyList<string> failed) => string.Create(
        CultureInfo.CurrentCulture,
        $"{failed.Count} files could not be deleted, usually because another program has them open: {string.Join("; ", failed.Take(5))}{(failed.Count > 5 ? " and more" : string.Empty)}. Close it and try again.");
}
