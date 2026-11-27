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
using Avalonia.Threading;
using HeapScope.Application;
using HeapScope.Application.Logging;
using HeapScope.Application.Workers;
using HeapScope.UI.Services;
using HeapScope.UI.ViewModels;
using HeapScope.UI.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI;

/// <summary>Options read from the UI's command line.</summary>
/// <param name="SmokeSyntheticObjects">When set (<c>--smoke-synthetic N</c>), the app runs one synthetic job of N
/// objects, records the maximum dispatcher lag and exits.</param>
/// <param name="SmokeOpenPath">When set (<c>--smoke-open path</c>), the app opens this dump instead, waits for its overview
/// and exits with the same dispatcher checks.</param>
/// <param name="SmokeComparePath">With <c>--smoke-open</c>, a second dump to compare with.</param>
/// <param name="SmokeTracePath">With <c>--smoke-open</c>, where to record a CPU trace of the run.</param>
/// <param name="SmokeLiveSamples">When set (<c>--smoke-live N</c>), the Live page charts N synthetic samples fed fast
/// and the run exits with the dispatcher checks.</param>
/// <param name="SmokeAllocationsPath">With <c>--smoke-open</c>, an allocations trace to show on the Allocations page.</param>
/// <param name="ScreenshotFolder">With <c>--headless</c> (<c>--screenshots folder</c>), saves each page as PNG files (<see cref="Smoke.ScreenshotRunner"/>).</param>
/// <param name="SmokeLiveProcess">When set (<c>--smoke-live-process pid</c>), the Live page watches this process for real and records its allocations
/// under a data folder of its own, and the run exits with the dispatcher checks.</param>
/// <param name="ScreenshotLivePid">With <c>--screenshots</c> (<c>--screenshot-live-pid N</c>), the Live page's picture watches this process for real.</param>
/// <param name="SmokeMorePaths">With <c>--smoke-open</c> (<c>--smoke-more a.dmp;b.dmp</c>), more dumps opened after it, each drilled into and
/// inspected, with the UI process's memory logged after each: what several open snapshots cost the front end.</param>
public sealed record UiOptions(int? SmokeSyntheticObjects, string? SmokeOpenPath = null, string? SmokeComparePath = null, string? SmokeTracePath = null, int? SmokeLiveSamples = null, string? SmokeAllocationsPath = null, string? ScreenshotFolder = null, int? ScreenshotLivePid = null, int? SmokeLiveProcess = null, IReadOnlyList<string>? SmokeMorePaths = null)
{
    /// <summary>The paths of <c>--smoke-more</c>, separated by semicolons; none for null or blank.</summary>
    public static IReadOnlyList<string> SplitPaths(string? paths) =>
        paths?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    public bool IsSmokeRun => SmokeSyntheticObjects is not null || SmokeOpenPath is not null || SmokeLiveSamples is not null || SmokeLiveProcess is not null || ScreenshotFolder is not null;
}

/// <summary>Builds the generic host (DI, configuration, logging) for the app.</summary>
internal static class UiHost
{
    public static IHost Build(string[] args, Dispatcher dispatcher)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();
        var fileLog = builder.Services.AddHeapScopeFileLog(builder.Configuration, "ui");
        builder.Logging.Configure(o => o.ActivityTrackingOptions = ActivityTrackingOptions.TraceId);
        if (fileLog.MinimumLevel < LogLevel.Information)
        {
            builder.Logging.SetMinimumLevel(fileLog.MinimumLevel);
        }

        builder.Services.PostConfigure<WorkerHostOptions>(o => o.WorkerLogLevel = fileLog.MinimumLevel);

        // The live smoke run charts made-up samples: no process is monitored.
        var smokeLive = builder.Configuration["smoke-live"];
        if (smokeLive is not null)
        {
            builder.Services.AddSingleton<HeapScope.Application.Monitoring.ICounterSource>(
                new HeapScope.Application.Monitoring.SyntheticCounterSource(int.Parse(smokeLive, NumberStyles.None, CultureInfo.InvariantCulture), perSecond: 200));
        }

        builder.Services.AddHeapScopeApplication();
        builder.Services.Configure<WorkerHostOptions>(builder.Configuration.GetSection(WorkerHostOptions.SectionName));
        builder.Services.Configure<WorkerHostOptions>(builder.Configuration.GetSection(WorkerHostOptions.SymbolsSectionName));
        builder.Services.Configure<HeapScope.Application.Sessions.SessionOptions>(builder.Configuration.GetSection(HeapScope.Application.Sessions.SessionOptions.SectionName));

        var smoke = builder.Configuration["smoke-synthetic"];
        var options = new UiOptions(
            smoke is null ? null : int.Parse(smoke, NumberStyles.None, CultureInfo.InvariantCulture),
            builder.Configuration["smoke-open"],
            builder.Configuration["smoke-compare"],
            builder.Configuration["smoke-trace"],
            smokeLive is null ? null : int.Parse(smokeLive, NumberStyles.None, CultureInfo.InvariantCulture),
            builder.Configuration["smoke-allocations"],
            builder.Configuration["screenshots"],
            builder.Configuration["screenshot-live-pid"] is { } livePid ? int.Parse(livePid, NumberStyles.None, CultureInfo.InvariantCulture) : null,
            builder.Configuration["smoke-live-process"] is { } watched ? int.Parse(watched, NumberStyles.None, CultureInfo.InvariantCulture) : null,
            UiOptions.SplitPaths(builder.Configuration["smoke-more"]));
        builder.Services.AddSingleton(options);

        builder.Services.AddSingleton<IUiDispatcher>(new AvaloniaUiDispatcher(dispatcher));
        builder.Services.AddSingleton(sp => new DispatcherWatchdog(
            dispatcher, sp.GetRequiredService<ILogger<DispatcherWatchdog>>(), options.IsSmokeRun ? DispatcherWatchdog.SmokeProbeInterval : null));
        builder.Services.AddSingleton<IUserInteraction>(sp => options.IsSmokeRun
            ? new AutomaticUserInteraction()
            : new AvaloniaUserInteraction(() => sp.GetService<MainWindow>(), sp.GetService<ILogger<AvaloniaUserInteraction>>()));
        builder.Services.AddHeapScopeViewModels();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<ViewPrewarmer>();
        return builder.Build();
    }
}
