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

using System.Text.RegularExpressions;

namespace HeapScope.Architecture.Tests;

/// <summary>
/// Every version has its release notes in both places (owner, 2026-11-18): the app's (RELEASE-NOTES.md) and the CLI
/// package's, which nuget.org shows (src/HeapScope.Cli/package-release-notes.txt). /close-sprint writes both when it sets
/// the version, and this fails a version that has only one.
/// </summary>
public partial class ReleaseNotesTests
{
    private static string Version()
    {
        var match = VersionElement().Match(File.ReadAllText(Path.Combine(RepoPaths.Root, "Directory.Build.props")));
        match.Success.ShouldBeTrue("Directory.Build.props has no <Version>");
        return match.Groups[1].Value;
    }

    [Fact]
    public void The_app_release_notes_have_a_section_for_the_version()
    {
        var version = Version();
        File.ReadAllLines(Path.Combine(RepoPaths.Root, "RELEASE-NOTES.md"))
            .ShouldContain(line => line.StartsWith($"## {version}: ", StringComparison.Ordinal), $"RELEASE-NOTES.md has no \"## {version}: \" section");
    }

    [Fact]
    public void The_cli_package_release_notes_start_with_the_version()
    {
        var version = Version();
        var first = File.ReadLines(Path.Combine(RepoPaths.Root, "src", "HeapScope.Cli", "package-release-notes.txt")).FirstOrDefault() ?? "";
        first.ShouldStartWith($"{version}: ", Case.Sensitive, "package-release-notes.txt must start with the version and a headline");
    }

    [GeneratedRegex("<Version>([^<]+)</Version>")]
    private static partial Regex VersionElement();
}
