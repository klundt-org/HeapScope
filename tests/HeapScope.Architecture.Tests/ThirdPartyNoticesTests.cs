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

using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeapScope.Architecture.Tests;

/// <summary>
/// THIRD-PARTY-NOTICES.md lists the versions HeapScope ships (sprint 4.8, peer review 4.5 M2): every package the shipped
/// programs' lock files resolve (the app, the CLI, the worker and the sample) is in its table, or under a heading of its own, at
/// the version resolved; and wherever the file names a shipped package with a version, it is that version. An update that
/// changes a version fails here until the notices say so.
/// </summary>
public sealed partial class ThirdPartyNoticesTests
{
    private static readonly string[] s_shipped = ["UI", "Cli", "Worker", "Sample"];

    // Restored to build, never copied into a build: Avalonia's build tasks.
    private static readonly HashSet<string> s_buildOnly = new(StringComparer.Ordinal) { "Avalonia.BuildServices" };

    /// <summary>Each package the shipped programs resolve, with its versions (one, unless two programs differ).</summary>
    private static Dictionary<string, SortedSet<string>> Shipped()
    {
        var packages = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var program in s_shipped)
        {
            using var lockFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "src", $"HeapScope.{program}", "packages.lock.json")));
            foreach (var target in lockFile.RootElement.GetProperty("dependencies").EnumerateObject())
            {
                foreach (var package in target.Value.EnumerateObject())
                {
                    if (package.Value.GetProperty("type").GetString() is "Project" || s_buildOnly.Contains(package.Name))
                    {
                        continue;
                    }

                    var version = package.Value.GetProperty("resolved").GetString()!;
                    (packages.TryGetValue(package.Name, out var versions) ? versions : packages[package.Name] = new SortedSet<string>(StringComparer.Ordinal)).Add(version);
                }
            }
        }

        return packages;
    }

    private static string Notices() => File.ReadAllText(Path.Combine(RepoPaths.Root, "THIRD-PARTY-NOTICES.md")).ReplaceLineEndings("\n");

    /// <summary>The rows of the shipped table and the headings that name a package: what they name, and the versions they give.</summary>
    private static List<(string Names, string[] Versions)> Entries(string notices)
    {
        var entries = new List<(string, string[])>();
        var start = notices.IndexOf("## NuGet packages shipped with HeapScope", StringComparison.Ordinal);
        var end = notices.IndexOf("## Development and test only", StringComparison.Ordinal);
        foreach (var line in notices[start..end].Split('\n'))
        {
            if (line.StartsWith("| ", StringComparison.Ordinal) && !line.StartsWith("| Package", StringComparison.Ordinal))
            {
                var cells = line.Trim('|').Split(" | ");
                entries.Add((cells[0], cells[1].Split(", ").Select(static v => v.Trim()).ToArray()));
            }
            else if (HeadingWithPackage().Match(line) is { Success: true } heading)
            {
                entries.Add((heading.Groups["name"].Value, [heading.Groups["version"].Value]));
            }
        }

        return entries;
    }

    /// <summary>Whether a row's names cover <paramref name="package"/>: by name, by a <c>Prefix.*</c>, or as a package's native assets.</summary>
    private static bool Covers(string names, string package)
    {
        // "(via Avalonia)" says where a package comes from; it does not name Avalonia.
        foreach (var token in NameToken().Matches(ViaNote().Replace(names, string.Empty)).Select(static m => m.Value))
        {
            if (token == package
                || (token.EndsWith(".*", StringComparison.Ordinal) && package.StartsWith(token[..^1], StringComparison.Ordinal))
                || (names.Contains("native assets", StringComparison.Ordinal) && package.StartsWith(token + ".NativeAssets.", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void Notices_list_the_shipped_versions()
    {
        var notices = Notices();
        var entries = Entries(notices);
        var shipped = Shipped();
        var problems = new List<string>();

        shipped.Count.ShouldBeGreaterThan(50, "the lock files were read");
        foreach (var (package, versions) in shipped)
        {
            var rows = entries.Where(e => Covers(e.Names, package)).ToList();
            if (rows.Count == 0)
            {
                problems.Add($"{package} {string.Join(", ", versions)} is shipped and not listed");
                continue;
            }

            foreach (var version in versions.Where(v => !rows.Any(r => r.Versions.Contains(v))))
            {
                problems.Add($"{package} is shipped at {version}; the notices say {string.Join(" or ", rows.SelectMany(static r => r.Versions).Distinct())}");
            }
        }

        // A version in the table that no package it names ships is out of date too.
        foreach (var (names, versions) in entries)
        {
            var covered = shipped.Where(p => Covers(names, p.Key)).SelectMany(static p => p.Value).ToHashSet(StringComparer.Ordinal);
            problems.AddRange(versions.Where(v => !covered.Contains(v)).Select(v => $"\"{names}\" gives {v}, which nothing it names ships"));
        }

        problems.ShouldBeEmpty();
    }

    [Fact]
    public void Wherever_the_notices_name_a_shipped_package_with_a_version_it_is_the_one_shipped()
    {
        var notices = Notices();
        var problems = new List<string>();
        foreach (var (package, versions) in Shipped())
        {
            // "Avalonia.Fonts.Inter 12.1.5" in a sentence, "licenses/Avalonia.Fonts.Inter.12.1.5/" in a path.
            foreach (Match mention in Regex.Matches(notices, $@"(?<![\w.]){Regex.Escape(package)}[ .](?<version>\d+\.\d+\.\d+(\.\d+)?)\b"))
            {
                if (!versions.Contains(mention.Groups["version"].Value))
                {
                    problems.Add($"{mention.Value}: shipped at {string.Join(", ", versions)}");
                }
            }
        }

        problems.ShouldBeEmpty();
    }

    [GeneratedRegex(@"^### [^(]*\((?<name>[\w.]+), (?<version>\d[\w.]*)")]
    private static partial Regex HeadingWithPackage();

    [GeneratedRegex(@"(via [^)]*)")]
    private static partial Regex ViaNote();

    [GeneratedRegex(@"[A-Za-z][\w]*(\.[\w]+)*(\.\*)?")]
    private static partial Regex NameToken();
}
