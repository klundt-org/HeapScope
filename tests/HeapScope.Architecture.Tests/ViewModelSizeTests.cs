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

namespace HeapScope.Architecture.Tests;

/// <summary>
/// A view model file stays under <see cref="Cap"/> lines (DEVELOPER.md 4): a class that grows past it is split along its parts,
/// into classes of their own or partial files by part (CompareViewModel.Links.cs). <see cref="s_exceptions"/> is a ratchet: a
/// listed file may not grow, and one that drops under the cap leaves the list.
/// </summary>
public class ViewModelSizeTests
{
    private const int Cap = 600;

    // Files allowed over the cap, with their size when listed. Empty since the split of the large view models.
    private static readonly Dictionary<string, int> s_exceptions = new(StringComparer.Ordinal);

    [Fact]
    public void No_view_model_file_is_over_the_cap()
    {
        var problems = new List<string>();
        foreach (var (file, lines) in Sizes())
        {
            var limit = s_exceptions.GetValueOrDefault(file, Cap);
            if (lines > limit)
            {
                problems.Add($"{file} has {lines} lines (allowed {limit}): split it along its parts");
            }
        }

        foreach (var (file, limit) in s_exceptions)
        {
            var lines = Sizes().GetValueOrDefault(file);
            if (lines <= Cap)
            {
                problems.Add($"{file} has {lines} lines now: take it off the exceptions");
            }
            else if (lines < limit)
            {
                problems.Add($"{file} has {lines} lines: lower its exception to {lines}");
            }
        }

        problems.ShouldBeEmpty();
    }

    private static Dictionary<string, int> Sizes()
    {
        var root = Path.Combine(RepoPaths.Root, "src", "HeapScope.UI.ViewModels");
        var sizes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(RepoPaths.Root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (!relative.Contains("/bin/", StringComparison.Ordinal) && !relative.Contains("/obj/", StringComparison.Ordinal))
            {
                sizes[relative] = File.ReadLines(file).Count();
            }
        }

        return sizes;
    }
}
