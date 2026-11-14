// HeapScope
// Copyright (C) 2026 Ray Klundt
// https://github.com/rklundt/HeapScope
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

namespace HeapScope.Cli.Tests;

/// <summary>
/// The command line as the log gets it (sprint 3.12, B1): a filter's text follows the user's setting whatever it looks like
/// (a number was logged as given before), and other text values are written by their length.
/// </summary>
public sealed class CommandLineLogTests
{
    private static System.CommandLine.ParseResult Parse(params string[] args) =>
        CliCommands.Build(() => throw new InvalidOperationException("no host")).Parse(args);

    [Fact]
    public void A_filter_is_written_as_typed_while_the_setting_allows_it()
    {
        var line = CliCommands.DescribeTokens(Parse("diff", "a.dmp", "b.dmp", "--filter", "Order \"x\""), includeFilterText: true);

        line.ShouldContain("--filter \"Order \\\"x\\\"\"");
    }

    [Fact]
    public void A_filter_is_written_by_its_length_when_the_user_turned_that_off_even_when_it_looks_like_a_number()
    {
        var line = CliCommands.DescribeTokens(Parse("diff", "a.dmp", "b.dmp", "--filter", "4111111111111111", "--top", "5"), includeFilterText: false);

        line.ShouldContain("--filter (16 chars)");
        line.ShouldNotContain("4111");
        line.ShouldContain("--top 5", customMessage: "a number of another option is written as given");
    }
}
