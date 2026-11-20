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

using HeapScope.Core;

namespace HeapScope.Capture.Tests;

/// <summary>Sprint 4.1 task 5: why Linux refuses a capture, and the words for more rights on each system.</summary>
public sealed class LinuxCaptureRulesTests
{
    private const string OwnTemp = "/tmp/";

    [Fact]
    public void A_dump_the_runtime_could_not_write_is_explained_by_the_ptrace_rule_or_the_container()
    {
        LinuxCaptureRules.WriteFailureReason(3, inContainer: false).ShouldNotBeNull().ShouldContain("kernel.yama.ptrace_scope is 3");
        LinuxCaptureRules.WriteFailureReason(2, inContainer: true).ShouldNotBeNull().ShouldContain("sudo sysctl kernel.yama.ptrace_scope=1");
        LinuxCaptureRules.WriteFailureReason(1, inContainer: true).ShouldNotBeNull().ShouldContain("--cap-add=SYS_PTRACE");
        LinuxCaptureRules.WriteFailureReason(null, inContainer: true).ShouldNotBeNull().ShouldContain("--cap-add=SYS_PTRACE");
        LinuxCaptureRules.WriteFailureReason(1, inContainer: false).ShouldBeNull();
        LinuxCaptureRules.WriteFailureReason(0, inContainer: false).ShouldBeNull();
    }

    [Fact]
    public void Another_users_process_says_to_run_as_that_user_or_root()
    {
        LinuxCaptureRules.WhyNoDiagnostics(4321, running: true, environ: null, maps: null, OwnTemp).ShouldNotBeNull()
            .ShouldStartWith("Process 4321 runs as another user");
    }

    [Theory]
    [InlineData("DOTNET_EnableDiagnostics")]
    [InlineData("COMPlus_EnableDiagnostics")]
    [InlineData("DOTNET_EnableDiagnostics_IPC")]
    public void A_process_that_turned_diagnostics_off_says_which_setting(string name)
    {
        var environ = $"HOME=/home/ray\0{name}=0\0";

        LinuxCaptureRules.WhyNoDiagnostics(4321, running: true, environ, maps: null, OwnTemp).ShouldBe(
            $"Process 4321 turned its diagnostics off ({name}=0), so no tool can capture it. Start it without that setting, then capture it again.");
    }

    [Fact]
    public void A_socket_in_another_temp_folder_says_where_and_how_to_look_there()
    {
        var environ = "HOME=/home/ray\0TMPDIR=/run/user/1000/app/\0";

        LinuxCaptureRules.WhyNoDiagnostics(4321, running: true, environ, maps: null, OwnTemp).ShouldBe(
            "Process 4321 keeps its diagnostics socket in /run/user/1000/app (its TMPDIR), and HeapScope looks in /tmp. Start HeapScope with TMPDIR=/run/user/1000/app, then capture it again.");
        LinuxCaptureRules.WhyNoDiagnostics(4321, running: true, "HOME=/home/ray\0", maps: null, "/var/tmp/").ShouldNotBeNull()
            .ShouldContain("keeps its diagnostics socket in /tmp (its TMPDIR)");
    }

    [Fact]
    public void A_process_without_a_runtime_is_not_a_dotnet_process_and_an_exited_one_has_no_reason()
    {
        const string Maps = "5610-5620 r-xp 00000000 08:01 7 /usr/bin/python3.12\n";

        LinuxCaptureRules.WhyNoDiagnostics(4321, running: true, "TMPDIR=/tmp\0", Maps, OwnTemp).ShouldBe("Process 4321 is not a .NET process: no .NET runtime is loaded in it.");
        LinuxCaptureRules.WhyNoDiagnostics(4321, running: true, "TMPDIR=/tmp\0", maps: null, OwnTemp).ShouldBeNull();
        LinuxCaptureRules.WhyNoDiagnostics(4321, running: false, environ: null, maps: null, OwnTemp).ShouldBeNull();
    }

    [Fact]
    public void Each_system_names_the_rights_it_needs()
    {
        ElevationTexts.NeedsMoreOn(linux: false).ShouldBe("Needs administrator");
        ElevationTexts.NeedsMoreOn(linux: true).ShouldBe("Needs root or its user");
        ElevationTexts.HintOn(linux: true).ShouldContain("'Needs root or its user'");
        ElevationTexts.HintOn(linux: false).ShouldContain("'Needs administrator'");
        ElevationTexts.Refusal("app", 7, "dump it", "capture again", linux: true).ShouldBe(
            "app (pid 7) runs as another user, so HeapScope cannot dump it. Run HeapScope as that user, or as root (sudo), and capture again.");
        ElevationTexts.Refusal(null, 7, "dump it", "capture again", linux: false).ShouldBe(
            "The process (pid 7) runs elevated or as another user, so HeapScope cannot dump it. Run HeapScope as administrator (right-click, Run as administrator) and capture again.");
        ElevationTexts.NeedsMore.ShouldBe(ElevationTexts.NeedsMoreOn(OperatingSystem.IsLinux()));
    }
}
