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

/// <summary>The system's own memory numbers on every system, not the last collection's (peer review 4.5 H7, sprint 4.7).</summary>
public sealed class SystemMemoryTests
{
    private const string MemInfo = "MemTotal:       16303136 kB\nMemFree:         1048576 kB\nMemAvailable:    8151568 kB\nBuffers:          123456 kB\n";

    [Fact]
    public void Linux_reads_total_and_available_from_meminfo()
    {
        var memory = SystemMemory.FromLinux(MemInfo, limit: null, usage: null).ShouldNotBeNull();

        memory.TotalBytes.ShouldBe(16303136L * 1024);
        memory.AvailableBytes.ShouldBe(8151568L * 1024);
        memory.Source.ShouldBe("/proc/meminfo");
    }

    [Fact]
    public void A_cgroup_limit_below_the_machine_is_the_total_and_its_use_counts()
    {
        var memory = SystemMemory.FromLinux(MemInfo, limit: 4L << 30, usage: 3L << 30).ShouldNotBeNull();

        memory.TotalBytes.ShouldBe(4L << 30);
        memory.AvailableBytes.ShouldBe(1L << 30);
        memory.Source.ShouldContain("cgroup");
        SystemMemory.FromLinux(MemInfo, limit: 64L << 30, usage: 1).ShouldNotBeNull().TotalBytes.ShouldBe(16303136L * 1024, "a limit above the machine's is no limit");
    }

    [Fact]
    public void Meminfo_without_a_total_is_not_read_and_one_without_available_falls_back_to_free()
    {
        SystemMemory.FromLinux("MemFree: 1 kB\n", null, null).ShouldBeNull();
        SystemMemory.FromLinux("MemTotal: 2048 kB\nMemFree: 1024 kB\n", null, null).ShouldNotBeNull().AvailableBytes.ShouldBe(1024L * 1024);
    }

    [Theory]
    [InlineData("0::/user.slice/app.scope\n", "/sys/fs/cgroup/user.slice/app.scope/memory.max", "/sys/fs/cgroup/user.slice/app.scope/memory.current")]
    [InlineData("0::/\n", "/sys/fs/cgroup/memory.max", "/sys/fs/cgroup/memory.current")]
    [InlineData("12:cpu,cpuacct:/docker/abc\n4:memory:/docker/abc\n", "/sys/fs/cgroup/memory/docker/abc/memory.limit_in_bytes", "/sys/fs/cgroup/memory/docker/abc/memory.usage_in_bytes")]
    public void The_cgroup_files_are_found_for_v2_and_v1(string selfCgroup, string limit, string usage) =>
        SystemMemory.CgroupFiles(selfCgroup).ShouldBe((limit, usage));

    [Fact]
    public void No_cgroup_and_no_limit_are_none()
    {
        SystemMemory.CgroupFiles(string.Empty).ShouldBe((null, null));
        SystemMemory.CgroupValue("max\n").ShouldBeNull();
        SystemMemory.CgroupValue("1073741824\n").ShouldBe(1L << 30);
    }

    // On Windows and Linux the system says it (CI runs both), so a worker that has not collected yet still has a budget.
    [Fact]
    public void The_current_reading_comes_from_the_system_and_is_not_zero()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), "macOS is built, not run (no macOS runners).");
        var memory = SystemMemory.Current();

        memory.Source.ShouldNotBe("the last garbage collection");
        memory.TotalBytes.ShouldBeGreaterThan(0);
        memory.AvailableBytes.ShouldBeGreaterThan(0);
        memory.AvailableBytes.ShouldBeLessThanOrEqualTo(memory.TotalBytes);
        ProcessMemory.PrivateBytes().ShouldBeGreaterThan(0);
    }
}
