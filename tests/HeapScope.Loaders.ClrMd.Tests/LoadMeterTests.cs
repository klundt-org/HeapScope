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

using HeapScope.Core.Loading;

namespace HeapScope.Loaders.ClrMd.Tests;

/// <summary>
/// The load meter's numbers (sprint 3.12 review M7): hard faults, the pages read from disk, not every page fault; a 32-bit
/// counter that wrapped read forward; and a log label that says what each number is.
/// </summary>
public sealed class LoadMeterTests
{
    [Fact]
    public void A_fault_count_that_wrapped_is_read_as_the_distance_forward()
    {
        LoadMeter.Delta(100, 250).ShouldBe(150);
        LoadMeter.Delta(uint.MaxValue - 9, 5).ShouldBe(15, "Windows counts in 32 bits");
        LoadMeter.Delta(null, 5).ShouldBeNull();
        LoadMeter.Delta(5, null).ShouldBeNull();
    }

    [Fact]
    public void Hard_faults_are_read_on_windows_and_linux()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            LoadMeter.HardFaults().ShouldBeNull();
            return;
        }

        var first = LoadMeter.HardFaults().ShouldNotBeNull();
        LoadMeter.HardFaults().ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(first);
    }

    [Fact]
    public void A_phase_names_its_numbers_as_the_process_s_and_its_faults_as_pages_read_from_disk()
    {
        var cost = new LoadPhaseCost("objects", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5.1), TimeSpan.FromSeconds(0.2), 1234);

        cost.ToString().ShouldBe("objects 5.0 s (process CPU 5.1 s, GC pause 0.2 s, 1,234 pages read from disk)");
        (cost with { HardFaults = null }).ToString().ShouldEndWith("pages read from disk not known)");
    }
}
