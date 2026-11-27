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

namespace HeapScope.Cli.Tests;

/// <summary>
/// Sprint 4.8 (peer review 4.5 M4; docs/cli-output.md): every command's JSON is one object whose first member is
/// <c>"Format": 1</c>. The integration tier's <c>CliJsonContractTests</c> checks each command's members on a real dump.
/// </summary>
public sealed class JsonFormatTests
{
    private static async Task<JsonElement> WriteAsync(object document, string? listName = null)
    {
        using var output = new StringWriter();
        await (listName is null ? CliCommands.WriteJsonAsync(output, document) : CliCommands.WriteJsonAsync(output, document, listName));
        using var parsed = JsonDocument.Parse(output.ToString());
        return parsed.RootElement.Clone();
    }

    [Fact]
    public async Task An_object_gets_the_format_as_its_first_member_and_keeps_its_own_in_order()
    {
        var root = await WriteAsync(new { TotalObjects = 3, Rows = new[] { new { Name = "A" } } });

        root.EnumerateObject().Select(static p => p.Name).ShouldBe(["Format", "TotalObjects", "Rows"]);
        root.GetProperty("Format").GetInt32().ShouldBe(CliCommands.JsonFormat);
        CliCommands.JsonFormat.ShouldBe(1);
    }

    [Fact]
    public async Task A_list_becomes_the_named_member_of_an_object()
    {
        var root = await WriteAsync(new[] { new { Name = "loh" }, new { Name = "finalizable" } }, "Inspections");

        root.EnumerateObject().Select(static p => p.Name).ShouldBe(["Format", "Inspections"]);
        root.GetProperty("Inspections").GetArrayLength().ShouldBe(2);
    }
}
