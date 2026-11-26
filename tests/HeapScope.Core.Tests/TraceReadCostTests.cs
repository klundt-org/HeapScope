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

namespace HeapScope.Core.Tests;

/// <summary>A recording's trace is one whose read fits beside two snapshots (peer review 4.5 H8, sprint 4.7).</summary>
public sealed class TraceReadCostTests
{
    private const long GB = 1L << 30;

    [Fact]
    public void The_default_limit_reads_within_a_quarter_of_the_16_GB_baseline()
    {
        const long limit = 512L << 20;
        TraceReadCost.Reservation(limit).ShouldBeLessThanOrEqualTo((long)(16 * GB * TraceReadCost.ShareOfMemory));
        TraceReadCost.Fit(limit, 16 * GB).ShouldBe(limit);
    }

    [Fact]
    public void A_limit_over_a_small_machine_is_lowered_to_one_that_fits_but_never_below_the_least()
    {
        var fitted = TraceReadCost.Fit(1 * GB, 4 * GB);
        fitted.ShouldBeLessThan(1 * GB);
        TraceReadCost.Reservation(fitted).ShouldBeLessThanOrEqualTo(1 * GB);
        TraceReadCost.Fit(1 * GB, 256L << 20).ShouldBe(TraceReadCost.LeastLimitBytes);
        TraceReadCost.Fit(1 * GB, 0).ShouldBe(1 * GB, "a machine whose memory cannot be read keeps the limit asked");
    }
}
