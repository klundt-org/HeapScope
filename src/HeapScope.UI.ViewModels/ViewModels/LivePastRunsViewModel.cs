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
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeapScope.Application.Monitoring;
using HeapScope.Application.Sessions;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// The Live page's past runs (sprint 3.4; split from <see cref="LiveViewModel"/> in sprint 4.3): the current session's monitoring
/// runs, and showing one read only.
/// </summary>
public sealed partial class LivePastRunsViewModel : ObservableObject
{
    private readonly ICaptureService _capture;
    private readonly LiveRunState _shown;
    private readonly LiveRunFilesViewModel _files;
    private readonly ILogger _logger;

    /// <summary>Whether a run is being watched: none is shown over it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenPastRunCommand))]
    private bool _isRunning;

    public LivePastRunsViewModel(ICaptureService capture, LiveRunState shown, LiveRunFilesViewModel files, ILogger logger)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _shown = shown ?? throw new ArgumentNullException(nameof(shown));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The current session's monitoring runs, newest first (sprint 3.4).</summary>
    public ObservableCollection<PastRunItem> Runs { get; } = [];

    /// <summary>Lists the current session's monitoring runs (sprint 3.4).</summary>
    [RelayCommand]
    private async Task RefreshRunsAsync()
    {
        // Read from disk, off the UI thread (a read waits up to a second for a program that holds the document, sprint 3.6
        // review): runs are recorded there as they start and end (never creating a session to list nothing).
        var folder = _capture.Library.CurrentFolder;
        var session = folder is null ? null : await Task.Run(() => SessionLibrary.TryRead(folder));
        Runs.Clear();
        if (session is null)
        {
            return;
        }

        foreach (var run in (session.Document.Runs ?? []).OrderByDescending(r => r.StartedUtc))
        {
            Runs.Add(new PastRunItem(run, session.Folder));
        }
    }

    private bool CanOpenPastRun(PastRunItem? item) => !IsRunning;

    /// <summary>
    /// Shows a past run read only (sprint 3.4, decision 5): its timeline's counters, its snapshots and forced collections as
    /// markers, and its snapshots as cards. Everything is read before anything is shown.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenPastRun))]
    private async Task OpenPastRunAsync(PastRunItem? item)
    {
        if (item is null)
        {
            return;
        }

        var run = item.Run;

        // Only a CSV timeline inside its session folder, reached through no link: Delete timeline acts on this path, and
        // Open timeline hands it to the system, so a received session cannot name a program to run (peer review S3).
        var (timelineCheck, traceCheck) = await Task.Run(() => Session.RunFiles(item.Folder, run));
        if (timelineCheck.Path is not { } path)
        {
            _logger.LogInformation("Monitoring run {RunId} not reopened: its timeline {Timeline} was refused ({Refusal})", run.Id, run.Timeline ?? "(none recorded)", timelineCheck.Refusal);
            Say(timelineCheck.Describe("This run's timeline"));
            return;
        }

        try
        {
            var (read, cards) = await Task.Run(async () => (await TimelineFile.ReadAsync(path, CancellationToken.None), Cards(item)));
            if (IsRunning)
            {
                _logger.LogInformation("Monitoring run {RunId} not shown: a run started while its timeline was read", run.Id);
                return;
            }

            _shown.Show(
                read.Samples,
                read.Events.Where(e => e.Text.StartsWith(MonitorRun.SnapshotEventPrefix, StringComparison.Ordinal)).Select(e => e.At),
                read.Events.Where(e => e.Text == MonitorRun.GcForcedEvent).Select(e => e.At),
                cards,
                read.Samples.Count > 0 ? read.Samples[0].At : null,
                item.Title + (read.SkippedLines > 0 ? string.Create(CultureInfo.CurrentCulture, $" ({read.SkippedLines} unreadable lines left out)") : string.Empty));
            _files.Show(path, traceCheck.Path);
            if (run.Trace is not null && traceCheck.Path is null)
            {
                _logger.LogInformation("Monitoring run {RunId}: its trace {Trace} was refused ({Refusal})", run.Id, run.Trace, traceCheck.Refusal);
            }

            _shown.Status = "Showing a past run, read only. Start monitoring to see live data again.";
            _shown.RunWarning = null;
            _logger.LogInformation(
                "Reopened monitoring run {RunId} of pid {Pid} from {Timeline}: {Samples} samples ({Shown} shown, {Skipped} unreadable lines), {Events} events, {Snapshots} snapshots",
                run.Id,
                run.ProcessId,
                path,
                read.Samples.Count,
                _shown.Samples.Count,
                read.SkippedLines,
                read.Events.Count,
                _shown.Snapshots.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Monitoring run {RunId}: its timeline could not be read", run.Id);
            Say("The run's timeline could not be read: " + ex.Message);
        }
    }

    /// <summary>Says why a past run is not shown, unless a run that started meanwhile has the page.</summary>
    private void Say(string status)
    {
        if (!IsRunning)
        {
            _shown.Status = status;
        }
    }

    /// <summary>The run's snapshots as cards, newest first (reads the session's document and looks for the files).</summary>
    private static List<SnapshotCard> Cards(PastRunItem item)
    {
        var cards = new List<SnapshotCard>();
        if (SessionLibrary.TryRead(item.Folder) is not { } session)
        {
            return cards;
        }

        foreach (var snapshot in session.Document.Snapshots.Where(s => item.Run.SnapshotIds.Contains(s.Id)).OrderByDescending(s => s.CapturedUtc))
        {
            // A received session may name a network path: it is never looked at (sprint 3.7, peer review S3).
            var file = session.PathOf(snapshot);
            var gone = !Session.IsLocal(file) ? "not on this machine" : File.Exists(file) ? null : "deleted";
            cards.Add(new SnapshotCard(snapshot.CapturedUtc, snapshot.Label ?? snapshot.Source.ToString(), file, snapshot.SizeBytes, gone));
        }

        return cards;
    }
}
