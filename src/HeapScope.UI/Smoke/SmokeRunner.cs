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


using HeapScope.Contracts;
using Avalonia.Threading;
using HeapScope.UI.Services;
using HeapScope.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.Smoke;

/// <summary>
/// The unattended smoke runs (`--smoke-synthetic`, `--smoke-open`, `--smoke-live`, ARCHITECTURE.md 8): each drives the
/// real window through its view models, checks the dispatcher budget and returns the process exit code (0 ok,
/// <see cref="SmokeLagExceeded"/>, <see cref="SmokeJobFailed"/>).
/// </summary>
internal sealed class SmokeRunner(IServiceProvider services)
{
    /// <summary>Smoke mode exit code: the dispatcher was blocked longer than <see cref="SmokeLagLimit"/>.</summary>
    public const int SmokeLagExceeded = 3;

    /// <summary>Smoke mode exit code: the run did not complete.</summary>
    public const int SmokeJobFailed = 4;

    /// <summary>
    /// Regression gate of the smoke run: twice the 100 ms budget. Breaches of the budget itself are logged by
    /// <see cref="DispatcherWatchdog"/>; probe lag also holds the system's scheduling noise, hence twice the budget.
    /// </summary>
    public static readonly TimeSpan SmokeLagLimit = DispatcherWatchdog.Budget * 2;

    private readonly IServiceProvider _services = services;

    /// <summary>Smoke mode exit code: the main window took longer than <see cref="FirstWindowLimit"/> to show.</summary>
    public const int SmokeColdStartExceeded = 5;

    /// <summary>
    /// Gross regression check of the cold start: process start to the main window's first frame.
    /// Measured 2.2 s warm for a Release build on the development machine (WPF the same), and 8 to 15 s for the first launch
    /// after a build while its files are scanned, so the limit only catches a start that hangs. Not checked headless.
    /// </summary>
    public static readonly TimeSpan FirstWindowLimit = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Smoke mode exit code: a grid walked a whole row list (<see cref="Controls.VirtualCollectionViews.Enumerations"/>),
    /// which makes a row per index.
    /// </summary>
    public const int SmokeGridEnumerated = 6;

    /// <summary>Smoke mode exit code: more UI operations over 100 ms than <see cref="SlowOperationCeiling"/>.</summary>
    public const int SmokeSlowOperations = 7;

    /// <summary>
    /// The ceiling on slow first shows: UI operations over the 100 ms budget in a whole open run, cold
    /// first shows included. Measured on 2026-11-13 with the published build on the demo dumps: 0 to 4 on the development
    /// machine, 5 to 10 with four more cores busy (budgets.md); the ceiling sits above that, so a regression that adds slow
    /// first shows fails the run, and the ordinary spread does not. Not checked headless.
    /// </summary>
    public const int SlowOperationCeiling = 12;

    /// <summary>The exit code of the smoke run <paramref name="options"/> ask for, or null when they ask for none.</summary>
    public async Task<int?> RunAsync(UiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.IsSmokeRun)
        {
            await WaitForPrewarmAsync();
        }

        int? result = options switch
        {
            { SmokeSyntheticObjects: int objects } => await RunSmokeAsync(objects),
            { SmokeOpenPath: { } dump } => await RunSmokeOpenAsync(dump),
            { SmokeLiveSamples: int samples } => await RunSmokeLiveAsync(samples),
            { SmokeLiveProcess: int pid } => await RunSmokeLiveProcessAsync(pid),
            _ => null,
        };
        if (result is null)
        {
            return null;
        }

        // Every smoke run reports the UI's memory and proves on real pages and dumps that no grid walked its whole list.
        var enumerations = Controls.VirtualCollectionViews.Enumerations;
        using (var process = System.Diagnostics.Process.GetCurrentProcess())
        {
            _services.GetRequiredService<ILogger<SmokeRunner>>().LogInformation(
                "Smoke memory: UI private {PrivateMb:N0} MB, managed heap {ManagedMb:N0} MB; whole-list grid enumerations {Enumerations}",
                process.PrivateMemorySize64 / (1024.0 * 1024),
                GC.GetTotalMemory(forceFullCollection: false) / (1024.0 * 1024),
                enumerations);
        }

        return SmokeVerdict.Final(result.Value, enumerations, Program.IsHeadless, App.FirstWindowTime);
    }

    /// <summary>
    /// Waits for the background build of the pages, so the run measures what a user meets after it,
    /// and logs its slow operations apart: no page is shown while it runs, so they are not first shows and not in the ceiling.
    /// </summary>
    private async Task WaitForPrewarmAsync()
    {
        var logger = _services.GetRequiredService<ILogger<SmokeRunner>>();
        var watchdog = _services.GetRequiredService<DispatcherWatchdog>();
        var prewarm = _services.GetRequiredService<ViewPrewarmer>();
        var phases = new SmokePhases(watchdog);
        phases.Begin();
        try
        {
            await prewarm.Completion.WaitAsync(TimeSpan.FromMinutes(2));
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Smoke prewarm: not done after 2 minutes; the run goes on");
        }

        await IdleAsync();
        var phase = phases.End("prewarm", cold: true);
        logger.LogInformation("Smoke prewarm ({State}): {Measured}; slow operations {Slow}", prewarm.IsEnabled ? "on" : "off", phase.Measured, phase.SlowOperations);
    }

    // Waits until the UI thread has nothing left to do, rendering included.
    private static Task IdleAsync() => Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle).GetTask();

    /// <summary>Runs one synthetic job, checks the dispatcher lag budget and closes the app with a result code.</summary>
    private async Task<int> RunSmokeAsync(int objects)
    {
        var services = _services;
        var logger = services.GetRequiredService<ILogger<SmokeRunner>>();
        var watchdog = services.GetRequiredService<DispatcherWatchdog>();
        var home = services.GetRequiredService<HomeViewModel>();

        // Measure steady state, not one-time cold costs (first window render, first job row template, JIT):
        // run a tiny warm-up job, then wait until the dispatcher is idle. Cold-start lag is tracked separately
        // (SPRINTS.md backlog: UI cold start).
        home.ObjectCount = 10_000;
        await home.RunSyntheticCommand.ExecuteAsync(null);
        await IdleAsync();
        await Task.Delay(500);
        home.ObjectCount = objects;
        watchdog.Reset();
        await home.RunSyntheticCommand.ExecuteAsync(null);
        var completed = home.LastJob is { Result.IsCompletedSuccessfully: true };

        // Let queued progress updates drain so their cost is measured too.
        await Task.Delay(500);
        var lag = watchdog.MaxLag;
        var operation = watchdog.MaxOperationBound;
        logger.LogInformation(
            "Smoke run: job completed {Completed}, maximum dispatcher lag {LagMs:N0} ms, longest dispatcher operation at most {OperationMs:N0} ms",
            completed,
            lag.TotalMilliseconds,
            operation.TotalMilliseconds);

        // The budget's subject (a single dispatcher work item, its upper bound) is checked hard at
        // 100 ms; probe lag, which also contains OS scheduling noise, is checked at the 2x gate.
        return SmokeVerdict.Phase(completed, operation, lag);
    }

    /// <summary>
    /// Charts <paramref name="count"/> made-up samples on the Live page at 200 a second: the page's first
    /// show is logged as a cold cost; the feed, every sample a redraw of up to 30 minutes of history, is checked.
    /// </summary>
    private async Task<int> RunSmokeLiveAsync(int count)
    {
        var services = _services;
        var logger = services.GetRequiredService<ILogger<SmokeRunner>>();
        var watchdog = services.GetRequiredService<DispatcherWatchdog>();
        var shell = services.GetRequiredService<ShellViewModel>();
        var live = shell.Live ?? throw new InvalidOperationException("The Live page is not registered.");
        var phases = new SmokePhases(watchdog);

        shell.SelectedItem = shell.NavigationItems.Single(i => i.Page == live);
        await IdleAsync();
        await Task.Delay(500);
        logger.LogInformation("Smoke live warm-up: Live page first shown (cold): {Measured}", phases.End("live first show", cold: true).Measured);
        phases.Begin();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        live.SelectedProcess = new ProcessRow(Environment.ProcessId, "synthetic", string.Empty, string.Empty, 0, string.Empty, false);
        live.WriteTimeline = false;
        live.RecordRuns = false;
        await live.StartCommand.ExecuteAsync(null);
        if (live.Run is { } run)
        {
            await run.Completion;
        }

        await IdleAsync();
        await Task.Delay(500);
        var completed = live.Shown.Samples.Count == Math.Min(count, LiveRunState.WindowSamples) && !live.IsRunning;
        var feed = phases.End("live feed", cold: false);
        logger.LogInformation(
            "Smoke live: {Count:N0} samples in {Seconds:N1} s, {Kept:N0} charted ({Status}), {Measured}",
            count,
            stopwatch.Elapsed.TotalSeconds,
            live.Shown.Samples.Count,
            live.Shown.RunWarning ?? live.Shown.Status ?? "ok",
            feed.Measured);
        return SmokeVerdict.Phase(completed, feed.MaxOperation, feed.MaxLag);
    }

    /// <summary>How long the run with a real process watches it.</summary>
    private static readonly TimeSpan s_liveProcessTime = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Watches a real process on the Live page while it records the process's allocations: its samples, the live
    /// allocations and their call stacks, checked against the dispatcher budget. Records into a session, so only under a data folder
    /// that was moved: nothing is left in the user's own.
    /// </summary>
    private async Task<int> RunSmokeLiveProcessAsync(int pid)
    {
        var services = _services;
        var logger = services.GetRequiredService<ILogger<SmokeRunner>>();
        if (!HeapScope.Application.Sessions.MovedDataFolder.IsMoved)
        {
            logger.LogError("Smoke live process: set {Variable}, this run records into a session", HeapScope.Application.Sessions.MovedDataFolder.Variable);
            return SmokeVerdict.Phase(completed: false, TimeSpan.Zero, TimeSpan.Zero);
        }

        var watchdog = services.GetRequiredService<DispatcherWatchdog>();
        var shell = services.GetRequiredService<ShellViewModel>();
        var live = shell.Live ?? throw new InvalidOperationException("The Live page is not registered.");
        var phases = new SmokePhases(watchdog);
        shell.SelectedItem = shell.NavigationItems.Single(i => i.Page == live);
        await IdleAsync();
        await Task.Delay(500);
        phases.Begin();
        live.SelectedProcess = new ProcessRow(pid, "watched", string.Empty, string.Empty, 0, string.Empty, false);
        live.RecordAllocations = true;
        await live.StartCommand.ExecuteAsync(null);
        var allocations = live.Allocations;
        var until = DateTime.UtcNow + s_liveProcessTime;
        while (DateTime.UtcNow < until && allocations.Types.Count == 0)
        {
            await Task.Delay(100);
        }

        // The panel's and its call stacks' first shows are cold costs, as a page's is.
        allocations.ShowStacks = true;
        await Task.Delay(2000);
        logger.LogInformation("Smoke live process: allocations first shown (cold): {Measured}", phases.End("live allocations first show", cold: true).Measured);
        phases.Begin();
        var longest = 0.0;
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(1000);
            allocations.RefreshStacksCommand.Execute(null);
            longest = Math.Max(longest, allocations.DelaySeconds ?? 0);
        }

        var feed = phases.End("live allocations", cold: false);
        live.StopCommand.Execute(null);
        if (live.Run is { } run)
        {
            await run.Completion;
        }

        await allocations.EndTask.WaitAsync(TimeSpan.FromSeconds(60));
        var completed = allocations.Types.Count > 0 && allocations.Stacks.Count > 0 && string.IsNullOrEmpty(allocations.Note);
        logger.LogInformation(
            "Smoke live process: {Summary} {Types} types and {Stacks} stacks shown, longest delay {Delay:N2} s, note {Note}; {Measured}",
            allocations.Summary,
            allocations.Types.Count,
            allocations.Stacks.Count,
            longest,
            string.IsNullOrEmpty(allocations.Note) ? "none" : allocations.Note,
            feed.Measured);
        return SmokeVerdict.Phase(completed, feed.MaxOperation, feed.MaxLag);
    }

    /// <summary>
    /// Opens a dump through the Snapshots page, waits for its overview, checks the dispatcher budget like
    /// <see cref="RunSmokeAsync"/> and closes the app with a result code.
    /// </summary>
    private async Task<int> RunSmokeOpenAsync(string dump)
    {
        var services = _services;
        var logger = services.GetRequiredService<ILogger<SmokeRunner>>();
        var watchdog = services.GetRequiredService<DispatcherWatchdog>();
        var home = services.GetRequiredService<HomeViewModel>();
        var snapshots = services.GetRequiredService<SnapshotsViewModel>();

        // Warm-up as in RunSmokeAsync, so one-time cold costs are not counted: a tiny job, then the Snapshots page shown
        // once (pages are built on first show and kept). The cold first show is logged, not checked.
        home.ObjectCount = 10_000;
        await home.RunSyntheticCommand.ExecuteAsync(null);
        await IdleAsync();
        await Task.Delay(500);
        var phases = new SmokePhases(watchdog);
        phases.Begin();
        var shell = services.GetRequiredService<ShellViewModel>();
        shell.SelectedItem = shell.NavigationItems.Single(i => i.Page == snapshots);
        await IdleAsync();
        await Task.Delay(500);
        logger.LogInformation("Smoke warm-up: Snapshots page first shown (cold): {Measured}", phases.End("snapshots first show", cold: true).Measured);
        phases.Begin();

        // --smoke-trace records this process from here to the end of the run, for diagnosing lags.
        using var traceStop = new CancellationTokenSource();
        var trace = services.GetRequiredService<UiOptions>().SmokeTracePath is { } tracePath
            ? services.GetRequiredService<HeapScope.Application.Sessions.TraceService>()
                .RecordAsync(Environment.ProcessId, tracePath, HeapScope.Application.Sessions.TraceKind.Cpu, TimeSpan.FromMinutes(10), traceStop.Token)
            : null;

        // The trace stops however the run ends: a failing step must not leave it recording.
        VirtualRowList<TypeRowItem> rows;
        bool completed;
        try
        {
            await snapshots.OpenPathAsync(dump);
            await snapshots.Types.QueryTask;

            // Touch the first rows as the grid would, and wait for their page.
            rows = snapshots.Types.Rows;
            await TouchRowsAsync(rows);
            completed = snapshots.Selected is not null && rows.Count > 0 && rows.GetRow(0).IsLoaded;

            // The largest types as shown, for checks of the known answers in CI. A contract: ci.yml matches
            // "Smoke open types: ... <type> <count>"; keep the wording.
            logger.LogInformation(
                "Smoke open types: {Types}",
                string.Join("; ", Enumerable.Range(0, Math.Min(rows.Count, 50)).Select(rows.GetRow).Where(r => r.IsLoaded).Select(r => $"{r.Name} {r.Objects}")));
            await Task.Delay(500);

            // The open and the types grid are checked; each phase below logs its cold first show and ends its warm part here,
            // after the drain, so the checked maxima are every warm part's (SmokePhases.Checked).
            logger.LogInformation("Smoke open phase: {Measured}", phases.End("open", cold: false).Measured);
            if (completed)
            {
                completed = await RunSmokeDrillAsync(snapshots, phases, logger);
                await Task.Delay(500);
                phases.End("drill", cold: false);
            }

            if (completed)
            {
                completed = await RunSmokeInspectionsAsync(snapshots, phases, logger);
                await Task.Delay(500);
                phases.End("inspections", cold: false);
            }

            if (completed)
            {
                completed = await RunSmokeRetainersAsync(snapshots, phases, logger);
                await Task.Delay(500);
                phases.End("retainers drill", cold: false);
            }

            if (completed && services.GetRequiredService<UiOptions>().SmokeAllocationsPath is { } allocationTrace)
            {
                completed = await RunSmokeAllocationsAsync(shell, allocationTrace, phases, logger);
                shell.SelectedItem = shell.NavigationItems.Single(i => i.Page == snapshots);
                await Task.Delay(500);
                phases.End("allocations drill", cold: false);
            }

            if (completed && services.GetRequiredService<UiOptions>().SmokeComparePath is { } other)
            {
                completed = await RunSmokeCompareAsync(snapshots, other, phases, logger);
                await Task.Delay(500);
                phases.End("compare", cold: false);
            }
        }
        finally
        {
            if (trace is not null)
            {
                await traceStop.CancelAsync();
                try
                {
                    var recorded = await trace;
                    logger.LogInformation("Smoke trace written: {Path} ({Bytes:N0} bytes)", recorded.Path, recorded.Bytes);
                }
                catch (Exception ex) when (ex is HeapScope.Application.Sessions.CaptureFailedException or OperationCanceledException)
                {
                    logger.LogWarning(ex, "Smoke trace failed");
                }
            }
        }

        var (lag, operation) = phases.Checked;
        logger.LogInformation(
            "Smoke open: snapshot shown {Completed} ({Status}), {Pages} of {TotalPages} row pages fetched, {Measured}",
            completed,
            snapshots.Status ?? "ok",
            rows.RequestedPages.Count,
            (rows.Count + VirtualRowList.PageSize - 1) / VirtualRowList.PageSize,
            new SmokePhase("run", false, lag, operation, phases.SlowOperations).Measured);
        logger.LogInformation(
            "Smoke slow operations: {Count} UI operations over 100 ms in the run, ceiling {Ceiling}; by phase: {Phases}",
            phases.SlowOperations,
            SlowOperationCeiling,
            string.Join(", ", phases.All.Where(p => p.SlowOperations > 0).Select(p => $"{p.Name} {p.SlowOperations}")) is { Length: > 0 } byPhase ? byPhase : "none");
        return SmokeVerdict.Ceiling(SmokeVerdict.Phase(completed, operation, lag), phases.SlowOperations, SlowOperationCeiling, Program.IsHeadless);
    }

    /// <summary>
    /// Drills down as a user would: the largest type's objects, the largest object, its references. The first
    /// showing of each view is logged as a cold cost; a re-sort of the objects and a second object are the checked part.
    /// Returns whether every step showed its data.
    /// </summary>
    private static async Task<bool> RunSmokeDrillAsync(SnapshotsViewModel snapshots, SmokePhases phases, ILogger logger)
    {
        var navigator = snapshots.Navigator;
        var type = snapshots.Types.Rows.GetRow(0);
        snapshots.Types.OpenRowCommand!.Execute(type);
        if (navigator.Current is not InstancesViewModel instances)
        {
            return Failed(logger, "the largest type's objects did not open");
        }

        await instances.QueryTask;
        var objects = instances.Rows;
        await TouchRowsAsync(objects);
        if (objects.Count == 0)
        {
            return Failed(logger, "the largest type has no objects");
        }

        instances.OpenRowCommand!.Execute(objects.GetRow(0));
        if (navigator.Current is not ObjectViewModel first)
        {
            return Failed(logger, "the first object did not open");
        }

        await first.LoadTask;
        await first.ReferenceRows.WhenLoadedAsync();

        // Cold: the object's referrers (the first query of a snapshot builds its reverse index in the worker
        // while the window stays responsive) and who references the type's objects, each view shown for the first time.
        first.ReferencesView = ObjectReferenceTabs.Incoming;
        await first.IncomingTask;
        await first.IncomingRows.WhenLoadedAsync();

        // Cold: why the object is alive, and why the type's objects are. Then, cold: its retained size,
        // after the paths (the first such query computes the snapshot's dominators, or opens them from their sidecar).
        first.ReferencesView = ObjectReferenceTabs.Retention;
        await first.RetentionTask;
        await first.RetainedTask;
        navigator.BackCommand.Execute(null);
        instances.WhyAliveCommand!.Execute(null);
        if (navigator.Current is not TypePathsViewModel typePaths)
        {
            return Failed(logger, "the type's retention paths did not open");
        }

        await typePaths.LoadTask;
        var typePathsShown = typePaths.Totals is not null;
        navigator.BackCommand.Execute(null);
        instances.ReferencedByCommand!.Execute(null);
        if (navigator.Current is not TypeReferrersViewModel referrers)
        {
            return Failed(logger, "the type's referrers did not open");
        }

        await referrers.QueryTask;
        await referrers.Rows.WhenLoadedAsync();

        // Opening the next object drops the referrers from the forward history (their rows are released), so judge them now.
        var referrersShown = referrers.Totals is not null;
        await IdleAsync();
        logger.LogInformation(
            "Smoke drill: {Type} has {Objects:N0} objects; first object {Object} ({Status}), {Incoming}, {Retention}, {Retained}; {Title}: {Totals}; {PathsTitle}: {Paths} key paths; first show (cold): {Measured}",
            instances.TypeName,
            objects.Count,
            first.Title,
            first.Status ?? first.FieldsUnavailable ?? "fields read",
            first.IncomingTotals ?? first.IncomingStatus,
            first.RetentionStatus,
            first.RetainedSummary,
            referrers.Title,
            referrers.Totals ?? referrers.Status,
            typePaths.Title,
            typePaths.Paths.Count,
            phases.End("drill first show", cold: true).Measured);

        // Checked part: back to the objects, a re-sort, retained sizes (the dominators warm), and another object
        // with its referrers and retention (the views and the indices warm). The caller ends it.
        phases.Begin();
        navigator.BackCommand.Execute(null);
        instances.SortByCommand.Execute(Contracts.InstanceColumns.Address);
        await instances.QueryTask;
        instances.ShowRetained = true;
        await instances.QueryTask;
        var retainedShown = instances.RetainedColumnShown;
        objects = instances.Rows;
        await TouchRowsAsync(objects);
        instances.OpenRowCommand!.Execute(objects.GetRow(objects.Count > 1 ? 1 : 0));
        if (navigator.Current is not ObjectViewModel second)
        {
            return Failed(logger, "the second object did not open");
        }

        await second.LoadTask;
        await second.ReferenceRows.WhenLoadedAsync();
        second.ReferencesView = ObjectReferenceTabs.Incoming;
        await second.IncomingTask;
        await second.IncomingRows.WhenLoadedAsync();
        second.ReferencesView = ObjectReferenceTabs.Retention;
        await second.RetentionTask;
        await second.RetainedTask;
        // Each check by name, so a red run says which view did not show its data.
        (string Check, bool Passed)[] checks =
        [
            ("first object read", first.Status is null),
            ("second object read", second.Status is null),
            ("first object's fields or text", first.Fields.Count > 0 || first.FieldsUnavailable is not null || first.Text is not null),
            ("first object's referrers", first.IncomingTotals is not null),
            ("second object's referrers", second.IncomingTotals is not null),
            ("type's referrers", referrersShown),
            ("first object's retention tree", first.RetentionTree.Count == 1),
            ("second object's retention tree", second.RetentionTree.Count == 1),
            ("type's retention paths", typePathsShown),
            ("retained column", retainedShown),
            ("first object's retained size", first.RetainedSummary?.StartsWith("Retains", StringComparison.Ordinal) == true),
            ("second object's retained size", second.RetainedSummary?.StartsWith("Retains", StringComparison.Ordinal) == true),
        ];
        return SmokePhases.Failed(checks) is not { } failed || Failed(logger, failed);
    }

    private static bool Failed(ILogger logger, string step)
    {
        logger.LogWarning("Smoke step failed: {Step}", step);
        return false;
    }

    /// <summary>Asks for the first rows as a grid on screen would, and waits for their page.</summary>
    private static async Task TouchRowsAsync<TRow>(VirtualRowList<TRow> rows)
        where TRow : class, IIndexedRow
    {
        for (var i = 0; i < Math.Min(rows.Count, 50); i++)
        {
            rows.GetRow(i);
        }

        await rows.WhenLoadedAsync();
    }

    /// <summary>
    /// Shows the Inspections tab: every inspection runs in the worker while the tab stays responsive; the
    /// rows of the selected one are fetched as the grid would. Returns whether every available inspection finished.
    /// </summary>
    private static async Task<bool> RunSmokeInspectionsAsync(SnapshotsViewModel snapshots, SmokePhases phases, ILogger logger)
    {
        phases.Begin();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        snapshots.Tab = SnapshotTabs.Inspections;
        var inspections = snapshots.Inspections;
        await inspections.RunTask;
        var rows = inspections.Rows;
        await TouchRowsAsync(rows);
        await IdleAsync();
        var finished = inspections.Tiles.Count > 0 && inspections.Tiles.All(t => !t.IsAvailable || (t.Run is not null && t.Status is null));
        logger.LogInformation(
            "Smoke inspections: {Count} inspections in {Seconds:N1} s ({Findings}), selected {Selected}; {Measured}",
            inspections.Tiles.Count,
            stopwatch.Elapsed.TotalSeconds,
            string.Join("; ", inspections.Tiles.Select(t => $"{t.Info.Name}: {t.Status ?? t.Summary}")),
            inspections.Selected?.Title ?? "none",
            phases.SoFar("inspections so far").Measured);
        snapshots.Tab = SnapshotTabs.Snapshot;
        return finished;
    }

    /// <summary>
    /// Shows the Retainers tab: the biggest retainers and their icicle (the first time computes or opens the
    /// dominators in the worker; logged, not checked), then the checked part in the warm tab: expand the first row and focus
    /// on it, as a user would. Returns whether the tree showed rows each time.
    /// </summary>
    private static async Task<bool> RunSmokeRetainersAsync(SnapshotsViewModel snapshots, SmokePhases phases, ILogger logger)
    {
        var retainers = snapshots.Retainers;
        phases.Begin();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        snapshots.Tab = SnapshotTabs.Retainers;
        await retainers.QueryTask;
        await retainers.IcicleTask;
        await TouchRowsAsync(retainers.Rows);
        await IdleAsync();
        var shown = retainers.Rows.Count > 0 && retainers.Rows.GetRow(0).IsLoaded;
        logger.LogInformation(
            "Smoke retainers: {Shown} ({Status}) in {Seconds:N1} s, {Rows:N0} rows, {Bars:N0} icicle bars, first {First}; first show (cold): {Measured}",
            shown,
            retainers.Status ?? retainers.IcicleStatus ?? "ok",
            stopwatch.Elapsed.TotalSeconds,
            retainers.Rows.Count,
            retainers.IcicleSegments.Count,
            shown ? retainers.Rows.GetRow(0).Name : "none",
            phases.End("retainers first show", cold: true).Measured);
        if (!shown)
        {
            return false;
        }

        phases.Begin();
        var first = retainers.Rows.GetRow(0);
        retainers.ToggleCommand.Execute(first);
        await retainers.QueryTask;
        await TouchRowsAsync(retainers.Rows);
        var expanded = retainers.Rows.Count;
        retainers.FocusCommand.Execute(retainers.Rows.GetRow(0));
        await retainers.QueryTask;
        await retainers.IcicleTask;
        await TouchRowsAsync(retainers.Rows);
        await IdleAsync();
        var focused = retainers.HasFocus && retainers.Rows.Count > 0 && retainers.Rows.GetRow(0).IsLoaded;
        logger.LogInformation(
            "Smoke retainers drill: expanded to {Expanded:N0} rows, focused {Focused} ({Totals}); {Measured}",
            expanded,
            focused,
            retainers.Totals,
            phases.SoFar("retainers drill so far").Measured);
        retainers.ShowTopCommand.Execute(null);
        await retainers.QueryTask;
        snapshots.Tab = SnapshotTabs.Snapshot;
        return focused;
    }

    /// <summary>
    /// Shows an allocations trace on the Allocations page: its first show is logged as a cold cost; expanding
    /// the first back trace and switching to the call tree are the checked part. Returns whether both showed rows.
    /// </summary>
    private static async Task<bool> RunSmokeAllocationsAsync(ShellViewModel shell, string path, SmokePhases phases, ILogger logger)
    {
        phases.Begin();
        var allocations = shell.Allocations;
        if (allocations is null)
        {
            return false;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await allocations.OpenPathAsync(path);
        if (allocations.Selected is not { } trace)
        {
            logger.LogWarning("Smoke allocations: {Path} was not shown: {Status}", path, allocations.Status);
            return false;
        }

        await trace.LoadTask;
        await trace.Types.QueryTask;
        await trace.Tree.QueryTask;
        await TouchRowsAsync(trace.Types.Rows);
        await TouchRowsAsync(trace.Tree.Rows);
        await IdleAsync();
        var shown = trace.Tree.Rows.Count > 0 && trace.Tree.Rows.GetRow(0).IsLoaded;
        logger.LogInformation(
            "Smoke allocations: {Shown} ({Status}) in {Seconds:N1} s, {Types:N0} types, {Stacks} top stacks, first back trace {First}; first show (cold): {Measured}",
            shown,
            trace.Tree.Status ?? trace.StacksStatus ?? "ok",
            stopwatch.Elapsed.TotalSeconds,
            trace.Types.Rows.Count,
            trace.Stacks.Count,
            shown ? trace.Tree.Rows.GetRow(0).Name : "none",
            phases.End("allocations first show", cold: true).Measured);
        if (!shown)
        {
            return false;
        }

        phases.Begin();
        trace.Tree.ToggleCommand.Execute(trace.Tree.Rows.GetRow(0));
        await trace.Tree.QueryTask;
        await TouchRowsAsync(trace.Tree.Rows);
        var expanded = trace.Tree.Rows.Count;
        trace.ShowCallTree = true;
        await trace.Tree.QueryTask;
        await TouchRowsAsync(trace.Tree.Rows);
        await IdleAsync();
        var callTree = trace.Tree.Rows.Count > 0 && trace.Tree.Rows.GetRow(0).IsLoaded;
        logger.LogInformation(
            "Smoke allocations drill: expanded to {Expanded:N0} rows, call tree {CallTree}; {Measured}",
            expanded,
            callTree,
            phases.SoFar("allocations drill so far").Measured);
        return callTree;
    }

    /// <summary>
    /// Opens a second dump and shows the comparison: the pickers fill themselves with the two open snapshots,
    /// the grid's first rows are fetched as the grid would. Returns whether the comparison showed rows.
    /// </summary>
    private static async Task<bool> RunSmokeCompareAsync(SnapshotsViewModel snapshots, string dump, SmokePhases phases, ILogger logger)
    {
        phases.Begin();
        // Show the (empty) tab first and let it render, then open: the tab's first layout and the rows are logged apart.
        snapshots.Tab = SnapshotTabs.Compare;
        await IdleAsync();
        logger.LogInformation("Smoke compare: tab shown");
        await snapshots.OpenPathAsync(dump);
        await snapshots.Compare.QueryTask;
        var rows = snapshots.Compare.Rows;
        await TouchRowsAsync(rows);
        var shown = rows.Count > 0 && rows.GetRow(0).IsLoaded;
        logger.LogInformation(
            "Smoke compare: {Shown} ({Status}), {Rows:N0} rows, first {First}; {Totals}; first show (cold): {Measured}",
            shown,
            snapshots.Compare.Status ?? snapshots.Status ?? "ok",
            rows.Count,
            shown ? rows.GetRow(0).Name : "none",
            snapshots.Compare.Totals,
            phases.End("compare first show", cold: true).Measured);

        // The checked part, like the other smoke runs after their warm-up: a re-query and its rows in the warm grid
        // (one-time cold costs of the tab are logged above and tracked by the UI cold start backlog item).
        await IdleAsync();
        phases.Begin();
        snapshots.Compare.SortByCommand.Execute(TypeDiffColumns.ObjectsDelta);
        await snapshots.Compare.QueryTask;
        rows = snapshots.Compare.Rows;
        await TouchRowsAsync(rows);
        return shown && rows.Count > 0 && rows.GetRow(0).IsLoaded;
    }
}
