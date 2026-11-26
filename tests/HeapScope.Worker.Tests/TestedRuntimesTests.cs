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

using HeapScope.Worker.Snapshots;

namespace HeapScope.Worker.Tests;

/// <summary>A dump of a runtime the known answers are not checked on says so when it opens (sprint 4.7, peer review 4.5 H1).</summary>
public sealed class TestedRuntimesTests
{
    [Theory]
    [InlineData("8.0.3426.52217")]
    [InlineData("9.0.23")]
    [InlineData("10.0.1526.53008")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void A_tested_or_unknown_runtime_has_no_note(string? version) => TestedRuntimes.Note(version).ShouldBeNull();

    [Theory]
    [InlineData("6.0.36")]
    [InlineData("5.0.17")]
    [InlineData("11.0.0")]
    public void Another_runtime_is_said_untested_with_its_version(string version)
    {
        var note = TestedRuntimes.Note(version).ShouldNotBeNull();
        note.ShouldContain($".NET {version}.");
        note.ShouldContain(".NET 8 to 10");
    }

    [Theory]
    [InlineData("4.8.4645.0")]
    [InlineData("4.700.22.55902")]
    public void A_runtime_before_dotnet_5_is_said_not_supported(string version) =>
        TestedRuntimes.Note(version).ShouldNotBeNull().ShouldContain("older than .NET 5");

    // The fixtures are built for each tested runtime: the list and their TargetFrameworks stay in step.
    [Fact]
    public void The_tested_runtimes_are_the_fixtures_runtimes()
    {
        var project = File.ReadAllText(Path.Combine(HeapScope.Tests.Shared.RepoRoot.Path, "fixtures", "LeakyConsole", "LeakyConsole.csproj"));
        project.ShouldContain($"<TargetFrameworks>{string.Join(';', TestedRuntimes.Majors.Select(m => $"net{m}.0"))}</TargetFrameworks>");
    }
}
