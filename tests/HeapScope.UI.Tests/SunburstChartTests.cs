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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HeapScope.Contracts;
using HeapScope.UI.Controls;
using HeapScope.UI.ViewModels;

namespace HeapScope.UI.Tests;

/// <summary>The sunburst chart (ADR-046): it draws headless, hovering names an arc, a click selects one, a double-click focuses it.</summary>
public sealed class SunburstChartTests(UiSession ui)
{
    private static readonly IcicleSegmentDto[] s_segments =
    [
        new(0, 0, 1000, string.Empty, "All reachable objects", 0),
        new(1, 0, 750, "o:10", "App.Cache", 0x10),
        new(1, 750, 250, "o:20", "App.Other", 0x20),
        new(2, 0, 500, "o:30", "App.Entry", 0x30),
    ];

    /// <summary>Shows the chart 400 by 300 pixels: its center at (200, 150), rings of 50 pixels.</summary>
    private static (Window Window, SunburstChart Chart) Show(SunburstChart chart)
    {
        var window = new Window { Width = 400, Height = 340, Content = new StackPanel { Children = { chart } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame().ShouldNotBeNull();
        return (window, chart);
    }

    private static Point At(Window window, SunburstChart chart, double x, double y) =>
        chart.TranslatePoint(new Point(chart.Center.X + x, chart.Center.Y + y), window)!.Value;

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    [Fact]
    public async Task It_draws_every_level_as_a_ring_around_the_focus()
    {
        await ui.RunAsync(() =>
        {
            var (window, chart) = Show(new SunburstChart { Segments = s_segments, Total = 1000 });
            try
            {
                chart.Bounds.Height.ShouldBe(300, "as tall as its diameter");
                chart.Arcs.Select(static a => a.Segment.Key).ShouldBe([string.Empty, "o:10", "o:20", "o:30"]);
                chart.Arcs[0].IsCenter.ShouldBeTrue();
                chart.Arcs.Max(static a => a.Outer).ShouldBe(150);

                chart.Segments = [];
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame().ShouldNotBeNull();
                chart.Arcs.ShouldBeEmpty();
                chart.Bounds.Height.ShouldBe(0, "an empty chart takes no room");
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Hovering_an_arc_says_what_it_is_in_the_charts_terms()
    {
        await ui.RunAsync(() =>
        {
            var (window, chart) = Show(new SunburstChart { Segments = s_segments, Total = 1000 });
            try
            {
                window.MouseMove(At(window, chart, -75, -5));
                Dispatcher.UIThread.RunJobs();
                ToolTip.GetTip(chart).ShouldBeOfType<string>().ShouldStartWith("App.Other\nKeeps ");

                chart.Subject = ChartSubject.Allocations;
                window.MouseMove(At(window, chart, 120, 10));
                Dispatcher.UIThread.RunJobs();
                ToolTip.GetTip(chart).ShouldBeOfType<string>().ShouldStartWith("App.Entry\nAbout ");

                // The second ring past the entry is the cache's own bytes: no arc.
                window.MouseMove(At(window, chart, -120, 20));
                Dispatcher.UIThread.RunJobs();
                ToolTip.GetTip(chart).ShouldBeNull();
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_click_selects_an_arc_and_a_double_click_focuses_it()
    {
        await ui.RunAsync(() =>
        {
            var focused = new List<IcicleSegmentDto?>();
            var (window, chart) = Show(new SunburstChart { Segments = s_segments, Total = 1000, FocusCommand = new RelayCommand<IcicleSegmentDto?>(focused.Add) });
            try
            {
                Click(window, At(window, chart, 120, 10));
                Dispatcher.UIThread.RunJobs();
                chart.SelectedSegment.ShouldBe(s_segments[3]);
                focused.ShouldBeEmpty();
                window.CaptureRenderedFrame().ShouldNotBeNull("the path to the selection is drawn");

                var other = At(window, chart, -75, -5);
                Click(window, other);
                Click(window, other);
                Dispatcher.UIThread.RunJobs();
                chart.SelectedSegment.ShouldBe(s_segments[2]);
                focused.ShouldBe([s_segments[2]]);

                // In a corner, outside the rings, nothing changes.
                Click(window, At(window, chart, -198, 148));
                Dispatcher.UIThread.RunJobs();
                chart.SelectedSegment.ShouldBe(s_segments[2]);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }
}
