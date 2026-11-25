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


using HeapScope.Core.Loading;
using Microsoft.Diagnostics.Runtime;

namespace HeapScope.Loaders.ClrMd.Tests;

/// <summary>How the number of handle tables is found, and what is said of the walk (peer review 4.5). Real dumps: the integration tests' HandleTablesTests.</summary>
public sealed class HandleTablesTests
{
    private static readonly HandleTables.Attempt s_end = new(HandleTables.AttemptOutcome.End, HandleTables.Found.None);

    /// <summary>What is said of a walk: whole only when it ended where the tables do, past the processors.</summary>
    [Theory]
    [InlineData(24, 24, 5, false, HandleWalkOutcome.Recovered)]
    [InlineData(25, 24, 0, false, HandleWalkOutcome.Complete)]
    [InlineData(18, 24, 5, false, HandleWalkOutcome.Unknown)]
    [InlineData(25, null, 5, false, HandleWalkOutcome.Recovered)]
    [InlineData(25, null, 0, false, HandleWalkOutcome.Complete)]
    [InlineData(25, null, 5, true, HandleWalkOutcome.Unknown)]
    public void A_walk_that_stopped_before_the_last_table_is_not_complete(int tables, int? processors, int added, bool doubt, HandleWalkOutcome expected) =>
        HandleTables.Judge(tables, processors, added, doubt).ShouldBe(expected);

    /// <summary>The search on a made-up process of <paramref name="tables"/> tables, two handles in each past its 4 heaps.</summary>
    [Theory]
    [InlineData(24, 24)]
    [InlineData(24, null)]
    [InlineData(80, 48)]
    [InlineData(4, 4)]
    [InlineData(4, null)]
    [InlineData(1, null)]
    [InlineData(16, 24)]
    public void The_search_ends_at_the_last_table(int tables, int? processors)
    {
        var heaps = Math.Min(4, tables);
        var found = HandleTables.Search(heaps, processors, count => count <= tables ? Works((count - heaps) * 2) : s_end);

        (found.Best.LeftOut.Count, found.Tables, found.Doubt, found.Endless).ShouldBe(((tables - heaps) * 2, tables, false, false));
    }

    [Fact]
    public void A_walk_that_fails_otherwise_than_past_the_tables_leaves_a_doubt_and_says_why()
    {
        var found = HandleTables.Search(4, null, count => count <= 7 ? Works(count) : new(HandleTables.AttemptOutcome.Doubt, HandleTables.Found.None, $"at {count}: made up"));
        (found.Best.LeftOut.Count, found.Tables, found.Doubt, found.Why).ShouldBe((7, 7, true, "at 8: made up"));

        // A larger count with fewer handles is not a better one.
        found = HandleTables.Search(4, null, count => Works(count <= 7 ? count : 1));
        (found.Tables, found.Doubt, found.Why).ShouldBe((7, true, "at 8: 1 more handles, at 7: 7"));

        // A place that changes nothing is not the heap count.
        found = HandleTables.Search(4, null, _ => Works(0));
        (found.Doubt, found.Endless, found.Why).ShouldBe((true, true, (string?)null));
    }

    private static HandleTables.Attempt Works(int handles) =>
        new(HandleTables.AttemptOutcome.Works, new HandleTables.Found([.. Enumerable.Range(1, handles).Select(static i => new LeftOutHandle((ulong)i, ClrHandleKind.Strong, 0, 0, true))], 0, 0));
}
