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
using Microsoft.Extensions.Logging;

namespace HeapScope.Cli;

/// <summary>
/// The <c>settings</c> and <c>purge</c> commands (sprint 3.12 review M4, ADR-008: every feature in the UI and on the command
/// line): the Settings page's Include filter text in logs, Purge logs and Purge all, for a user without the app (CI). Since
/// sprint 3.13.2, Include dump values in logs too, which turns on only with --yes (the app asks instead). Since sprint 4.8 (H15),
/// the DAC folder (<c>--saved-dac-folder</c>, since the run's own <c>--dac-folder</c> is taken) and Download missing DACs from
/// Microsoft (<c>--symbol-server</c>, on only with --yes).
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
        var dumpValues = new Option<string?>("--dump-values-in-logs")
        {
            Description = "Write the values HeapScope reads from dumps (field values and string text) into its logs (on, with --yes) or only their length (off, the default). Dumps can hold passwords, tokens and personal data. The app, the worker and the command line follow it.",
        };
        dumpValues.AcceptOnlyFromAmong("on", "off");
        var dacFolder = new Option<string?>("--saved-dac-folder")
        {
            Description = "Save the folder on this computer searched for a dump's DAC when its runtime is not installed here, or none to clear it. The app and the command line follow it; --dac-folder overrides it for one run.",
        };
        var symbolServer = new Option<string?>("--symbol-server")
        {
            Description = "Download a missing DAC from Microsoft's symbol server (on, with --yes) or never (off, the default). A download sends the name, time stamp and size of each module it asks for (the runtime's, and any whose image the dump lacks), never dump contents. The app and the command line follow it.",
        };
        symbolServer.AcceptOnlyFromAmong("on", "off");
        var yes = new Option<bool>("--yes") { Description = "Confirm turning on --dump-values-in-logs or --symbol-server; without it, the command says what it would do and changes nothing." };
        var command = new Command("settings", "Show HeapScope's settings, or change one. They apply to the app and the command line on this machine.");
        command.Options.Add(filterText);
        command.Options.Add(dumpValues);
        command.Options.Add(dacFolder);
        command.Options.Add(symbolServer);
        command.Options.Add(yes);
        command.SetAction((parseResult, ct) => RunAsync(parseResult, services, async context =>
        {
            var settings = context.Get<SettingsService>();
            if (parseResult.GetValue(dumpValues) == "on" && !parseResult.GetValue(yes))
            {
                // Turned on only when the user confirms, as the app asks first (owner, 2026-11-19); then nothing else changes
                // either, so "Nothing was changed" holds (review M6).
                context.Logger.LogInformation("Settings not changed: --dump-values-in-logs on needs --yes");
                await context.Output.WriteLineAsync(DumpValuesQuestion).ConfigureAwait(false);
                await WriteSettingsAsync(context, settings).ConfigureAwait(false);
                return Success;
            }

            if (parseResult.GetValue(symbolServer) == "on" && !parseResult.GetValue(yes))
            {
                context.Logger.LogInformation("Settings not changed: --symbol-server on needs --yes");
                await context.Output.WriteLineAsync(SymbolServerQuestion).ConfigureAwait(false);
                await WriteSettingsAsync(context, settings).ConfigureAwait(false);
                return Success;
            }

            if (parseResult.GetValue(dacFolder) is { } folder)
            {
                try
                {
                    settings.SetDacFolder(string.Equals(folder.Trim(), "none", StringComparison.OrdinalIgnoreCase) ? null : folder);
                }
                catch (ArgumentException ex)
                {
                    // Checked before anything is saved, so nothing else changes either.
                    context.Logger.LogInformation("Settings not changed: {Reason}", ex.Message);
                    await context.Error.WriteLineAsync("Nothing was changed: " + ex.Message).ConfigureAwait(false);
                    return Usage;
                }
            }

            if (parseResult.GetValue(symbolServer) is { } allow)
            {
                settings.SetAllowMicrosoftSymbolServer(allow == "on");
            }

            if (parseResult.GetValue(filterText) is { } value)
            {
                settings.SetIncludeFilterTextInLogs(value == "on");
            }

            switch (parseResult.GetValue(dumpValues))
            {
                case "on":
                    settings.SetIncludeDumpValuesInLogs(true);
                    break;
                case "off":
                    settings.SetIncludeDumpValuesInLogs(false);
                    await context.Output.WriteLineAsync("Logs written while it was on still hold values read from dumps. Purge logs on the Settings page deletes them, or heapscope-cli purge logs once every HeapScope window is closed.").ConfigureAwait(false);
                    break;
            }

            await WriteSettingsAsync(context, settings).ConfigureAwait(false);
            return Success;
        }));
        return command;
    }

    private static async Task WriteSettingsAsync(CliContext context, SettingsService settings)
    {
        await context.Output.WriteLineAsync($"DAC folder: {settings.DacFolder ?? "none"}{(settings.ConfiguredDacFolder is { } configured ? $" (this run uses {configured}, from --dac-folder or appsettings)" : string.Empty)}").ConfigureAwait(false);
        await context.Output.WriteLineAsync($"Download missing DACs from Microsoft: {(settings.AllowMicrosoftSymbolServer ? "on" : "off")}{(settings.ConfiguredSymbolServer ? " (on for this run, from --allow-symbol-server or appsettings)" : string.Empty)}").ConfigureAwait(false);
        await context.Output.WriteLineAsync($"Include filter text in logs: {(settings.IncludeFilterTextInLogs ? "on" : "off")}").ConfigureAwait(false);
        await context.Output.WriteLineAsync($"Include dump values in logs: {(settings.IncludeDumpValuesInLogs ? "on" : "off")}").ConfigureAwait(false);
        if (HeapScope.Application.Settings.SettingsService.UnderSudo)
        {
            await context.Output.WriteLineAsync("Run with sudo, HeapScope ignores the saved DAC folder and download choice (your own programs can change this file): give --dac-folder or --allow-symbol-server instead.").ConfigureAwait(false);
        }

        await context.Output.WriteLineAsync($"Settings file: {settings.SettingsPath}").ConfigureAwait(false);
        await context.Output.WriteLineAsync($"Log folder: {settings.LogFolder}").ConfigureAwait(false);
    }

    /// <summary>What --dump-values-in-logs on without --yes says (sprint 3.13.2; approved by the owner on 2026-11-19).</summary>
    internal const string DumpValuesQuestion =
        "Turning this on writes the values HeapScope reads from your dumps (field values and string text) into its logs, and they can include passwords, tokens and personal data. The logs stay on this machine and are never sent anywhere, but anyone you give a log to can read them. Nothing was changed: run it again with --yes to turn it on.";

    /// <summary>What --symbol-server on without --yes says (sprint 4.8, a draft in docs/design/info-text.md).</summary>
    internal const string SymbolServerQuestion =
        "Turning this on lets HeapScope download a missing DAC from Microsoft's symbol server when no DAC on this computer matches a dump's runtime. Each download sends the name, time stamp and size of each module it asks for (the runtime's, and any whose image the dump lacks) to Microsoft, never the dump's contents. The app and the command line on this computer follow it. Nothing was changed: run it again with --yes to turn it on.";

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
