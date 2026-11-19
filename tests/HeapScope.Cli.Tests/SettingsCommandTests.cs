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

using HeapScope.Core;

namespace HeapScope.Cli.Tests;

/// <summary>
/// The Settings page on the command line (sprint 3.12 review M4, ADR-008): show and change Include filter text in logs, and
/// Purge all, which only says what it would delete unless told --yes.
/// </summary>
public sealed class SettingsCommandTests
{
    [Fact]
    public async Task Settings_shows_and_changes_include_filter_text_in_logs_where_every_process_reads_it()
    {
        var before = HeapScopeSettings.Read(HeapScopeFolders.Default);
        try
        {
            var (code, output, _) = await InProcessCli.RunAsync(CliHost.Build, "settings", "--filter-text-in-logs", "off");

            code.ShouldBe(0);
            output.ShouldContain("Include filter text in logs: off");
            output.ShouldContain("Settings file: " + HeapScopeSettings.PathIn(HeapScopeFolders.Default));
            HeapScopeSettings.Read(HeapScopeFolders.Default).IncludeFilterTextInLogs.ShouldBeFalse();

            (code, output, _) = await InProcessCli.RunAsync(CliHost.Build, "settings", "--filter-text-in-logs", "on");
            output.ShouldContain("Include filter text in logs: on");
        }
        finally
        {
            before.Save(HeapScopeFolders.Default);
        }
    }

    // Sprint 3.13.2 (owner, 2026-11-19): values read from a dump reach the logs only when the user confirms it.
    [Fact]
    public async Task Dump_values_in_logs_turn_on_only_with_yes_and_off_without_it()
    {
        var before = HeapScopeSettings.Read(HeapScopeFolders.Default);
        try
        {
            new HeapScopeSettings().Save(HeapScopeFolders.Default);

            var (code, output, _) = await InProcessCli.RunAsync(CliHost.Build, "settings", "--dump-values-in-logs", "on");
            code.ShouldBe(0);
            output.ShouldContain(CliCommands.DumpValuesQuestion);
            output.ShouldContain("Include dump values in logs: off");
            HeapScopeSettings.Read(HeapScopeFolders.Default).IncludeDumpValuesInLogs.ShouldBeFalse("nothing changes without --yes");

            (code, output, _) = await InProcessCli.RunAsync(CliHost.Build, "settings", "--dump-values-in-logs", "on", "--yes");
            code.ShouldBe(0);
            output.ShouldContain("Include dump values in logs: on");
            HeapScopeSettings.Read(HeapScopeFolders.Default).IncludeDumpValuesInLogs.ShouldBeTrue();

            (code, output, _) = await InProcessCli.RunAsync(CliHost.Build, "settings", "--dump-values-in-logs", "off");
            code.ShouldBe(0);
            output.ShouldContain("Logs written while it was on still hold values read from dumps. Purge logs on the Settings page deletes them, or heapscope-cli purge logs once every HeapScope window is closed.");
            output.ShouldContain("Include dump values in logs: off");
            HeapScopeSettings.Read(HeapScopeFolders.Default).IncludeFilterTextInLogs.ShouldBeTrue("the other setting is kept");
        }
        finally
        {
            before.Save(HeapScopeFolders.Default);
        }
    }

    [Fact]
    public async Task Dump_values_on_without_yes_changes_no_other_setting_either()
    {
        var before = HeapScopeSettings.Read(HeapScopeFolders.Default);
        try
        {
            new HeapScopeSettings().Save(HeapScopeFolders.Default);

            var (code, output, _) = await InProcessCli.RunAsync(CliHost.Build, "settings", "--filter-text-in-logs", "off", "--dump-values-in-logs", "on");

            code.ShouldBe(0);
            output.ShouldContain(CliCommands.DumpValuesQuestion);
            output.ShouldContain("Include filter text in logs: on");
            var after = HeapScopeSettings.Read(HeapScopeFolders.Default);
            after.IncludeFilterTextInLogs.ShouldBeTrue("not changed either");
            after.IncludeDumpValuesInLogs.ShouldBeFalse();
        }
        finally
        {
            before.Save(HeapScopeFolders.Default);
        }
    }

    [Fact]
    public void The_refusal_is_the_one_the_owner_approved()
    {
        var table = File.ReadAllText(Path.Combine(global::HeapScope.Tests.Shared.RepoRoot.Path, "docs", "design", "info-text.md"));

        table.ShouldContain("on without `--yes`: \"" + CliCommands.DumpValuesQuestion + "\"");
    }

    [Fact]
    public async Task A_value_other_than_on_or_off_is_a_usage_error()
    {
        var (code, _, _) = await InProcessCli.RunAsync(InProcessCli.NoHost, "settings", "--filter-text-in-logs", "maybe");

        code.ShouldBe(64);
    }

    [Fact]
    public async Task Purge_all_without_yes_says_what_it_would_delete_and_deletes_nothing()
    {
        var library = Path.Combine(HeapScopeFolders.Default.Root, "sessions");
        var before = Directory.Exists(library) ? Directory.GetDirectories(library).Length : 0;

        var (code, output, error) = await InProcessCli.RunAsync(CliHost.Build, "purge", "all");

        // Other test processes share this data folder and may be running: then Purge all is refused (review M20), never run.
        if (error.Contains("is running; close it first.", StringComparison.Ordinal))
        {
            code.ShouldBe(1);
            error.ShouldStartWith("Nothing was deleted: ");
        }
        else
        {
            code.ShouldBe(0, error);
            output.ShouldStartWith("This deletes HeapScope's logs and its whole library: ");
            output.ShouldContain("Run it again with --yes to delete.");
        }

        (Directory.Exists(library) ? Directory.GetDirectories(library).Length : 0).ShouldBe(before);
    }
}
