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

using HeapScope.UI;

namespace HeapScope.UI.Tests;

public sealed class UiOptionsTests
{
    [Fact]
    public void Smoke_more_takes_several_dumps_separated_by_semicolons()
    {
        UiOptions.SplitPaths(@"D:\a.dmp; D:\b.dmp;;D:\c.dmp ").ShouldBe([@"D:\a.dmp", @"D:\b.dmp", @"D:\c.dmp"]);
        UiOptions.SplitPaths(null).ShouldBeEmpty();
        UiOptions.SplitPaths("  ").ShouldBeEmpty();
        new UiOptions(null, SmokeOpenPath: "a.dmp", SmokeMorePaths: ["b.dmp"]).IsSmokeRun.ShouldBeTrue();
    }

    [Fact]
    public void The_memory_line_says_what_budgets_md_reads()
    {
        // budgets.md's measurements read this line: the counts first, then the three sizes.
        Smoke.SmokeRunner.MemoryLine.ShouldStartWith("Smoke memory with {Open} snapshots open, {Tabs} tabs: ");
        Smoke.SmokeRunner.MemoryLine.ShouldContain("working set {WorkingSet:N0} MB, private {Private:N0} MB, managed heap {Heap:N0} MB after a full collection");
    }
}
