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

using HeapScope.UI.ViewModels;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>Sprint 4.1.1: the one rule both process lists follow for the processes HeapScope cannot capture as it runs now.</summary>
public sealed class ProcessListFilterTests
{
    private static readonly ProcessRow s_own = new(10, "Api", "10.0.1", string.Empty, 1, "Api.exe", NeedsAdministrator: false);
    private static readonly ProcessRow s_other = new(20, "Service", "10.0.1", "Needs administrator", 1, "Service.exe", NeedsAdministrator: true);
    private static readonly ProcessRow s_third = new(30, "Worker", "10.0.1", "Needs administrator", 1, "Worker.exe", NeedsAdministrator: true);

    [Fact]
    public void A_process_that_needs_more_rights_is_hidden_until_shown_and_counted()
    {
        var filter = new ProcessListFilter();
        filter.Replace([s_own, s_other, s_third]);

        filter.Visible.ShouldBe([s_own]);
        filter.Hidden.ShouldBe([20, 30]);
        filter.AnyNeedMoreRights.ShouldBeTrue();
        filter.All.Count.ShouldBe(3);

        filter.ShowHidden = true;

        filter.Visible.ShouldBe([s_own, s_other, s_third], "in the listing's order");
        filter.Hidden.ShouldBeEmpty();
    }

    [Fact]
    public void Run_elevated_no_process_needs_more_so_nothing_is_hidden()
    {
        // ProcessEntry.NeedsAdministrator is false for every process when HeapScope runs elevated or as root.
        var filter = new ProcessListFilter();
        filter.Replace([s_own, s_other with { NeedsAdministrator = false, Elevation = "Elevated" }]);

        filter.Visible.Count.ShouldBe(2);
        filter.Hidden.ShouldBeEmpty();
        filter.AnyNeedMoreRights.ShouldBeFalse();
    }

    [Fact]
    public void An_updated_row_keeps_its_place_and_an_unknown_one_is_ignored()
    {
        var filter = new ProcessListFilter { ShowHidden = true };
        filter.Replace([s_own, s_other]);

        filter.Update(s_own with { IsSample = true });
        filter.Update(s_third);

        filter.All.ShouldBe([s_own with { IsSample = true }, s_other]);
    }
}
