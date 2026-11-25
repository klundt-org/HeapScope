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
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HeapScope.UI.Controls;
using HeapScope.UI.Views;
using HeapScope.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.Smoke;

/// <summary>
/// Captures the pages of the app as PNG files at twice the size, one theme per run, for the design review and the README
/// (<c>--headless --screenshots folder --smoke-open dump</c>, optionally <c>--smoke-compare</c>, <c>--smoke-allocations</c>
/// and <c>--screenshot-live-pid</c>). Fixture data only: everything a page shows goes into a picture. The Processes page's rows are
/// made up, so this machine's real processes and command lines never appear.
/// </summary>
internal sealed class ScreenshotRunner(IServiceProvider services, Window window, string folder)
{
    private const int LiveSeconds = 45;

    private const string DiagramType = "System.Threading.TimerCallback";

    private readonly ILogger _logger = services.GetRequiredService<ILogger<ScreenshotRunner>>();

    /// <summary>Waits until the UI thread has nothing left to do, then a little longer for queued rows and charts.</summary>
    private static async Task SettleAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle).GetTask();
        await Task.Delay(400);
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle).GetTask();
    }

    /// <summary>
    /// Saves the window as it stands, named by the run's theme (<c>--Ui:Theme Light</c> or <c>Dark</c>). One theme per run: the
    /// charts resolve their colors when they first draw, so a theme switched mid-run would leave them in the old colors.
    /// </summary>
    private async Task CaptureAsync(string name, TopLevel? of = null)
    {
        await SettleAsync();
        var suffix = Avalonia.Application.Current!.ActualThemeVariant == ThemeVariant.Dark ? "dark" : "light";
        var path = Path.Combine(folder, $"{name}-{suffix}.png");
        using var frame = (of ?? window).CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame was rendered.");
        frame.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        _logger.LogInformation("Screenshot written: {Path}", path);
    }

    // Sprint 4.1 task 1: the popups DEVELOPER.md 5 checked by hand. A headless window draws its popups into its own frame, so
    // the picture shows the popup over the page; a popup that does not open fails the run.

    /// <summary>Opens the text of the first info icon shown that <paramref name="which"/> picks, captures it, and closes it.</summary>
    private async Task CaptureInfoAsync(string name, Func<InfoIcon, bool> which)
    {
        await SettleAsync();
        var icon = window.GetVisualDescendants().OfType<InfoIcon>().FirstOrDefault(i => i.IsEffectivelyVisible && i.Bounds.Width > 0 && which(i))
            ?? throw new InvalidOperationException($"No info icon for {name} is on the page shown.");
        var flyout = icon.Flyout ?? throw new InvalidOperationException($"The info icon for {name} has no text to open.");
        flyout.ShowAt(icon);
        await SettleAsync();
        if (!flyout.IsOpen)
        {
            throw new InvalidOperationException($"The info icon's text for {name} did not open.");
        }

        await CaptureAsync(name);
        flyout.Hide();
    }

    /// <summary>
    /// Points at the widest bar of the chart shown that <paramref name="which"/> picks, opens its hover text, captures it,
    /// and moves off. A chart without such a bar is logged and skipped (not every dump has a "+N smaller" bar).
    /// </summary>
    private async Task CaptureChartTipAsync(string name, Func<IcicleBar, bool> which)
    {
        await SettleAsync();
        var chart = window.GetVisualDescendants().OfType<IcicleChart>().FirstOrDefault(static c => c.IsEffectivelyVisible && c.Bounds.Width > 0)
            ?? throw new InvalidOperationException($"No Retainers chart is shown for {name}.");
        if (chart.Bars.Where(which).OrderBy(static b => b.Y).ThenByDescending(static b => b.Width).FirstOrDefault() is not { } bar)
        {
            _logger.LogInformation("Screenshot skipped: {Name}, the chart has no such bar", name);
            return;
        }

        var point = chart.TranslatePoint(new Point(bar.X + (bar.Width / 2), bar.Y + (bar.Height / 2)), window)
            ?? throw new InvalidOperationException($"The bar for {name} is not in the window.");
        window.MouseMove(point);
        await SettleAsync();
        if (ToolTip.GetTip(chart) is null)
        {
            throw new InvalidOperationException($"Pointing at the bar for {name} gave it no hover text.");
        }

        ToolTip.SetIsOpen(chart, true);
        await CaptureAsync(name);
        ToolTip.SetIsOpen(chart, false);
        window.MouseMove(new Point(0, 0));
    }

    /// <summary>Shows a question window, captures it and answers it Cancel; the answer is checked to be no.</summary>
    private async Task CaptureQuestionAsync(string name, Func<Task<bool>> ask)
    {
        MessageWindow? asked = null;
        void Seen(MessageWindow question) => asked = question;
        MessageWindow.Showing += Seen;
        try
        {
            var command = ask();
            for (var waited = 0; asked is not { IsVisible: true } && waited < 300; waited++)
            {
                await Task.Delay(100);
            }

            if (asked is not { IsVisible: true } question)
            {
                throw new InvalidOperationException($"The question for {name} was not shown.");
            }

            question.SetRenderScaling(2);
            await CaptureAsync(name, question);
            question.Close(false);
            if (await command)
            {
                throw new InvalidOperationException($"The question for {name} was answered yes.");
            }
        }
        finally
        {
            MessageWindow.Showing -= Seen;
        }
    }

    /// <summary>Drives the pages and returns the exit code.</summary>
    public async Task<int> RunAsync(UiOptions options)
    {
        Directory.CreateDirectory(folder);
        window.Width = 1440;
        window.Height = 900;
        window.SetRenderScaling(2);
        var shell = services.GetRequiredService<ShellViewModel>();
        var snapshots = services.GetRequiredService<SnapshotsViewModel>();
        void Show(object page) => shell.SelectedItem = shell.NavigationItems.Single(i => i.Page == page);

        await SettleAsync();
        await CaptureAsync("01-home");

        var processes = services.GetRequiredService<ProcessesViewModel>();
        Show(processes);
        {
            // The page refreshes itself when shown: its real rows must be in before these replace them.
            await SettleAsync();
            await Task.Delay(500);
            for (var waited = 0; processes.IsBusy && waited < 300; waited++)
            {
                await Task.Delay(100);
            }

            if (processes.IsBusy)
            {
                throw new InvalidOperationException("The Processes page's refresh did not finish, so its real rows could reach the picture.");
            }

            processes.Processes.Clear();
            processes.Processes.Add(new ProcessRow(18244, "LeakyApi", ".NET 10.0.14", "No", 412_000_000, "LeakyApi.exe --urls http://localhost:5100", false));
            processes.Processes.Add(new ProcessRow(20112, "OrdersWorker", ".NET 8.0.21", "No", 1_260_000_000, "dotnet OrdersWorker.dll", false));
            processes.Processes.Add(new ProcessRow(9320, "ReportingService", ".NET 9.0.10", "Yes", 788_000_000, "ReportingService.exe", true));
            await CaptureAsync("02-processes");
            // Sprint 4.1 task 1: an info icon's open text, by the title and beside a turned-off button (Capture dump, with no
            // process selected).
            await CaptureInfoAsync("02-processes-info", static icon => icon.Text == InfoTexts.Processes);
            await CaptureInfoAsync("02-processes-info-beside-off", static icon =>
                icon.Parent is Panel panel && panel.Children.Any(static c => c is Button button and not InfoIcon && !button.IsEffectivelyEnabled));
            if (processes.Processes.Count != 3 || processes.Processes.Any(static p => p.ProcessId is not (18244 or 20112 or 9320)))
            {
                File.Delete(Path.Combine(folder, $"02-processes-{(Avalonia.Application.Current!.ActualThemeVariant == ThemeVariant.Dark ? "dark" : "light")}.png"));
                throw new InvalidOperationException("Real processes reached the Processes page's picture, so it was deleted.");
            }
        }

        if (options.SmokeOpenPath is { } dump)
        {
            Show(snapshots);
            await SettleAsync();
            await snapshots.OpenPathAsync(dump);
            await snapshots.Types.QueryTask;
            await TouchAsync(snapshots.Types.Rows);
            await CaptureAsync("03-types");

            var navigator = snapshots.Navigator;
            // A type with a few retention paths, so the diagram's picture can be read.
            var shown = Enumerable.Range(0, Math.Min(snapshots.Types.Rows.Count, 50)).Select(snapshots.Types.Rows.GetRow)
                .FirstOrDefault(row => row?.Name?.StartsWith(DiagramType, StringComparison.Ordinal) == true) ?? snapshots.Types.Rows.GetRow(0);
            snapshots.Types.OpenRowCommand!.Execute(shown);
            var instances = (InstancesViewModel)navigator.Current!;
            await instances.QueryTask;
            await TouchAsync(instances.Rows);
            await CaptureAsync("04-instances");

            instances.OpenRowCommand!.Execute(instances.Rows.GetRow(0));
            var item = (ObjectViewModel)navigator.Current!;
            await item.LoadTask;
            await item.ReferenceRows.WhenLoadedAsync();
            await CaptureAsync("05-object");
            item.ReferencesView = ObjectReferenceTabs.Retention;
            await item.RetentionTask;
            await item.RetainedTask;
            await CaptureAsync("06-object-retention");

            navigator.BackCommand.Execute(null);
            instances.WhyAliveCommand!.Execute(null);
            var paths = (TypePathsViewModel)navigator.Current!;
            await paths.LoadTask;
            await CaptureAsync("07-why-alive");
            paths.ShowDiagram = true;
            paths.FitDiagram = true;
            await CaptureAsync("08-why-alive-diagram");
            paths.ShowDiagram = false;
            navigator.BackCommand.Execute(null);

            snapshots.Tab = SnapshotTabs.Inspections;
            await snapshots.Inspections.RunTask;
            await TouchAsync(snapshots.Inspections.Rows);
            await CaptureAsync("09-inspections");

            snapshots.Tab = SnapshotTabs.Retainers;
            await SettleAsync();
            await snapshots.Retainers.QueryTask;
            await snapshots.Retainers.IcicleTask;
            await TouchAsync(snapshots.Retainers.Rows);
            await CaptureAsync("10-retainers");
            // Sprint 4.1 task 1: the chart's hover text on a bar, and on a "+N smaller" bar when the chart has one.
            await CaptureChartTipAsync("10-retainers-bar", static bar => bar.Count == 1 && bar.Y > 0);
            await CaptureChartTipAsync("10-retainers-smaller", static bar => bar.Count > 1);

            if (options.SmokeComparePath is { } other)
            {
                snapshots.Tab = SnapshotTabs.Compare;
                await SettleAsync();
                await snapshots.OpenPathAsync(other);
                await snapshots.Compare.QueryTask;
                await TouchAsync(snapshots.Compare.Rows);
                await CaptureAsync("11-compare");
            }
        }

        if (options.SmokeAllocationsPath is { } trace && shell.Allocations is { } allocations)
        {
            Show(allocations);
            await SettleAsync();
            await allocations.OpenPathAsync(trace);
            await SettleAsync();
            await CaptureAsync("12-allocations");
        }

        // Watches the running fixture itself: the picture shows what a real run looks like, not made-up samples.
        if (options.ScreenshotLivePid is { } pid && shell.Live is { } live)
        {
            Show(live);
            await SettleAsync();
            await live.RefreshCommand.ExecuteAsync(null);
            live.SelectedProcess = live.Processes.FirstOrDefault(p => p.ProcessId == pid)
                ?? throw new InvalidOperationException($"The process {pid} is not among the .NET processes the Live page lists.");
            live.WriteTimeline = false;
            // Under a data folder that was moved, the run records its allocations, and a picture shows them (sprint 4.3).
            var records = HeapScope.Core.HeapScopeFolders.Default.Source == HeapScope.Core.HeapScopeFolders.EnvironmentVariable;
            live.RecordRuns = records;
            live.RecordAllocations = records;
            await live.StartCommand.ExecuteAsync(null);
            await Task.Delay(LiveSeconds * 1000);
            await CaptureAsync("13-live");
            if (records)
            {
                live.Allocations.ShowStacks = true;
                await Task.Delay(2000);
                await CaptureAsync("20-live-allocations");
            }

            live.StopCommand.Execute(null);
            if (live.Run is { } run)
            {
                await run.Completion;
            }
        }

        Show(shell.JobsPage);
        await CaptureAsync("14-jobs");
        Show(shell.NavigationItems.Single(i => i.Title == "About").Page);
        await CaptureAsync("15-about");

        // Sprint 4.1 task 1: Settings, and Purge all's question. Never through its command: a smoke run answers every question
        // yes (AutomaticUserInteraction), so the command would delete. The question is shown in the real question window and
        // answered Cancel here.
        if (shell.NavigationItems.SingleOrDefault(i => i.Title == "Settings")?.Page is SettingsViewModel settings)
        {
            Show(settings);
            await CaptureAsync("18-settings");
            if (await settings.PurgeAllQuestionAsync() is { } question)
            {
                await CaptureQuestionAsync("19-purge-all-question", () => MessageWindow.ShowAsync(
                    window, SettingsViewModel.PurgeAllTitle, question, MessageButtons.YesNo, (SettingsViewModel.PurgeAllConfirm, SettingsViewModel.PurgeAllCancel)));
            }
            else
            {
                _logger.LogInformation("Screenshot skipped: 19-purge-all-question, Purge all is refused now ({Status})", settings.Status);
            }
        }

        // Sprint 3.13: the first lesson started on the real sample. The page it opens, with the lesson's steps in the panel beside
        // it, then the Learn page with its question (review M15: a lesson that does not start fails the run).
        if (services.GetService<LearnViewModel>() is { } learn)
        {
            Show(learn);
            learn.Selected = learn.Lessons[0];
            await learn.StartLessonCommand.ExecuteAsync(null);
            if (learn.Current is null)
            {
                throw new InvalidOperationException($"The lesson did not start: {learn.Status}");
            }

            await SettleAsync();
            await CaptureAsync("16-lesson-panel");
            Show(learn);
            await SettleAsync();
            await CaptureAsync("17-learn");
            learn.StopLessonCommand.Execute(null);
        }

        return 0;
    }

    private static async Task TouchAsync<TRow>(VirtualRowList<TRow> rows)
        where TRow : class, IIndexedRow
    {
        for (var i = 0; i < Math.Min(rows.Count, 50); i++)
        {
            rows.GetRow(i);
        }

        await rows.WhenLoadedAsync();
    }
}
