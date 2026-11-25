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

using HeapScope.Application.Monitoring;
using HeapScope.UI.ViewModels;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>What the Live page shows of a run (sprint 4.3): one state that live monitoring and the past runs write.</summary>
public sealed class LiveRunStateTests
{
    private static readonly DateTimeOffset s_at = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly LiveRunState _state = new();

    [Fact]
    public void Samples_keep_the_last_window_and_the_run_s_start()
    {
        for (var i = 0; i < LiveRunState.WindowSamples + 5; i++)
        {
            _state.Add(Sample(i));
        }

        _state.Samples.Count.ShouldBe(LiveRunState.WindowSamples);
        _state.Samples[0].At.ShouldBe(s_at.AddSeconds(5));
        _state.ChartStart.ShouldBe(s_at, "the axis reads the run's age once the window has slid");
        _state.ChartVersion.ShouldBe(LiveRunState.WindowSamples + 5, "a redraw per sample");
    }

    [Fact]
    public void A_shown_run_replaces_everything_with_one_redraw_and_clearing_empties_it()
    {
        _state.Add(Sample(0));
        _state.Add(new SnapshotCard(s_at, "manual", "a.dmp", 1, null));
        _state.AddForcedGc(s_at);
        var version = _state.ChartVersion;

        _state.Show([.. Enumerable.Range(0, LiveRunState.WindowSamples + 1).Select(Sample)], [s_at], [], [new SnapshotCard(s_at, "above 1 MB", "b.dmp", 2, null)], s_at, "A past run");

        (_state.Samples.Count, _state.Markers.Count, _state.GcMarkers.Count).ShouldBe((LiveRunState.WindowSamples, 1, 0));
        _state.Snapshots.ShouldHaveSingleItem().Path.ShouldBe("b.dmp");
        (_state.PastRunShown, _state.ChartStart, _state.ChartVersion).ShouldBe(("A past run", s_at, version + 1));

        _state.Clear();
        (_state.HasSamples, _state.ShowEmptyState, _state.PastRunShown, _state.ChartStart).ShouldBe((false, true, null, null));
        (_state.Markers.Count, _state.Snapshots.Count).ShouldBe((0, 0));
    }

    [Fact]
    public void The_newest_snapshot_is_on_top_and_each_has_a_marker()
    {
        _state.Add(new SnapshotCard(s_at, "first", "a.dmp", 1, null));
        _state.Add(new SnapshotCard(s_at.AddSeconds(1), "second", null, 0, "no"));

        _state.Snapshots.Select(s => s.Rule).ShouldBe(["second", "first"]);
        _state.Markers.ShouldBe([s_at, s_at.AddSeconds(1)]);
    }

    private static HeapSample Sample(int second) => new(s_at.AddSeconds(second), 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
