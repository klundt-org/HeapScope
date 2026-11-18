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

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HeapScope.UI.Controls;
using HeapScope.UI.ViewModels;
using HeapScope.UI.Views;
using HeapScope.UI.Views.Pages;
using HeapScope.UI.Views.Subjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.Services;

/// <summary>
/// Builds the heavy views once, unseen, while the app is idle after its first window (sprint 3.12, task 4): task 3's
/// recordings put most of a page's cold first show in styles and control templates built on first use, so this pays that
/// cost before the user opens the page. One view per step at the dispatcher's idle priority, so input waits at most one
/// step: first the Snapshots page in its place in the shell, kept for its first show, then the other pages and the views a
/// page shows only with data, measured and arranged in a hidden host and dropped. The status bar shows that it runs
/// (<see cref="ShellViewModel.IsPreparingViews"/>, the owner's requirement of 2026-11-14: a longer start must not look like
/// a frozen app). <c>Ui:Prewarm=false</c> turns it off, for A/B runs; screenshot runs skip it.
/// </summary>
public sealed class ViewPrewarmer
{
    /// <summary>The configuration key that turns the prewarm off ("false").</summary>
    public const string SettingName = "Ui:Prewarm";

    private static readonly Size s_size = new(1200, 800);

    private readonly ShellViewModel _shell;
    private readonly ILogger<ViewPrewarmer> _logger;
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<string> _built = [];

    public ViewPrewarmer(ShellViewModel shell, IConfiguration configuration, UiOptions options, ILogger<ViewPrewarmer> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        _shell = shell;
        _logger = logger;
        IsEnabled = !string.Equals(configuration[SettingName], "false", StringComparison.OrdinalIgnoreCase) && options.ScreenshotFolder is null;
    }

    /// <summary>False when <c>Ui:Prewarm=false</c> or in a screenshot run.</summary>
    public bool IsEnabled { get; }

    /// <summary>Completes when the prewarm has finished, failed or was skipped; never faults.</summary>
    public Task Completion => _done.Task;

    /// <summary>
    /// The views built alone without a data context and dropped: the subjects of the Snapshot tab and the tabs other than the
    /// first, which show only with data, and the other pages. Only the Snapshots page is built in place first
    /// (<see cref="LazyContentControl.Prebuild"/>), with its Snapshot tab and object view, which bind to their page and cannot be
    /// built alone. Every page built in place stays bound to its view model while hidden: with all of them, the hidden Jobs,
    /// Live and Processes pages followed the smoke run's jobs, and its Retainers and Allocations first shows were 20 to 45 ms
    /// slower (A/B smoke runs of 2026-11-14).
    /// </summary>
    internal static IReadOnlyList<(string Name, Func<Control> Create)> Views { get; } =
    [
        (nameof(TypesView), static () => new TypesView()),
        (nameof(InstancesView), static () => new InstancesView()),
        (nameof(RetainersTab), static () => new RetainersTab()),
        (nameof(InspectionsTab), static () => new InspectionsTab()),
        (nameof(CompareTab), static () => new CompareTab()),
        (nameof(SetView), static () => new SetView()),
        (nameof(TypePathsView), static () => new TypePathsView()),
        (nameof(TypeReferrersView), static () => new TypeReferrersView()),
        (nameof(AllocationsPage), static () => new AllocationsPage()),
        (nameof(ProcessesPage), static () => new ProcessesPage()),
        (nameof(LivePage), static () => new LivePage()),
        (nameof(JobsPage), static () => new JobsPage()),
        (nameof(SettingsPage), static () => new SettingsPage()),
        (nameof(LearnPage), static () => new LearnPage()),
        (nameof(AboutPage), static () => new AboutPage()),
    ];

    /// <summary>The views a run builds alone: <see cref="Views"/>, or a test's own (sprint 3.12 review M12: a step that fails).</summary>
    internal IReadOnlyList<(string Name, Func<Control> Create)> StandaloneViews { get; init; } = Views;

    /// <summary>The views built by the last run, in order (for its log line and tests).</summary>
    public IReadOnlyList<string> Built => _built;

    /// <summary>
    /// Builds <paramref name="window"/>'s Snapshots page in place unless it is shown already, then <see cref="Views"/> in its
    /// prewarm host, a visible panel that shows nothing; one view per step, each step's time logged.
    /// </summary>
    public async Task RunAsync(MainWindow window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!IsEnabled)
        {
            _logger.LogInformation("View prewarm skipped ({Setting} is false, or a screenshot run)", SettingName);
            _done.TrySetResult();
            return;
        }

        var elapsed = Stopwatch.StartNew();
        var work = TimeSpan.Zero;
        try
        {
            _shell.IsPreparingViews = true;
            var pages = await Dispatcher.UIThread.InvokeAsync(
                () => window.GetVisualDescendants().OfType<LazyContentControl>().Where(p => p.Page is SnapshotsViewModel).ToList(), DispatcherPriority.ApplicationIdle);
            var steps = pages.Select(page => (Func<TimeSpan?>)(() => Prebuild(page)))
                .Concat(StandaloneViews.Select(view => (Func<TimeSpan?>)(() => Build(window.PrewarmHost, view.Name, view.Create))));
            foreach (var step in steps)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (await Dispatcher.UIThread.InvokeAsync(step, DispatcherPriority.ApplicationIdle) is { } time)
                {
                    work += time;
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                // The views built alone are garbage now. Left to the gen 0 budget, they were collected inside a later first
                // show (the Allocations page's longest operation 71 to 118 ms against 54 to 66 ms with this collect, A/B smoke runs
                // of 2026-11-14); collected here, at idle, it costs about 8 ms.
                work += await Dispatcher.UIThread.InvokeAsync(CollectGarbage, DispatcherPriority.ApplicationIdle, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            // Outside a step (finding the pages, the collection): the prewarm is never the app's problem (the review's M12).
            _logger.LogWarning(ex, "View prewarm stopped by an error after {Built} views; the others are built when first shown", _built.Count);
        }
        finally
        {
            _shell.IsPreparingViews = false;
            _logger.LogInformation(
                "View prewarm {Outcome}: {Built} views in {WorkMs:N0} ms of UI work over {ElapsedMs:N0} ms ({Views})",
                cancellationToken.IsCancellationRequested ? "stopped" : "done",
                _built.Count,
                work.TotalMilliseconds,
                elapsed.Elapsed.TotalMilliseconds,
                string.Join(", ", _built));
            _done.TrySetResult();
        }
    }

    private TimeSpan CollectGarbage()
    {
        var watch = Stopwatch.StartNew();
        GC.Collect(1, GCCollectionMode.Forced, blocking: true);
        _logger.LogInformation("Prewarm garbage collected (gen 1) in {Ms:N1} ms", watch.Elapsed.TotalMilliseconds);
        return watch.Elapsed;
    }

    // A page not shown yet, built where it is kept; the page shown now (or one shown meanwhile) is skipped.
    private TimeSpan? Prebuild(LazyContentControl page)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            if (page.Prebuild(s_size) is not { } view)
            {
                return null;
            }

            var name = view.GetType().Name;
            _built.Add(name);
            _logger.LogInformation("Prewarmed {View} in place in {Ms:N1} ms", name, watch.Elapsed.TotalMilliseconds);
            return watch.Elapsed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Prewarming the page of {Page} failed after {Ms:N1} ms; it is built when first shown", page.Page?.GetType().Name, watch.Elapsed.TotalMilliseconds);
            return null;
        }
    }

    private TimeSpan? Build(Panel host, string name, Func<Control> create)
    {
        var watch = Stopwatch.StartNew();
        Control? view = null;
        try
        {
            view = create();
            view.Width = s_size.Width;
            view.Height = s_size.Height;
            host.Children.Add(view);
            view.Measure(s_size);
            view.Arrange(new Rect(s_size));
            _built.Add(name);
            _logger.LogInformation("Prewarmed {View} in {Ms:N1} ms", name, watch.Elapsed.TotalMilliseconds);
            return watch.Elapsed;
        }
        catch (Exception ex)
        {
            // Never the app's problem: the page is built again when it is first shown, where a real failure surfaces.
            _logger.LogWarning(ex, "Prewarming {View} failed after {Ms:N1} ms; it is built when first shown", name, watch.Elapsed.TotalMilliseconds);
            return null;
        }
        finally
        {
            if (view is not null)
            {
                host.Children.Remove(view);
            }
        }
    }
}
