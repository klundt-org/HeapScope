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
