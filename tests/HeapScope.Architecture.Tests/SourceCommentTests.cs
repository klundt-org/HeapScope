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
/// Source comments say why, not when (DEVELOPER.md 4): no sprint or review ids in src. A decision keeps its ADR number;
/// the history lives in git, the review files and the ADRs.
/// </summary>
public partial class SourceCommentTests
{
    [Fact]
    public void No_source_comment_cites_a_sprint_or_a_review()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(RepoPaths.Root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("ThirdParty", StringComparison.Ordinal))
            {
                continue;
            }

            var number = 0;
            foreach (var line in File.ReadLines(file))
            {
                number++;
                var comment = line.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0 && Id().IsMatch(line[comment..]))
                {
                    offenders.Add($"{relative}:{number}: {line.Trim()}");
                }
            }
        }

        offenders.ShouldBeEmpty("say why without the sprint or review that decided it");
    }

    [Theory]
    [InlineData("// Kept while it lives (sprint 2.7).")]
    [InlineData("/// The rows (3.11 review M1): kept.")]
    [InlineData("// Peer review 4.5 H13: one per snapshot.")]
    [InlineData("// Not counted (review M15).")]
    [InlineData("// The review's M10: shown with a next step.")]
    [InlineData("// Measured in the 2.4 re-review.")]
    [InlineData("// Grouped (2.3 decision 7).")]
    [InlineData("// The exact retention of a set is 2.4's.")]
    public void The_rule_finds_ids(string comment) => Id().IsMatch(comment).ShouldBeTrue();

    [Theory]
    [InlineData("// One file per process (ADR-029).")]
    [InlineData("// TraceEvent 4.0.2 reads it the same way.")]
    [InlineData("// Review the rows before you share them.")]
    [InlineData("// Uses .NET 8.0's static storage.")]
    [InlineData("// 1.5 GB at most.")]
    [InlineData("// TraceEvent 4.0.0's source, the commit the package is built from.")]
    public void The_rule_leaves_other_text_alone(string comment) => Id().IsMatch(comment).ShouldBeFalse();

    [GeneratedRegex(@"\b[Ss]prints? \d+(?:\.\d+)*\b|\b\d+\.\d+(?:\.\d+)? (?:re-)?review\b|\b(?:[Pp]eer )?[Rr]eview(?:'s)? [A-Z]{1,2}\d+\b|\b[Pp]eer review\b|\bre-review\b|\breview of \d+\.\d+|\b[A-Z]{1,2}\d+ of the\b|\b\d+\.\d+(?:\.\d+)? decision \d|\b[1-4]\.\d{1,2}'s\b")]
    private static partial Regex Id();
}
