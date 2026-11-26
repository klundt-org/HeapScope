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


using Microsoft.Extensions.Configuration;

namespace HeapScope.Cli.Tests;

/// <summary>A start that fails on appsettings.json says where, and never shows a stack trace (peer review 4.5 H16).</summary>
public sealed class StartFailureTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("heapscope-start-").FullName;

    [Fact]
    public async Task A_settings_file_that_is_not_json_is_said_with_its_file_and_line()
    {
        var file = Path.Combine(_folder, "appsettings.json");
        await File.WriteAllTextAsync(file, "{\n  \"Symbols\": { \"DacFolder\": \"C:\\dacs\" }\n}", TestContext.Current.CancellationToken);

        var (code, output, error) = await InProcessCli.RunAsync((_, _, _) => throw Load(file), "summary", "app.dmp");

        code.ShouldBe(1);
        output.ShouldBeEmpty();
        error.ShouldContain(file + ", line 2 is not valid JSON");
        error.ShouldContain("write each \\ in a path twice");
        error.ShouldNotContain("   at ");
    }

    [Fact]
    public async Task A_value_of_the_wrong_kind_and_any_other_failure_are_said_without_a_stack_trace()
    {
        var wrongKind = new InvalidOperationException("Failed to convert configuration value 'yes' at 'Symbols:AllowMicrosoftSymbolServer' to type 'System.Boolean'.");

        var (code, _, error) = await InProcessCli.RunAsync((_, _, _) => throw wrongKind, "summary", "app.dmp");
        (code, error.Trim()).ShouldBe((1, "A setting has a value HeapScope cannot use: " + wrongKind.Message + " Fix it in appsettings.json or the environment, then run the command again."));

        (_, _, error) = await InProcessCli.RunAsync((_, _, _) => throw new UnauthorizedAccessException("Access denied."), "summary", "app.dmp");
        error.Trim().ShouldStartWith("HeapScope could not start: UnauthorizedAccessException: Access denied. Details are in HeapScope's log in ");
    }

    // Review of 4.6 (High): a failed start or a crash logs the command line as every command's line describes it, so a filter's text
    // stays out when Include filter text in logs is off.
    [Fact]
    public void The_command_line_of_a_failed_start_is_logged_without_the_filter_text_unless_allowed()
    {
        var line = CliCommands.Build(() => throw new InvalidOperationException("No host.")).Parse(["summary", "app.dmp", "--filter", "SecretTypeName"]);

        CliApp.Arguments(includeFilterText: false, line).ShouldNotContain("SecretTypeName");
        CliApp.Arguments(includeFilterText: false, line).ShouldContain("summary");
        CliApp.Arguments(includeFilterText: true, line).ShouldContain("SecretTypeName");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static Exception Load(string file)
    {
        try
        {
            new ConfigurationBuilder().AddJsonFile(file).Build();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("The file was read.");
    }
}
