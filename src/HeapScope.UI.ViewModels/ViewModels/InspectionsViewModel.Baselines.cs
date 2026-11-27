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
using HeapScope.Application.Snapshots;
using HeapScope.Application.Workers;
using HeapScope.Contracts;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>The growth detectors' baseline (ADR-018): the choices, the default, and running the growth tiles again when it changes.</summary>
public sealed partial class InspectionsViewModel
{
    /// <summary>
    /// The baseline choices for <paramref name="snapshot"/>: none, and the other open snapshots, latest first. The latest
    /// one of the same process captured earlier is chosen (a growth detector needs the earlier dump; ADR-018). With
    /// <paramref name="keep"/>, a chosen baseline that is still open stays chosen (snapshots opened or closed).
    /// </summary>
    private void ChooseBaselines(SnapshotHandle? snapshot, bool keep = false)
    {
        var keepChoice = keep && _baselineChosen;
        var kept = Baseline?.Snapshot;
        _choosingBaseline = true;
        try
        {
            BaselineChoices.Clear();
            BaselineChoices.Add(new BaselineChoice("None", null));
            if (snapshot is null)
            {
                Baseline = BaselineChoices[0];
                return;
            }

            var others = _session.Snapshots.Where(s => s != snapshot && !s.IsClosed)
                .OrderByDescending(s => s.Descriptor.CapturedAtUtc ?? DateTimeOffset.MinValue)
                .ToList();
            foreach (var other in others)
            {
                BaselineChoices.Add(new BaselineChoice(
                    string.Create(CultureInfo.CurrentCulture, $"{Path.GetFileName(other.Path)}{(other.Descriptor.CapturedAtUtc is { } at ? $" ({at.ToLocalTime():g})" : string.Empty)}"),
                    other));
            }

            var same = keepChoice ? BaselineChoices.FirstOrDefault(c => c.Snapshot == kept) : null;
            _baselineChosen = same is not null;
            Baseline = same
                ?? BaselineChoices.Skip(1).FirstOrDefault(c => IsEarlierOfSameProcess(c.Snapshot!.Descriptor, snapshot.Descriptor))
                ?? BaselineChoices[0];
        }
        finally
        {
            _choosingBaseline = false;
        }
    }

    /// <summary>
    /// Rebuilds the baseline choices after snapshots opened or closed: a baseline the person chose stays
    /// chosen while it is open; otherwise the default is chosen again (an earlier capture of the process may have opened),
    /// and the growth detectors run again when it changed.
    /// </summary>
    public void RefreshBaselines()
    {
        if (Snapshot is not { } snapshot)
        {
            return;
        }

        var before = Baseline?.Snapshot;
        ChooseBaselines(snapshot, keep: true);
        if (Baseline?.Snapshot != before)
        {
            Rerun(Baseline, "changed as snapshots opened or closed");
        }
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is an earlier capture of the process of <paramref name="mine"/>: the same
    /// process id when both dumps record one, else the same process name (dumps record no start time, and
    /// an earlier capture time rules out most reused ids). A time that is only a file's time (a copied dump) orders
    /// nothing, as on the Compare tab, so no default is chosen then.
    /// </summary>
    internal static bool IsEarlierOfSameProcess(SnapshotDescriptor candidate, SnapshotDescriptor mine) =>
        !candidate.CaptureTimeFromFile && !mine.CaptureTimeFromFile
        && candidate.CapturedAtUtc is { } then && mine.CapturedAtUtc is { } now && then < now
        && (candidate.ProcessId is { } id && mine.ProcessId is { } myId
            ? id == myId && (candidate.ProcessName is null || mine.ProcessName is null || candidate.ProcessName == mine.ProcessName)
            : candidate.ProcessName is not null && candidate.ProcessName == mine.ProcessName);

    partial void OnBaselineChanged(BaselineChoice? value)
    {
        if (_choosingBaseline)
        {
            return;
        }

        _baselineChosen = true;
        Rerun(value, "chosen");
    }

    // Another baseline runs the growth detectors again (only those use it).
    private void Rerun(BaselineChoice? value, string how)
    {
        if (!_started || Snapshot is not { } snapshot)
        {
            return;
        }

        _logger.LogInformation("Inspections: baseline {Baseline} {How} for {Snapshot}", value?.Snapshot?.Path ?? "none", how, snapshot.Path);

        // While the first run goes on, it reruns the stale growth tiles when it ends: it is not cancelled.
        if (!_runAll.IsCompleted || _running is not { } running)
        {
            return;
        }

        // Not shown (another tab, or a hidden workspace): the stale tiles run when the tab is shown again (ADR-044).
        if (!IsActive)
        {
            _rerun?.Cancel();
            _logger.LogInformation("Inspections: the growth tiles of {Snapshot} run when shown ({Stale} stale)", snapshot.Path, StaleTiles().Count);
            return;
        }

        // A rerun has its own token, linked to the snapshot's: another baseline cancels only the rerun.
        _rerun?.Cancel();
        _rerun?.Dispose();
        _rerun = CancellationTokenSource.CreateLinkedTokenSource(running.Token);
        RunTask = RerunAsync(snapshot, StaleTiles(), _rerun.Token);
    }

    /// <summary>The growth tiles whose last run used another baseline than the chosen one.</summary>
    private List<InspectionTileViewModel> StaleTiles() =>
        [.. Tiles.Where(t => t.Info.UsesBaseline && t.IsAvailable && (t.Run is null || !ReferenceEquals(t.RanWith, Baseline?.Snapshot)))];

    private async Task RerunAsync(SnapshotHandle snapshot, List<InspectionTileViewModel> tiles, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var tile in tiles)
            {
                var previous = tile.Run;
                try
                {
                    await RunOneAsync(snapshot, tile, cancellationToken);
                }
                finally
                {
                    // The worker keeps a replaced result set and its lease until released.
                    if (!ReferenceEquals(previous, tile.Run))
                    {
                        Release(previous);
                    }
                }
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Another baseline or snapshot replaced this run.
        }
        catch (Exception ex) when (ex is WorkerException)
        {
            _logger.LogInformation("{Section} shown as: {Message}", "Inspections against a baseline", ex.Message);
            Status = ex.Message;
            CanTryAgain = true;
        }
        catch (Exception ex)
        {
            // Not awaited by anyone: logged and shown, never lost.
            _logger.LogError(ex, "Running the growth detectors again failed");
            Status = "The growth detectors could not be run again. Details are in the log; press Try again.";
            CanTryAgain = true;
        }
    }
}
