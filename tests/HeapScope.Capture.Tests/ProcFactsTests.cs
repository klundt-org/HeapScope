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

namespace HeapScope.Capture.Tests;

/// <summary>Sprint 4.1 task 2: a process's facts on Linux, parsed from recorded <c>/proc</c> text (on every system).</summary>
public sealed class ProcFactsTests
{
    // Recorded from an Ubuntu 24.04 machine (pids and addresses as they were; nothing else changed).
    private const string SharedFrameworkMaps = """
        5610a8d2b000-5610a8d33000 r--p 00000000 08:01 1835211                    /usr/share/dotnet/dotnet
        5610a8d33000-5610a8d3e000 r-xp 00008000 08:01 1835211                    /usr/share/dotnet/dotnet
        7f2c4a400000-7f2c4a4a1000 r--p 00000000 08:01 1836002                    /usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.1/libcoreclr.so
        7f2c4a4a1000-7f2c4a8b3000 r-xp 000a1000 08:01 1836002                    /usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.1/libcoreclr.so
        7f2c4b000000-7f2c4b021000 rw-p 00000000 00:00 0
        7ffd3c8e0000-7ffd3c901000 rw-p 00000000 00:00 0                          [stack]
        """;

    [Fact]
    public void A_command_line_joins_its_arguments_and_quotes_the_ones_with_spaces()
    {
        ProcText.CommandLine("dotnet\0/home/ray/My App/app.dll\0--urls\0http://localhost:5100\0\0").ShouldBe(
            "dotnet \"/home/ray/My App/app.dll\" --urls http://localhost:5100 \"\"");
        ProcText.CommandLine("/opt/svc/OrdersWorker\0").ShouldBe("/opt/svc/OrdersWorker");
        ProcText.CommandLine("say\0\"hi there\"\0").ShouldBe("say \"\\\"hi there\\\"\"");
    }

    [Fact]
    public void A_process_without_a_command_line_has_none()
    {
        ProcText.CommandLine(string.Empty).ShouldBeNull();
        ProcText.CommandLine("\0").ShouldBe("\"\"");
    }

    [Fact]
    public void A_deleted_executable_keeps_the_path_it_ran_from()
    {
        ProcText.ExecutablePath("/opt/svc/OrdersWorker (deleted)").ShouldBe("/opt/svc/OrdersWorker");
        ProcText.ExecutablePath("/usr/share/dotnet/dotnet").ShouldBe("/usr/share/dotnet/dotnet");
        ProcText.ExecutablePath(null).ShouldBeNull();
        ProcText.ExecutablePath(string.Empty).ShouldBeNull();
    }

    [Fact]
    public void The_runtime_of_a_shared_framework_app_names_its_folder_and_version()
    {
        var runtime = ProcText.Runtime(SharedFrameworkMaps).ShouldNotBeNull();

        runtime.Version.ShouldBe("10.0.1");
        runtime.Directory.ShouldBe("/usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.1");
    }

    [Fact]
    public void A_preview_runtime_keeps_its_whole_version_name()
    {
        var maps = "7f00-7f10 r-xp 00000000 08:01 12 /home/ray/.dotnet/shared/Microsoft.NETCore.App/11.0.0-preview.1.26101.3/libcoreclr.so\n";

        ProcText.Runtime(maps).ShouldNotBeNull().Version.ShouldBe("11.0.0-preview.1.26101.3");
    }

    [Fact]
    public void A_self_contained_app_names_its_runtime_folder_but_no_version()
    {
        var maps = "7f00-7f10 r-xp 00000000 08:01 12 /opt/My Service/libcoreclr.so\n";

        var runtime = ProcText.Runtime(maps).ShouldNotBeNull();

        runtime.Version.ShouldBeNull();
        runtime.Directory.ShouldBe("/opt/My Service");
    }

    [Fact]
    public void A_single_file_app_without_libcoreclr_has_no_runtime_and_a_replaced_runtime_is_still_found()
    {
        ProcText.Runtime("5610-5620 r-xp 00000000 08:01 7 /opt/svc/OrdersWorker\n7ffd-7ffe rw-p 00000000 00:00 0 [stack]\n").ShouldBeNull();
        ProcText.Runtime(string.Empty).ShouldBeNull();

        var deleted = "7f00-7f10 r-xp 00000000 08:01 12 /usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.1/libcoreclr.so (deleted)\n";
        ProcText.Runtime(deleted).ShouldNotBeNull().Version.ShouldBe("10.0.1");
    }

    [Fact]
    public void A_maps_line_keeps_a_path_with_spaces_and_has_none_for_an_anonymous_mapping()
    {
        ProcText.MappedPath("7f00-7f10 r--p 00000000 08:01 12      /opt/My Service/app.dll\r").ShouldBe("/opt/My Service/app.dll");
        ProcText.MappedPath("7f00-7f10 rw-p 00000000 00:00 0").ShouldBeNull();
        ProcText.MappedPath("7ffd-7ffe rw-p 00000000 00:00 0 [heap]").ShouldBeNull();
    }

    [Fact]
    public void The_dump_size_estimate_sums_the_readable_mappings_and_leaves_reserved_space_out()
    {
        // Sprint 4.1 task 3: 0x8000 + 0xb000 + 0xa1000 + 0x412000 + 0x21000 + 0x21000 readable; the GC's reserved range is not.
        var maps = SharedFrameworkMaps + "\n7f2c50000000-7f2d50000000 ---p 00000000 00:00 0\nnot a mapping line\n";

        ProcText.ReadableBytes(maps).ShouldBe(5_275_648);
        ProcText.ReadableBytes(string.Empty).ShouldBe(0);
    }

    [Fact]
    public void Free_space_of_the_temp_folder_is_known_on_every_system()
    {
        VolumeDiskSpace.Instance.AvailableBytes(Path.GetTempPath()).ShouldNotBeNull().ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_runtime_library_carries_its_file_version_as_clrmd_reads_it()
    {
        var library = System.Text.Encoding.ASCII.GetBytes("ELF....junk@(#)Version 10.0.125.57005 @Commit: 1a2b3c\0more");

        ProcText.EmbeddedVersion(library).ShouldBe("10.0.125.57005");
        ProcText.EmbeddedVersion("no version here"u8).ShouldBeNull();
        ProcText.EmbeddedVersion("@(#)Version not.a.number"u8).ShouldBeNull();
    }

    [Fact]
    public void The_owner_is_the_effective_user_id()
    {
        const string Status = "Name:\tdotnet\nUmask:\t0022\nState:\tS (sleeping)\nUid:\t1000\t0\t0\t0\nGid:\t1000\t1000\t1000\t1000\n";

        ProcText.EffectiveUserId(Status).ShouldBe(0);
        ProcText.EffectiveUserId("Uid:\t1000\t1000\t1000\t1000\n").ShouldBe(1000);
        ProcText.EffectiveUserId("Name:\tdotnet\n").ShouldBeNull();
    }

    [Fact]
    public void On_linux_the_test_process_reads_its_own_facts()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Reads /proc, which Linux has.");
        var facts = LinuxProcessFacts.Instance;
        using var self = System.Diagnostics.Process.GetCurrentProcess();

        facts.ImagePath(Environment.ProcessId).ShouldBe(Environment.ProcessPath);
        facts.CommandLine(Environment.ProcessId).ShouldNotBeNullOrEmpty();
        var runtime = facts.Runtime(self).ShouldNotBeNull();
        runtime.Directory.ShouldBe(Path.TrimEndingDirectorySeparator(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()));
        // The file version embedded in libcoreclr.so, as on Windows: 10.0.125.57005 for the 10.0.1 runtime.
        runtime.Version.ShouldNotBeNull().ShouldStartWith($"{Environment.Version.Major}.{Environment.Version.Minor}.");
        Version.Parse(runtime.Version).Revision.ShouldBeGreaterThanOrEqualTo(0);
        LinuxProcessFacts.Instance.EffectiveUserId(Environment.ProcessId).ShouldNotBeNull();
    }

    [Fact]
    public void The_facts_are_chosen_for_this_system()
    {
        CapturePlatform.ProcessFacts().ShouldBeOfType(OperatingSystem.IsLinux() ? typeof(LinuxProcessFacts) : typeof(WindowsProcessFacts));
    }
}
