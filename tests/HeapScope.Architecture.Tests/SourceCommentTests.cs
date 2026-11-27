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
/// Source comments say why, not when (DEVELOPER.md 4): no sprint or review ids in src, in C# comments (a run of comment
/// lines is read as one text, so an id split over two lines counts) and in the XML comments of views and project files. A
/// decision keeps its ADR number; the history lives in git, the review files and the ADRs.
/// </summary>
public partial class SourceCommentTests
{
    [Fact]
    public void No_source_comment_cites_a_sprint_or_a_review()
    {
        string[] patterns = ["*.cs", "*.axaml", "*.csproj", "*.props", "*.targets"];
        var offenders = new List<string>();
        foreach (var file in patterns.SelectMany(p => Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, "src"), p, SearchOption.AllDirectories)))
        {
            var relative = Path.GetRelativePath(RepoPaths.Root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("ThirdParty", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var (line, text) in Comments(file, File.ReadAllText(file)))
            {
                if (Id().Match(Spaces().Replace(text, " ")) is { Success: true } found)
                {
                    offenders.Add($"{relative}:{line}: {found.Value}");
                }
            }
        }

        offenders.ShouldBeEmpty("say why without the sprint or review that decided it");
    }

    [Fact]
    public void An_id_split_over_two_comment_lines_or_in_a_view_is_found()
    {
        Comments("a.cs", "// Kept while it lives (sprint\n// 2.7).\nvar x = 1; // fine\n").Where(c => Id().IsMatch(Spaces().Replace(c.Text, " "))).ShouldHaveSingleItem().Line.ShouldBe(1);
        Comments("a.cs", "/* The rows (3.11\n   review M1). */").Select(c => Id().IsMatch(Spaces().Replace(c.Text, " "))).ShouldBe([true]);
        Comments("a.axaml", "<!-- Bookmark marks (sprint 2.7). -->\n<Border />").Select(c => Id().IsMatch(c.Text)).ShouldBe([true]);
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
    [InlineData("// As LiveViewModel does since 3.4.")]
    [InlineData("// Caches written before 1.6 lack the flag.")]
    [InlineData("// The dark theme (Avalonia since 2.3.6).")]
    public void The_rule_finds_ids(string comment) => Id().IsMatch(comment).ShouldBeTrue();

    [Theory]
    [InlineData("// One file per process (ADR-029).")]
    [InlineData("// TraceEvent 4.0.2 reads it the same way.")]
    [InlineData("// Review the rows before you share them.")]
    [InlineData("// Uses .NET 8.0's static storage.")]
    [InlineData("// 1.5 GB at most.")]
    [InlineData("// A share of 0.1 percent allows at most 1,000 per level.")]
    [InlineData("// The load's peak fell from 3.2 GB to 1.7 GB.")]
    [InlineData("// Since .NET 8.0 the storage is a handle.")]
    [InlineData("// TraceEvent 4.0.0's source, the commit the package is built from.")]
    public void The_rule_leaves_other_text_alone(string comment) => Id().IsMatch(comment).ShouldBeFalse();

    [GeneratedRegex(@"\b[Ss]prints? \d+(?:\.\d+)*\b|\b\d+\.\d+(?:\.\d+)? (?:re-)?review\b|\b(?:[Pp]eer )?[Rr]eview(?:'s)? [A-Z]{1,2}\d+\b|\b[Pp]eer review\b|\bre-review\b|\breview of \d+\.\d+|\b[A-Z]{1,2}\d+ of the\b|\b\d+\.\d+(?:\.\d+)? decision \d|\b[1-4]\.\d{1,2}'s\b|\b(?:[Ss]ince|[Bb]efore|[Uu]ntil|[Aa]fter|[Ii]n|[Oo]f|[Ff]rom) [0-4]\.\d{1,2}(?!\.?\d)(?! ?(?:GB|MB|KB|s\b|ms\b|x\b|%|percent))|\b(?:[Ss]ince|[Bb]efore|[Uu]ntil|[Aa]fter) [0-4]\.\d{1,2}\.\d(?!\.?\d)")]
    private static partial Regex Id();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>
    /// The comments of a file with the line each starts on: in C#, a run of `//` lines as one text, a comment after code,
    /// and `/* */`; elsewhere `<!-- -->`.
    /// </summary>
    internal static List<(int Line, string Text)> Comments(string file, string text)
    {
        var found = new List<(int Line, string Text)>();
        if (!file.EndsWith(".cs", StringComparison.Ordinal))
        {
            foreach (Match comment in XmlComment().Matches(text))
            {
                found.Add((LineOf(text, comment.Index), comment.Value));
            }

            return found;
        }

        var lines = text.Split('\n');
        var run = (Line: 0, Text: new System.Text.StringBuilder());
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                if (run.Line == 0)
                {
                    run = (i + 1, new System.Text.StringBuilder());
                }

                run.Text.Append(' ').Append(trimmed.TrimStart('/'));
                continue;
            }

            if (run.Line != 0)
            {
                found.Add((run.Line, run.Text.ToString()));
                run = (0, new System.Text.StringBuilder());
            }

            var after = line.IndexOf("//", StringComparison.Ordinal);
            if (after >= 0 && !line[..after].Contains('"', StringComparison.Ordinal))
            {
                found.Add((i + 1, line[after..]));
            }
        }

        if (run.Line != 0)
        {
            found.Add((run.Line, run.Text.ToString()));
        }

        foreach (Match comment in BlockComment().Matches(text))
        {
            found.Add((LineOf(text, comment.Index), comment.Value));
        }

        return found;
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    [GeneratedRegex(@"<!--[\s\S]*?-->")]
    private static partial Regex XmlComment();

    [GeneratedRegex(@"/\*[\s\S]*?\*/")]
    private static partial Regex BlockComment();
}
