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

using HeapScope.Application.Snapshots;

namespace HeapScope.Application.Tests;

public sealed class SnapshotMemoryEstimateTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private const long Mb = 1024L * 1024;

    // load-dump's first load peaks (budgets.md): dump MB, peak MB. Sprint 1.2 and 3.10 before the references' file (Small, Medium,
    // DenseHeap), sprint 4.7 after it (DenseHeap 2 GB and 8 GB).
    [Theory]
    [InlineData(568, 227)]
    [InlineData(4_022, 2_395)]
    [InlineData(3_984, 2_470)]
    [InlineData(4_440, 2_636)]
    [InlineData(4_017, 2_364)]
    [InlineData(3_145, 1_161)]
    [InlineData(9_054, 3_569)]
    public void Estimate_covers_the_measured_first_load_peaks(long dumpMb, long peakMb)
    {
        SnapshotMemoryEstimate.FirstLoadPeakBytes(dumpMb * Mb).ShouldBeGreaterThan(peakMb * Mb);
    }

    [Fact]
    public void Estimate_of_a_large_dump_is_not_far_above_its_peak()
    {
        // The worker reserves it while it loads: the 8 GB DenseHeap dump's 3,569 MB within a third.
        SnapshotMemoryEstimate.FirstLoadPeakBytes(9_054 * Mb).ShouldBeLessThan(3_569 * Mb * 4 / 3 + (100 * Mb));
    }

    [Fact]
    public void No_warning_when_the_estimate_fits_in_available_memory()
    {
        SnapshotMemoryEstimate.Warn(4 * Gb, new PhysicalMemory(32 * Gb, 20 * Gb)).ShouldBeNull();
    }

    [Fact]
    public void Warning_names_the_need_and_what_is_available()
    {
        var warning = SnapshotMemoryEstimate.Warn(8 * Gb, new PhysicalMemory(16 * Gb, 3 * Gb));

        warning.ShouldNotBeNull();
        warning.ShouldContain("about 4.3 GB");
        warning.ShouldContain("only 3.0 GB of 16.0 GB");
    }

    [Fact]
    public void Live_physical_memory_is_plausible()
    {
        var memory = PhysicalMemory.Current();

        memory.TotalBytes.ShouldBeGreaterThan(Gb);
        memory.AvailableBytes.ShouldBeInRange(1, memory.TotalBytes);
    }
}
