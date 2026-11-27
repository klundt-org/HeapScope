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
/// The front ends use no Core type (ARCHITECTURE.md 3.1), checked in their sources too (sprint 4.8 review H5): a Core constant is
/// inlined by the compiler and leaves no reference to Core in the assembly, so the reference check of <see cref="LayeringTests"/>
/// cannot see it.
/// </summary>
public sealed partial class FrontEndSourceTests
{
    [Fact]
    public void The_front_ends_sources_name_no_core_type()
    {
        var problems = new List<string>();
        foreach (var project in new[] { "HeapScope.UI", "HeapScope.UI.ViewModels", "HeapScope.Cli" })
        {
            var folder = Path.Combine(RepoPaths.Root, "src", project);
            foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(static f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".axaml", StringComparison.Ordinal))
                .Where(static f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (CoreName().IsMatch(lines[i]))
                    {
                        problems.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        problems.ShouldBeEmpty("a front end reaches Core through Application or Contracts");
    }

    [GeneratedRegex(@"\bHeapScope\.Core\b|(?<![\w.])Core\.[A-Z]")]
    private static partial Regex CoreName();
}
