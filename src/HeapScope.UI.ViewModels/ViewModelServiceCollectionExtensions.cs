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


using HeapScope.Application;
using HeapScope.Application.Monitoring;
using HeapScope.Application.Sessions;
using HeapScope.Application.Snapshots;
using HeapScope.UI.Services;
using HeapScope.UI.ViewModels;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The shell's view models, wired once for the app and the UI tests (sprint 2.3.7 review M15): a new dependency is added
/// here, not in two places. The host supplies the Application services, an <see cref="IUiDispatcher"/> and an
/// <see cref="IUserInteraction"/>.
/// </summary>
public static class ViewModelServiceCollectionExtensions
{
    public static IServiceCollection AddHeapScopeViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(sp => new SnapshotsViewModel(
            sp.GetRequiredService<SnapshotSession>(),
            sp.GetRequiredService<WorkerOperations>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<IUserInteraction>(),
            sp.GetService<ILoggerFactory>(),
            sp.GetService<HeapScope.Application.Bookmarks.BookmarkStore>(),
            sp.GetService<SessionLibrary>(),
            sp.GetRequiredService<AllocationsViewModel>()));
        services.AddSingleton(sp => new HomeViewModel(
            sp.GetRequiredService<WorkerOperations>(),
            sp.GetRequiredService<SnapshotsViewModel>(),
            sp.GetRequiredService<SessionLibrary>(),
            sp.GetService<IUserInteraction>(),
            sp.GetService<ILogger<HomeViewModel>>()));
        services.AddSingleton(sp => new AllocationsViewModel(
            sp.GetRequiredService<HeapScope.Application.Allocations.AllocationSession>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<IUserInteraction>(),
            sp.GetService<ILoggerFactory>()));
        services.AddSingleton(sp => new ProcessesViewModel(
            sp.GetRequiredService<ICaptureService>(),
            sp.GetRequiredService<SnapshotsViewModel>(),
            sp.GetRequiredService<IUserInteraction>(),
            sp.GetRequiredService<AllocationsViewModel>(),
            sp.GetService<HeapScope.Application.Tutorial.SampleService>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetService<ILogger<ProcessesViewModel>>()));
        services.AddSingleton(sp => new LearnViewModel(
            sp.GetService<HeapScope.Application.Tutorial.SampleService>(),
            new HeapScope.Application.Tutorial.LessonProgress(logger: sp.GetService<ILogger<HeapScope.Application.Tutorial.LessonProgress>>()),
            sp.GetService<ILogger<LearnViewModel>>(),
            sp.GetRequiredService<ICaptureService>(),
            sp.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton(sp => new AboutViewModel(
            sp.GetRequiredService<WorkerOperations>(),
            sp.GetRequiredService<IUserInteraction>(),
            sp.GetService<HeapScope.Application.Logging.FileLoggerProvider>()?.Directory,
            sp.GetService<ILogger<AboutViewModel>>()));
        services.AddSingleton(sp => new LiveViewModel(
            sp.GetRequiredService<MonitorService>(),
            sp.GetRequiredService<ICaptureService>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetService<ILogger<LiveViewModel>>(),
            sp.GetRequiredService<IUserInteraction>(),
            sp.GetRequiredService<AllocationsViewModel>(),
            sp.GetService<HeapScope.Application.Allocations.AllocationSession>()));
        services.AddSingleton(sp => new GridExportService(
            sp.GetRequiredService<IUserInteraction>(),
            sp.GetService<ILogger<GridExportService>>()));
        services.AddSingleton(sp => new SettingsViewModel(
            sp.GetRequiredService<HeapScope.Application.Settings.SettingsService>(),
            sp.GetRequiredService<IUserInteraction>(),
            sp.GetService<ILogger<SettingsViewModel>>()));
        services.AddSingleton(sp => new ShellViewModel(
            sp.GetRequiredService<WorkerOperations>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<HomeViewModel>(),
            sp.GetRequiredService<SnapshotsViewModel>(),
            sp.GetRequiredService<ProcessesViewModel>(),
            sp.GetRequiredService<AboutViewModel>(),
            sp.GetRequiredService<LiveViewModel>(),
            sp.GetRequiredService<AllocationsViewModel>(),
            sp.GetRequiredService<GridExportService>(),
            sp.GetRequiredService<SettingsViewModel>(),
            sp.GetRequiredService<LearnViewModel>(),
            sp.GetRequiredService<IUserInteraction>(),
            sp.GetService<ILogger<ShellViewModel>>()));
        return services;
    }
}
