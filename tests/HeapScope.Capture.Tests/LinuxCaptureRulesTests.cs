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

    private const string NetMaps = "7f00-7f10 r-xp 00000000 08:01 12 /usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.1/libcoreclr.so\n";

    private const string PythonMaps = "5610-5620 r-xp 00000000 08:01 7 /usr/bin/python3.12\n";

    /// <summary>A running process of HeapScope's own user (1000), with what <c>/proc</c> held for it.</summary>
    private static LinuxTarget Target(
        string? environ = "TMPDIR=/tmp\0",
        string? maps = NetMaps,
        int? user = 1000,
        int? own = 1000,
        bool running = true,
        bool? sameMounts = true,
        bool? carriesRuntime = null,
        string ownTemp = OwnTemp) =>
        new(4321, running, user, own, environ, maps, sameMounts, carriesRuntime, ownTemp);

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
    public void Another_users_process_is_told_by_its_user_id_not_by_an_unreadable_environment()
    {
        // Review M1: the ids decide; an environment that cannot be read alone says nothing (exited, or not dumpable).
        LinuxCaptureRules.WhyNoDiagnostics(Target(environ: null, maps: null, user: 1001)).ShouldNotBeNull()
            .ShouldStartWith("Process 4321 runs as another user");
        LinuxCaptureRules.WhyNoDiagnostics(Target(environ: null, maps: null)).ShouldBeNull("the same user's process, unreadable: not dumpable");
        LinuxCaptureRules.WhyNoDiagnostics(Target(user: null)).ShouldBeNull("its status could not be read: it exited");
        LinuxCaptureRules.WhyNoDiagnostics(Target(running: false)).ShouldBeNull();
    }

    [Fact]
    public void As_root_another_users_process_is_judged_by_the_other_checks()
    {
        LinuxCaptureRules.WhyNoDiagnostics(Target(user: 1000, own: 0, environ: "TMPDIR=/run/app\0")).ShouldNotBeNull()
            .ShouldContain("keeps its diagnostics socket in /run/app");
        LinuxCaptureRules.WhyNoDiagnostics(Target(user: 1000, own: 0)).ShouldBeNull();
    }

    [Theory]
    [InlineData("DOTNET_EnableDiagnostics")]
    [InlineData("COMPlus_EnableDiagnostics")]
    [InlineData("DOTNET_EnableDiagnostics_IPC")]
    public void A_process_that_turned_diagnostics_off_says_which_setting(string name)
    {
        LinuxCaptureRules.WhyNoDiagnostics(Target(environ: $"HOME=/home/ray\0{name}=0\0")).ShouldBe(
            $"Process 4321 turned its diagnostics off ({name}=0), so no tool can capture it. Start it without that setting, then capture it again.");
    }

    [Fact]
    public void A_process_with_its_own_tmp_says_so_before_comparing_temp_folders()
    {
        // Review H3: a service with PrivateTmp, or a container, names the same /tmp and sees another one.
        LinuxCaptureRules.WhyNoDiagnostics(Target(sameMounts: false)).ShouldNotBeNull()
            .ShouldStartWith("Process 4321 probably has its own /tmp (a service with PrivateTmp, or a container)");
        LinuxCaptureRules.WhyNoDiagnostics(Target(sameMounts: null)).ShouldBeNull("unknown is not claimed");
    }

    [Fact]
    public void A_socket_in_another_temp_folder_says_where_and_how_to_look_there()
    {
        LinuxCaptureRules.WhyNoDiagnostics(Target(environ: "HOME=/home/ray\0TMPDIR=/run/user/1000/app/\0")).ShouldBe(
            "Process 4321 keeps its diagnostics socket in /run/user/1000/app (its TMPDIR), and HeapScope looks in /tmp. Start HeapScope with TMPDIR=/run/user/1000/app, then capture it again.");
        LinuxCaptureRules.WhyNoDiagnostics(Target(environ: "HOME=/home/ray\0", ownTemp: "/var/tmp/")).ShouldNotBeNull()
            .ShouldContain("keeps its diagnostics socket in /tmp (its TMPDIR)");
    }

    [Fact]
    public void Only_a_process_without_a_runtime_anywhere_is_called_not_dotnet()
    {
        // Review H3: a single-file app links the runtime into its executable, so its maps name no libcoreclr.so.
        LinuxCaptureRules.WhyNoDiagnostics(Target(maps: PythonMaps, carriesRuntime: false)).ShouldBe("Process 4321 is not a .NET process: no .NET runtime is loaded in it.");
        LinuxCaptureRules.WhyNoDiagnostics(Target(maps: PythonMaps, carriesRuntime: true)).ShouldBeNull("a single-file app");
        LinuxCaptureRules.WhyNoDiagnostics(Target(maps: PythonMaps, carriesRuntime: null)).ShouldBeNull("its executable was not read");
        LinuxCaptureRules.WhyNoDiagnostics(Target()).ShouldBeNull("a .NET process whose socket is missing gets the generic text");
        LinuxCaptureRules.WhyNoDiagnostics(Target(maps: null)).ShouldBeNull();
    }

    [Fact]
    public void The_evidence_names_ids_folders_and_the_runtime_but_no_values()
    {
        var evidence = LinuxCaptureRules.Evidence(Target(environ: "SECRET=hunter2\0TMPDIR=/run/app\0DOTNET_EnableDiagnostics=0\0", user: 1001, sameMounts: false));

        evidence.ShouldBe(
            "Linux check of pid 4321: running yes; effective user 1001, HeapScope's 1000; environ readable; diagnostics off: DOTNET_EnableDiagnostics=0; "
            + "TMPDIR /run/app, HeapScope's /tmp; mount namespace another; runtime libcoreclr.so in /usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.1");
        evidence.ShouldNotContain("hunter2");
        LinuxCaptureRules.Evidence(Target(environ: null, maps: PythonMaps, carriesRuntime: true, sameMounts: null)).ShouldContain(
            "environ not readable; diagnostics off: unknown; TMPDIR unknown, HeapScope's /tmp; mount namespace unknown; runtime in its executable (single-file)");
    }

    [Theory]
    [InlineData("[createdump] Could not open dump file /root/.local/share/HeapScope/x.dmp.partial - Permission denied (13)", "the account app runs as cannot write to /data")]
    [InlineData("Writing dump failed (HRESULT: 0x80004005): No space left on device", "the disk is full")]
    [InlineData("Writing dump failed (HRESULT: 0x80070070)", "the disk is full")]
    public void A_write_failure_createdump_explains_is_named_before_any_system_setting(string message, string expected)
    {
        // Review H2: on a system with ptrace_scope 2, in a container, a full disk or an unwritable folder is still that.
        LinuxCaptureRules.WriteFailure(message, 2, inContainer: true, "app", "/data", out _).ShouldBe(expected);
    }

    [Fact]
    public void The_ptrace_rule_and_the_container_explain_only_a_ptrace_failure_or_scope_3()
    {
        const string Ptrace = "[createdump] Problem suspending threads: ptrace(ATTACH, 4321) FAILED Operation not permitted (1)";

        LinuxCaptureRules.WriteFailure(Ptrace, 2, inContainer: false, "app", "/data", out var rule).ShouldNotBeNull().ShouldContain("kernel.yama.ptrace_scope is 2");
        rule.ShouldBe("ptrace refused");
        LinuxCaptureRules.WriteFailure(Ptrace, 1, inContainer: true, "app", "/data", out _).ShouldNotBeNull().ShouldContain("--cap-add=SYS_PTRACE");
        LinuxCaptureRules.WriteFailure(Ptrace, 1, inContainer: false, "app", "/data", out rule).ShouldBeNull();
        rule.ShouldBe("ptrace refused, no setting explains it");
        LinuxCaptureRules.WriteFailure("Writing dump failed (HRESULT: 0x80004005)", 3, inContainer: false, "app", "/data", out rule).ShouldNotBeNull().ShouldContain("ptrace_scope is 3");
        rule.ShouldBe("ptrace_scope 3");
        LinuxCaptureRules.WriteFailure("Writing dump failed (HRESULT: 0x80004005)", 2, inContainer: true, "app", "/data", out rule).ShouldBeNull("nothing says ptrace");
        rule.ShouldBe("none matched");
        LinuxCaptureRules.WriteEvidence(null, inContainer: true, rule).ShouldBe("ptrace_scope not present; container yes; rule: none matched");
    }

    [Fact]
    public void As_root_another_users_full_dump_is_refused_for_the_library_only_on_linux()
    {
        // Review H1: the runtime writes the dump as the process's user, who cannot write root's folders.
        var process = new DotNetProcess(4321, "app", null, "10.0.1", ElevationState.Elevated, null, null, null);

        var refusal = DumpCapturer.LibraryRefusal(process, heapScopeElevated: true);

        if (OperatingSystem.IsLinux())
        {
            refusal.ShouldBe(LinuxCaptureRules.RootLibraryRefusal("app", 4321));
            refusal.ShouldStartWith("app (pid 4321) runs as another user, and the runtime writes its full dump as that user");
        }
        else
        {
            refusal.ShouldBeNull("on Windows an elevated HeapScope writes where it likes");
        }

        DumpCapturer.LibraryRefusal(process with { Elevation = ElevationState.NotElevated }, heapScopeElevated: true).ShouldBeNull();
        DumpCapturer.LibraryRefusal(process, heapScopeElevated: false).ShouldBeNull("the elevation refusal comes first then");
    }

    [Fact]
    public void A_marker_is_found_across_the_pieces_a_large_executable_is_read_in()
    {
        var marker = "@(#)Version "u8.ToArray();
        var file = new byte[(64 * 1024 * 3) + 100];
        marker.CopyTo(file, (64 * 1024) + marker.Length - 3); // straddles the first piece's end

        LinuxCaptureRules.Contains(new MemoryStream(file), marker).ShouldBeTrue();
        LinuxCaptureRules.Contains(new MemoryStream(new byte[200_000]), marker).ShouldBeFalse();
        LinuxCaptureRules.Contains(new MemoryStream(marker), marker).ShouldBeTrue();
    }

    [Fact]
    public void Only_a_regular_file_of_a_sane_size_from_a_target_is_read()
    {
        // Review H5: as root, a runtime folder named by another user's process is not trusted to hold files.
        var folder = Directory.CreateTempSubdirectory("heapscope-targetfiles-");
        try
        {
            var library = Path.Combine(folder.FullName, "libcoreclr.so");
            File.WriteAllBytes(library, new byte[4096]);
            var empty = Path.Combine(folder.FullName, "empty.so");
            File.WriteAllBytes(empty, []);

            TargetFiles.IsReadable(library).ShouldBeTrue();
            TargetFiles.IsReadable(empty).ShouldBeFalse("a pipe or a device reports a length of 0 too");
            TargetFiles.IsReadable(Path.Combine(folder.FullName, "missing.so")).ShouldBeFalse();
            TargetFiles.IsReadable(folder.FullName).ShouldBeFalse();
            if (OperatingSystem.IsLinux())
            {
                TargetFiles.IsReadable("/dev/zero").ShouldBeFalse();
                var link = Path.Combine(folder.FullName, "link.so");
                File.CreateSymbolicLink(link, "/dev/zero");
                TargetFiles.IsReadable(link).ShouldBeFalse();
            }
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void On_linux_pid_1_is_judged_without_reading_what_another_user_keeps()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && !Environment.IsPrivilegedProcess, "Reads /proc as a user other than root, as CI's Linux runner does.");

        var target = LinuxCaptureRules.Read(1);

        target.Running.ShouldBeTrue();
        target.UserId.ShouldBe(0);
        target.Environ.ShouldBeNull("another user's environment is not readable");
        LinuxCaptureRules.WhyNoDiagnostics(target).ShouldNotBeNull().ShouldStartWith("Process 1 runs as another user");
        LinuxCaptureRules.Evidence(target).ShouldStartWith("Linux check of pid 1: running yes; effective user 0");
    }

    [Fact]
    public void On_linux_an_exited_process_has_no_reason()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Reads /proc.");
        using var exited = System.Diagnostics.Process.Start("true")!;
        exited.WaitForExit();
        var pid = exited.Id;

        LinuxCaptureRules.WhyNoDiagnostics(LinuxCaptureRules.Read(pid)).ShouldBeNull();
        LinuxProcessFacts.Instance.ImagePath(pid).ShouldBeNull();
        LinuxProcessFacts.EffectiveUserId(pid).ShouldBeNull();
    }

    [Fact]
    public void Each_system_says_which_processes_it_hides_and_how_to_show_them()
    {
        // Sprint 4.1.1: the processes HeapScope cannot capture as it runs now are hidden unless the box is checked.
        ElevationTexts.ShowHiddenOn(linux: false).ShouldBe("Show elevated processes");
        ElevationTexts.ShowHiddenOn(linux: true).ShouldBe("Show other users' processes");
        ElevationTexts.HiddenNoteOn(1, linux: false).ShouldBe(
            "1 elevated process is hidden: HeapScope cannot capture it unless it runs as administrator. Check 'Show elevated processes' to list it.");
        ElevationTexts.HiddenNoteOn(1200, linux: false).ShouldStartWith("1,200 elevated processes are hidden: ");
        ElevationTexts.HiddenNoteOn(3, linux: true).ShouldBe(
            "3 processes of other users are hidden: HeapScope cannot capture them unless it runs as that user or as root (sudo). Check 'Show other users' processes' to list them.");
        ElevationTexts.HiddenNoteOn(1, linux: true).ShouldStartWith("1 process of another user is hidden: ");
        ElevationTexts.HiddenNoteOn(1, linux: true).ShouldContain(ElevationTexts.ShowHiddenOn(linux: true));
        ElevationTexts.ShowHiddenTipOn(linux: false).ShouldContain("only when it runs as administrator");
        ElevationTexts.ShowHidden.ShouldBe(ElevationTexts.ShowHiddenOn(OperatingSystem.IsLinux()));
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
