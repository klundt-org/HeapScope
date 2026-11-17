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

using System.CommandLine;
using System.Globalization;
using HeapScope.Application.Settings;

namespace HeapScope.Cli;

/// <summary>
/// The <c>settings</c> and <c>purge</c> commands (sprint 3.12 review M4, ADR-008: every feature in the UI and on the command
/// line): the Settings page's Include filter text in logs, Purge logs and Purge all, for a user without the app (CI).
/// </summary>
internal static partial class CliCommands
{
    private static Command BuildSettingsCommand(Func<IServiceProvider> services)
    {
        var filterText = new Option<string?>("--filter-text-in-logs")
        {
            Description = "Write the text typed in filters and searches into HeapScope's logs (on, the default) or only its length and terms (off). The app, the worker and the command line follow it.",
        };
        filterText.AcceptOnlyFromAmong("on", "off");
        var command = new Command("settings", "Show HeapScope's settings, or change one. They apply to the app and the command line on this machine.");
        command.Options.Add(filterText);
        command.SetAction((parseResult, ct) => RunAsync(parseResult, services, async context =>
        {
            var settings = context.Get<SettingsService>();
            if (parseResult.GetValue(filterText) is { } value)
            {
                settings.SetIncludeFilterTextInLogs(value == "on");
            }

            await context.Output.WriteLineAsync($"Include filter text in logs: {(settings.IncludeFilterTextInLogs ? "on" : "off")}").ConfigureAwait(false);
            await context.Output.WriteLineAsync($"Settings file: {settings.SettingsPath}").ConfigureAwait(false);
            await context.Output.WriteLineAsync($"Log folder: {settings.LogFolder}").ConfigureAwait(false);
            return Success;
        }));
        return command;
    }

    private static Command BuildPurgeCommand(Func<IServiceProvider> services)
    {
        var logs = new Command("logs", "Delete HeapScope's log files. Logs that running HeapScope processes write now are kept until they close.");
        logs.SetAction((parseResult, ct) => RunAsync(parseResult, services, async context =>
        {
            var deletion = context.Get<SettingsService>().PurgeLogs();
            await context.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"Deleted {deletion.Files.Count} log files ({Mb(deletion.Bytes)}).")).ConfigureAwait(false);
            return await FailedAsync(context, deletion.Failed).ConfigureAwait(false);
        }));

        var yes = new Option<bool>("--yes") { Description = "Delete without asking; without it, Purge all only says what it would delete." };
        var all = new Command("all", "Delete HeapScope's logs and everything in its library: sessions, captures, dumps, traces, timelines, saved indexes and bookmarks. Dumps you opened from your own folders stay.");
        all.Options.Add(yes);
        all.SetAction((parseResult, ct) => RunAsync(parseResult, services, async context =>
        {
            var settings = context.Get<SettingsService>();
            var plan = settings.PlanPurgeAll();
            if (plan.Refused is { } refused)
            {
                await context.Error.WriteLineAsync("Nothing was deleted: " + refused).ConfigureAwait(false);
                return Failure;
            }

            if (!parseResult.GetValue(yes))
            {
                await context.Output.WriteLineAsync(string.Create(
                    CultureInfo.InvariantCulture,
                    $"This deletes HeapScope's logs and its whole library: {plan.Sessions} sessions, {plan.Files.Files.Count} files, {Mb(plan.Files.Bytes)}. Dumps you opened from your own folders stay. This cannot be undone. Run it again with --yes to delete.")).ConfigureAwait(false);
                return Success;
            }

            var (done, deleted) = await settings.PurgeAllAsync(ct).ConfigureAwait(false);
            await context.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"Deleted everything HeapScope keeps: {done.Sessions} sessions, {deleted.Files.Count} files ({Mb(deleted.Bytes)}).")).ConfigureAwait(false);
            return await FailedAsync(context, deleted.Failed).ConfigureAwait(false);
        }));

        var purge = new Command("purge", "Delete HeapScope's logs, or everything it keeps.");
        purge.Subcommands.Add(logs);
        purge.Subcommands.Add(all);
        return purge;
    }

    private static async Task<int> FailedAsync(CliContext context, IReadOnlyList<string>? failed)
    {
        if (failed is not { Count: > 0 })
        {
            return Success;
        }

        await context.Error.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"{failed.Count} files could not be deleted, usually because another program has them open: {string.Join("; ", failed.Take(5))}{(failed.Count > 5 ? " and more" : string.Empty)}. Close it and try again.")).ConfigureAwait(false);
        return Failure;
    }
}
