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
using HeapScope.Application.Monitoring;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// What the Live page shows of one run, the one being watched or a past one (sprint 4.3): the chart's samples and markers, the
/// snapshot cards, and what is said about it. Live monitoring, the past runs and the run's files write it, on the UI thread.
/// </summary>
public sealed partial class LiveRunState : ObservableObject
{
    /// <summary>Samples the chart keeps (30 minutes at 1 s).</summary>
    public const int WindowSamples = 1800;

    private readonly List<HeapSample> _samples = [];
    private readonly List<DateTimeOffset> _markers = [];
    private readonly List<DateTimeOffset> _gcMarkers = [];

    [ObservableProperty]
    private string? _status;

    /// <summary>
    /// What the user must not miss about the run: why it was refused, or that no samples are arriving. Shown as a banner: a refused
    /// run lasts milliseconds, and its Start and Stop buttons only flash (3.10, the owner's own test).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private string? _runWarning;

    /// <summary>What the chart shows when it is a past run's (sprint 3.4), read only.</summary>
    [ObservableProperty]
    private string? _pastRunShown;

    /// <summary>Bumped once per change: the chart redraws when it changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSamples))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private int _chartVersion;

    /// <summary>When the run began, so the time axis reads the run's age once the window has slid past its start.</summary>
    [ObservableProperty]
    private DateTimeOffset? _chartStart;

    /// <summary>The run's snapshots, newest first.</summary>
    public ObservableCollection<SnapshotCard> Snapshots { get; } = [];

    /// <summary>The chart's samples, oldest first (read on the UI thread).</summary>
    public IReadOnlyList<HeapSample> Samples => _samples;

    /// <summary>When rules took snapshots, for the chart's markers.</summary>
    public IReadOnlyList<DateTimeOffset> Markers => _markers;

    /// <summary>When the run forced a collection (sprint 3.4).</summary>
    public IReadOnlyList<DateTimeOffset> GcMarkers => _gcMarkers;

    /// <summary>Whether the chart has anything to show (raised with <see cref="ChartVersion"/>: the list itself does not notify).</summary>
    public bool HasSamples => _samples.Count > 0;

    /// <summary>Whether the empty chart has something useful to say: a warning says it better.</summary>
    public bool ShowEmptyState => !HasSamples && RunWarning is null;

    /// <summary>Empties the chart and the cards, for a run that starts.</summary>
    public void Clear() => Show([], [], [], [], start: null, pastRun: null);

    /// <summary>Shows a whole run (a past one) in place of what is shown.</summary>
    public void Show(
        IReadOnlyList<HeapSample> samples,
        IEnumerable<DateTimeOffset> markers,
        IEnumerable<DateTimeOffset> gcMarkers,
        IReadOnlyList<SnapshotCard> snapshots,
        DateTimeOffset? start,
        string? pastRun)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(snapshots);
        _samples.Clear();
        _samples.AddRange(samples.TakeLast(WindowSamples));
        _markers.Clear();
        _markers.AddRange(markers);
        _gcMarkers.Clear();
        _gcMarkers.AddRange(gcMarkers);
        Snapshots.Clear();
        foreach (var snapshot in snapshots)
        {
            Snapshots.Add(snapshot);
        }

        ChartStart = start;
        PastRunShown = pastRun;
        ChartVersion++;
    }

    /// <summary>A sample of the run being watched.</summary>
    public void Add(HeapSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ChartStart ??= sample.At;
        _samples.Add(sample);
        if (_samples.Count > WindowSamples)
        {
            _samples.RemoveRange(0, _samples.Count - WindowSamples);
        }

        ChartVersion++;
    }

    /// <summary>A snapshot of the run being watched: a marker, and its card on top.</summary>
    public void Add(SnapshotCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        _markers.Add(card.At);
        Snapshots.Insert(0, card);
        ChartVersion++;
    }

    /// <summary>A collection forced in the process being watched.</summary>
    public void AddForcedGc(DateTimeOffset at)
    {
        _gcMarkers.Add(at);
        ChartVersion++;
    }
}
