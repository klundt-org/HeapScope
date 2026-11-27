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
using HeapScope.Integration.Tests.Infrastructure;

namespace HeapScope.Integration.Tests;

/// <summary>
/// The command line's JSON output is a versioned contract (sprint 4.8, peer review 4.5 M4; docs/cli-output.md): each command's
/// object starts with <c>"Format": 1</c> and has the members the document lists for it, in that order. Run on the all-leaks
/// fixture dump; <c>allocations</c> is checked by <see cref="AllocationTraceTests"/>, which records a trace.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CliJsonContractTests : IClassFixture<AllLeaksDump>, IDisposable
{
    private const string Type = "Fixtures.Leaks.StaticCacheEntry";
    private readonly TempFolder _folder = new();
    private readonly string _dump;
    private readonly string _second;

    public CliJsonContractTests(AllLeaksDump shared)
    {
        _dump = _folder.File("contract.dmp");
        _second = _folder.File("contract-2.dmp");
        File.Copy(shared.DumpPath, _dump);
        File.Copy(shared.DumpPath, _second);
    }

    /// <summary>The members docs/cli-output.md lists for <paramref name="command"/> in format 1, in order.</summary>
    public static IReadOnlyList<string> Members(string command)
    {
        var rows = File.ReadAllLines(Path.Combine(global::HeapScope.Tests.Shared.RepoRoot.Path, "docs", "cli-output.md"))
            .SkipWhile(static l => l != "## Format 1")
            .Where(static l => l.StartsWith("| `", StringComparison.Ordinal))
            .Select(static l => l.Trim('|').Split(" | "))
            .ToDictionary(static c => c[0].Trim().Split('`')[1], static c => c[1].Trim(), StringComparer.Ordinal);
        var members = rows[command];
        return members.StartsWith("the members of `", StringComparison.Ordinal)
            ? Members(members.Split('`')[1])
            : members.Split(", ").Select(static m => m.Trim('`')).ToList();
    }

    /// <summary>The members of the object a command wrote to stdout, in order, after checking it is format 1.</summary>
    public static IReadOnlyList<string> Written(string standardOutput, out JsonElement root)
    {
        using var document = JsonDocument.Parse(standardOutput);
        root = document.RootElement.Clone();
        root.ValueKind.ShouldBe(JsonValueKind.Object, "every command writes one object");
        var first = root.EnumerateObject().First();
        first.Name.ShouldBe("Format");
        first.Value.GetInt32().ShouldBe(1);
        return root.EnumerateObject().Select(static p => p.Name).ToList();
    }

    [Fact]
    public async Task Each_command_writes_format_1_with_the_members_the_document_lists()
    {
        var (_, instances) = await JsonAsync("instances", _dump, "--type", Type);
        var address = instances.GetProperty("Rows")[0].GetProperty("Address").GetString().ShouldNotBeNull();
        address.ShouldStartWith("0x", Case.Sensitive, "an address is a string of hexadecimal digits");

        var runs = new (string Command, string[] Arguments)[]
        {
            ("summary", ["summary", _dump]),
            ("instances", ["instances", _dump, "--type", Type]),
            ("object", ["object", _dump, address]),
            ("inspect", ["inspect", _dump]),
            ("inspect <name>", ["inspect", _dump, "loh"]),
            ("paths --type", ["paths", _dump, "--type", Type]),
            ("paths --address", ["paths", _dump, "--address", address]),
            ("referrers --type", ["referrers", _dump, "--type", Type]),
            ("referrers --address", ["referrers", _dump, "--address", address]),
            ("retainers", ["retainers", _dump]),
            ("retained-by", ["retained-by", _dump, Type]),
            ("set", ["set", _dump, "--type", Type]),
            ("set --view paths", ["set", _dump, "--type", Type, "--view", "paths"]),
            ("diff", ["diff", _dump, _second]),
            ("trend", ["trend", _dump, _second]),
        };

        var problems = new List<string>();
        foreach (var (command, arguments) in runs)
        {
            var (members, _) = await JsonAsync(arguments);
            if (!members.SequenceEqual(Members(command)))
            {
                problems.Add($"{command}: writes {string.Join(", ", members)}");
            }
        }

        problems.ShouldBeEmpty("docs/cli-output.md lists other members; a change goes in both, and one that breaks a script is a new format");
    }

    private static async Task<(IReadOnlyList<string> Members, JsonElement Root)> JsonAsync(params string[] arguments)
    {
        using var cli = ChildProcess.Start(TestPaths.CliExe, [.. arguments, "--format", "json"]);
        (await cli.WaitForExitAsync(TimeSpan.FromSeconds(180))).ShouldBeTrue(cli.Output);
        cli.Process.ExitCode.ShouldBe(0, cli.Output);
        var members = Written(cli.StandardOutput, out var root);
        return (members, root);
    }

    public void Dispose() => _folder.Dispose();
}
