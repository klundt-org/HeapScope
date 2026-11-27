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
using HeapScope.Integration.Tests.Infrastructure;

namespace HeapScope.Integration.Tests;

/// <summary>
/// Sprint 4.8 (H15): the Settings page's DAC folder and download opt-in on the command line. Each run is a CLI process with a data
/// folder of its own (the review's M1: in the shared test data folder, every other test process's worker would have followed them).
/// </summary>
[Trait("Category", "Integration")]
public sealed class CliDacSettingsTests : IDisposable
{
    private readonly TempFolder _folder = new();

    private string Data => _folder.File("data");

    private HeapScopeSettings Saved => HeapScopeSettings.Read(new HeapScopeFolders(Data));

    private async Task<(int Code, string Output)> SettingsAsync(params string[] arguments)
    {
        using var cli = ChildProcess.Start(TestPaths.CliExe, new Dictionary<string, string> { [HeapScopeFolders.EnvironmentVariable] = Data }, ["settings", .. arguments]);
        (await cli.WaitForExitAsync(TimeSpan.FromSeconds(60))).ShouldBeTrue(cli.Output);
        return (cli.Process.ExitCode, cli.Output);
    }

    [Fact]
    public async Task The_dac_folder_is_saved_and_cleared_and_a_folder_that_does_not_exist_changes_nothing()
    {
        var dacs = Directory.CreateDirectory(_folder.File("dacs")).FullName;

        var (code, output) = await SettingsAsync("--saved-dac-folder", dacs);
        code.ShouldBe(0, output);
        output.ShouldContain("DAC folder: " + dacs);
        Saved.DacFolder.ShouldBe(dacs);

        (code, output) = await SettingsAsync("--saved-dac-folder", Path.Combine(dacs, "missing"), "--filter-text-in-logs", "off");
        code.ShouldBe(64, output);
        output.ShouldContain("Nothing was changed: ");
        output.ShouldContain("does not exist");
        Saved.ShouldBe(new HeapScopeSettings(DacFolder: dacs), "nothing else changed either");

        (code, output) = await SettingsAsync("--saved-dac-folder", "none");
        output.ShouldContain("DAC folder: none");
        Saved.DacFolder.ShouldBeNull();
    }

    [Fact]
    public async Task Downloads_from_microsoft_turn_on_only_with_yes()
    {
        var (code, output) = await SettingsAsync("--symbol-server", "on");
        code.ShouldBe(0, output);
        output.ShouldContain("Nothing was changed: run it again with --yes to turn it on.");
        output.ShouldContain("Download missing DACs from Microsoft: off");
        Saved.AllowMicrosoftSymbolServer.ShouldBeFalse("nothing changes without --yes");

        (_, output) = await SettingsAsync("--symbol-server", "on", "--yes");
        output.ShouldContain("Download missing DACs from Microsoft: on");
        Saved.AllowMicrosoftSymbolServer.ShouldBeTrue();

        (_, output) = await SettingsAsync("--symbol-server", "off");
        output.ShouldContain("Download missing DACs from Microsoft: off");
        Saved.AllowMicrosoftSymbolServer.ShouldBeFalse();
    }

    public void Dispose() => _folder.Dispose();
}
