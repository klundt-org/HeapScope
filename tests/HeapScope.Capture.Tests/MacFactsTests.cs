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

using System.Buffers.Binary;
using System.Text;
using HeapScope.Core;

namespace HeapScope.Capture.Tests;

/// <summary>
/// Sprint 4.2: macOS facts and rules. The parsers run here on buffers built to XNU's published layouts; the calls themselves
/// run on macOS only, where nobody has run them.
/// </summary>
public sealed class MacFactsTests
{
    private const string Framework = "/usr/local/share/dotnet/shared/Microsoft.NETCore.App/10.0.1";

    private static byte[] Region(ulong address, ulong size, uint protection = 1, uint shareMode = 2, string? path = null)
    {
        var bytes = new byte[MacProcData.RegionSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, protection);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), shareMode);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(80), address);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(88), size);
        Encoding.UTF8.GetBytes(path ?? string.Empty).CopyTo(bytes, 248);
        return bytes;
    }

    private static byte[] ProcArgs(int count, string rest) =>
        [.. BitConverter.GetBytes(count), .. Encoding.UTF8.GetBytes(rest)];

    private static MacTarget Target(int? user = 501, int? own = 501, TargetEnvironment? environment = null, bool running = true, MacWalk? walk = null) =>
        new(42, running, user, "errno 3", own, environment, "errno 22", walk, "/var/folders/ab/T/");

    private static Func<ulong, (MacRegion? Region, string Outcome)> Reads(params MacRegion[] regions) =>
        address => regions.Where(r => r.Address + r.Size > address).Select(r => ((MacRegion?)r, string.Empty)).DefaultIfEmpty((null, "errno 22")).First();

    [Fact]
    public void The_user_id_is_read_from_a_whole_struct_only()
    {
        var info = new byte[MacProcData.ShortInfoSize];
        BinaryPrimitives.WriteInt32LittleEndian(info.AsSpan(36), 501);
        MacProcData.EffectiveUserId(info).ShouldBe(501);
        MacProcData.EffectiveUserId(info.AsSpan(0, 60)).ShouldBeNull();
        MacProcData.EffectiveUserId(new byte[72]).ShouldBeNull("another size is another layout");
        MacProcData.EffectiveUserId([]).ShouldBeNull();
    }

    [Fact]
    public void A_region_is_readable_when_it_can_be_read_and_is_not_an_empty_reservation()
    {
        MacProcData.Region(Region(0x1000, 0x2000, path: "/usr/lib/a b.dylib")).ShouldBe(new MacRegion(0x1000, 0x2000, true, "/usr/lib/a b.dylib"));
        MacProcData.Region(Region(0x1000, 0x2000, protection: 0))!.Value.Readable.ShouldBeFalse();
        MacProcData.Region(Region(0x1000, 0x2000, protection: 6))!.Value.Readable.ShouldBeFalse("write and execute without read");
        MacProcData.Region(Region(0x1000, 0x2000, shareMode: 3))!.Value.Readable.ShouldBeFalse();
        MacProcData.Region(Region(0x1000, 0x2000))!.Value.Path.ShouldBeNull();
        MacProcData.Region(Region(0x1000, 0x2000, path: new string('x', 1024)))!.Value.Path!.Length.ShouldBe(1024, "a path with no NUL ends with the struct");
        MacProcData.Region(new byte[MacProcData.RegionSize - 1]).ShouldBeNull();
    }

    [Fact]
    public void A_walk_follows_the_regions_and_says_how_it_ended()
    {
        var walk = MacProcData.Walk(Reads(new(0x1000, 0x1000, true, "/usr/lib/dyld"), new(0x8000, 0x1000, false, null), new(0x9000, 0x3000, true, null)));
        walk.ShouldBe(new MacWalk(3, 1, 0xC000, "errno 22", 0x4000, null));
        walk.ToString().ShouldBe("3 regions (1 with a file) to 0xC000, ended: errno 22; 16384 readable bytes; runtime not seen");

        MacProcData.Walk(_ => (null, "errno 1")).ShouldBe(new MacWalk(0, 0, 0, "errno 1", null, null), "not allowed is not zero bytes");
        MacProcData.Walk(_ => (new MacRegion(0, 0, true, null), string.Empty)).End.ShouldBe("a region did not move it on");
        MacProcData.Walk(a => (new MacRegion(a, ulong.MaxValue, true, null), string.Empty)).Regions.ShouldBeLessThan(3, "an end past the address space wraps");
    }

    [Fact]
    public void A_sum_no_process_has_is_not_a_size() =>
        MacProcData.Walk(Reads(new(0, 1UL << 45, true, null), new(1UL << 45, 1UL << 60, true, null))).ReadableBytes.ShouldBeNull();

    [Fact]
    public void The_runtime_is_the_folder_of_the_mapped_libcoreclr()
    {
        var regions = new MacRegion[] { new(1, 1, true, "/usr/lib/dyld"), new(2, 1, true, null), new(3, 1, true, Framework + "/libcoreclr.dylib"), new(4, 1, true, null) };
        MacProcData.Walk(Reads(regions)).Runtime.ShouldBe(new RuntimeFacts("10.0.1", Framework));
        var stopped = MacProcData.Walk(Reads(regions), stopAtRuntime: true);
        stopped.Regions.ShouldBe(3);
        stopped.End.ShouldBe("the runtime was found");
        MacProcData.Walk(Reads(new MacRegion(1, 1, true, "/Applications/Orders.app/libcoreclr.dylib"))).Runtime.ShouldBe(new RuntimeFacts(null, "/Applications/Orders.app"));
        MacProcData.Walk(Reads(new MacRegion(1, 1, true, "/opt/orders/OrdersWorker"))).Runtime.ShouldBeNull("a single-file app links the runtime in");
    }

    [Fact]
    public void The_footprint_is_read_from_a_whole_struct_and_zero_is_not_one()
    {
        var usage = new byte[MacProcData.UsageSize];
        MacProcData.PhysicalFootprint(usage).ShouldBeNull();
        BinaryPrimitives.WriteUInt64LittleEndian(usage.AsSpan(72), 123_456_789);
        MacProcData.PhysicalFootprint(usage).ShouldBe(123_456_789);
        MacProcData.PhysicalFootprint(usage.AsSpan(0, 88)).ShouldBeNull();
        BinaryPrimitives.WriteUInt64LittleEndian(usage.AsSpan(72), ulong.MaxValue);
        MacProcData.PhysicalFootprint(usage).ShouldBeNull();
    }

    [Theory]
    [InlineData(2, "/bin/a\0\0\0run\0SECRET=orders-key\0HOME=/Users/a\0")]
    [InlineData(3, "/bin/a\0\0a\0run\0SECRET=orders-key\0HOME=/Users/a\0")]
    public void An_environment_entry_taken_for_an_argument_never_reaches_the_command_line(int count, string rest)
    {
        // An empty first argument reads as padding, or the count is one too high: either moves the first entry in.
        MacProcData.CommandLine(ProcArgs(count, rest))!.ShouldNotContain("orders-key");
        MacProcData.Environment(ProcArgs(count, rest)).ShouldNotBeNull();
    }

    [Fact]
    public void A_setting_in_an_option_is_kept() =>
        MacProcData.CommandLine(ProcArgs(2, "/bin/a\0\0a\0--urls=http://localhost:5000\0HOME=/Users/a\0")).ShouldBe("a --urls=http://localhost:5000");

    [Fact]
    public void The_command_line_is_the_arguments_after_the_padded_path()
    {
        var bytes = ProcArgs(3, "/usr/local/share/dotnet/dotnet\0\0\0\0dotnet\0run me.dll\0--x\0HOME=/Users/a\0TMPDIR=/var/folders/ab/T/\0\0ptr_munge=\0");
        MacProcData.CommandLine(bytes).ShouldBe("dotnet \"run me.dll\" --x");
        MacProcData.CommandLine(ProcArgs(2, "/bin/a\0\0a\0\0")).ShouldBe("a \"\"", "an empty argument is kept");
        MacProcData.CommandLine(ProcArgs(0, "/bin/a\0")).ShouldBeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(4)]
    public void A_wrong_argument_count_gives_nothing(int count) =>
        MacProcData.CommandLine(ProcArgs(count, "/bin/a\0\0a\0b\0c\0")).ShouldBeNull();

    [Fact]
    public void A_cut_buffer_gives_nothing()
    {
        MacProcData.CommandLine([]).ShouldBeNull();
        MacProcData.CommandLine([1, 0]).ShouldBeNull();
        MacProcData.CommandLine(ProcArgs(1, "/bin/a")).ShouldBeNull();
        MacProcData.CommandLine(ProcArgs(1, "/bin/a\0\0a")).ShouldBeNull();
        MacProcData.Environment(ProcArgs(1, "/bin/a\0\0a")).ShouldBeNull();
    }

    [Fact]
    public void The_environment_gives_the_temp_folder_and_whether_diagnostics_are_off()
    {
        var environment = MacProcData.Environment(ProcArgs(1, "/bin/a\0\0a\0TMPDIR=/var/folders/ab/T/\0DOTNET_EnableDiagnostics=0\0\0TMPDIR=/not/this\0")).ShouldNotBeNull();
        environment.TempFolder.ShouldBe("/var/folders/ab/T");
        environment.DiagnosticsOff.ShouldBe("DOTNET_EnableDiagnostics");
        MacProcData.Environment(ProcArgs(1, "/bin/a\0\0a\0HOME=/Users/a\0")).ShouldNotBeNull().TempFolder.ShouldBe("/tmp");
        MacProcData.Environment(ProcArgs(1, "/bin/a\0\0a\0")).ShouldBeNull("macOS left the environment out");
    }

    [Fact]
    public void Off_macos_every_fact_is_empty_and_says_why()
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS(), "The calls exist there.");
        var facts = MacProcessFacts.Instance;
        var pid = Environment.ProcessId;
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        facts.ImagePath(pid).ShouldBeNull();
        facts.CommandLine(pid).ShouldBeNull();
        facts.EffectiveUserId(pid).ShouldBeNull();
        facts.ReadableBytes(pid).ShouldBeNull();
        facts.PrivateBytes(self).ShouldBeNull();
        facts.Runtime(self).ShouldBeNull();
        facts.UncountedBytes.ShouldBe(MacProcessFacts.DefaultSharedCacheBytes);
        MacCaptureRules.Instance.WhyNotFound(pid).Reason.ShouldBeNull();
        MacCaptureRules.Instance.WhyNotFound(pid).Evidence!.ShouldContain("running yes; effective user not readable (the call is not there)", Case.Sensitive, "a failed read is not an exited process");
        facts.SelfCheck().ShouldContain("user id the call is not there; ");
        facts.SelfCheck().ShouldEndWith("regions 0 regions (0 with a file) to 0x0, ended: the call is not there; readable bytes unknown; runtime not seen");
        CapturePlatform.SelfCheck().ShouldBeNull("Windows and Linux are tested");
    }

    [Fact]
    public void On_macos_the_test_process_has_its_facts()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Calls libproc and sysctl, which macOS has.");
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        var facts = MacProcessFacts.Instance;
        facts.ImagePath(self.Id).ShouldBe(Environment.ProcessPath);
        facts.CommandLine(self.Id).ShouldNotBeNullOrEmpty();
        facts.ReadableBytes(self.Id)!.Value.ShouldBeGreaterThan(16 * 1024 * 1024);
        facts.PrivateBytes(self)!.Value.ShouldBeGreaterThan(1024 * 1024);
        facts.UncountedBytes.ShouldBeGreaterThan(512L * 1024 * 1024);
        facts.Runtime(self)!.Directory.ShouldBe(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory().TrimEnd('/'));
        facts.Environment(self.Id, out var outcome).ShouldNotBeNull(outcome);
        facts.EffectiveUserId(self.Id).ShouldNotBeNull();
        facts.EffectiveUserId(1).ShouldBe(0, "launchd runs as root");
    }

    [Fact]
    public void Macos_has_its_own_facts_and_rules()
    {
        CapturePlatform.ProcessFacts(HostSystem.MacOS).ShouldBeOfType<MacProcessFacts>();
        CapturePlatform.Rules(HostSystem.MacOS).ShouldBeOfType<MacCaptureRules>();
        CapturePlatform.ElevationProbe(HostSystem.MacOS).ShouldBeOfType<UnixElevationProbe>();
        CapturePlatform.IsSupportedOn(HostSystem.MacOS).ShouldBeTrue();
        CapturePlatform.Rules(HostSystem.MacOS).LibraryRefusal("orders", 42)!.ShouldStartWith("orders (pid 42) runs as another user, and the runtime writes its full dump as that user");
    }

    [Fact]
    public void The_elevation_probe_compares_the_ids_the_systems_facts_give()
    {
        var probe = new UnixElevationProbe(new FakeFacts());
        probe.GetState(1).ShouldBe(ElevationState.NotElevated);
        probe.GetState(2).ShouldBe(ElevationState.Elevated);
        probe.GetState(3).ShouldBe(ElevationState.Unknown);
    }

    // The texts are .NET 10's macOS createdump's own (its osx-arm64 runtime pack).
    [Theory]
    [InlineData("Invalid process id: task_for_pid(42) FAILED (os/kern) failure (5)\nThis failure may be because createdump or the application is not properly signed and entitled.", "task_for_pid refused ('task_for_pid')", "macOS did not let the runtime examine the process, which can mean that the application or the runtime is not signed with the entitlements that allow it. A gcdump does not need this: capture one instead")]
    [InlineData("task_for_pid(42) FAILED: Permission denied", "task_for_pid refused ('task_for_pid')", "macOS did not let the runtime examine the process, which can mean that the application or the runtime is not signed with the entitlements that allow it. A gcdump does not need this: capture one instead")]
    [InlineData("Error writing data to dump file: No space left on device (28)", "no space left ('No space left')", "the disk is full")]
    [InlineData("failed 0x80070070", "no space left ('0x80070070')", "the disk is full")]
    [InlineData("Could not create output file '/d/a.dmp': Operation not permitted (1)", "macOS refused the write ('Operation not permitted')", "macOS did not let the account orders runs as write to /d. macOS protects some folders (Desktop, Documents, Downloads) from applications that were not allowed to use them")]
    [InlineData("Could not create output file '/d/a.dmp': Permission denied (13)", "the folder is not writable by the target's account ('Permission denied')", "the account orders runs as cannot write to /d")]
    [InlineData("Could not create output file '/d/a.dmp': No such file or directory (2)", "the folder is not writable by the target's account ('Could not create output file')", "the account orders runs as cannot write to /d")]
    [InlineData("failed 0x80070005", "the folder is not writable by the target's account ('0x80070005')", "the account orders runs as cannot write to /d")]
    [InlineData("mach_vm_region_recurse for address 0 FAILED", "none matched", null)]
    public void A_write_failure_is_named_from_what_createdump_reported(string message, string rule, string? reason)
    {
        MacCaptureRules.WriteFailure(message, "orders", "/d", out var decided).ShouldBe(reason);
        decided.ShouldBe(rule);
        MacCaptureRules.Instance.WhyNotWritten(message, "orders", "/d").ShouldBe(new WriteFailureExplanation(reason, "macOS; rule: " + rule));
    }

    [Fact]
    public void A_process_that_is_not_found_is_explained_where_macos_can_tell()
    {
        var here = new TargetEnvironment(["TMPDIR=/private/var/folders/ab/T/"]);
        MacCaptureRules.WhyNoDiagnostics(Target(running: false, user: null)).ShouldBeNull();
        MacCaptureRules.WhyNoDiagnostics(Target(user: 502)).ShouldBe(
            "Process 42 runs as another user, and HeapScope finds a process by the diagnostics socket it creates in its own temp folder, which on macOS differs per user. Run HeapScope as that user.");
        MacCaptureRules.WhyNoDiagnostics(Target(own: 0, environment: here)).ShouldBeNull("root reaches it in the same folder");
        MacCaptureRules.WhyNoDiagnostics(Target(environment: new TargetEnvironment(["COMPlus_EnableDiagnostics=0"]))).ShouldBe(
            "Process 42 turned its diagnostics off (COMPlus_EnableDiagnostics=0), so no tool can capture it. Start it without that setting, then capture it again.");
        MacCaptureRules.WhyNoDiagnostics(Target(own: 0, environment: new TargetEnvironment(["TMPDIR=/var/folders/zz/T/"]))).ShouldBe(
            "Process 42 keeps its diagnostics socket in /var/folders/zz/T (its TMPDIR), and HeapScope looks in /var/folders/ab/T. Start HeapScope with TMPDIR=/var/folders/zz/T, then capture it again.");
        MacCaptureRules.WhyNoDiagnostics(Target(environment: new TargetEnvironment(["HOME=/Users/a"])))!.ShouldContain("in /tmp (its TMPDIR is not set), and");
        MacCaptureRules.WhyNoDiagnostics(Target(environment: here)).ShouldBeNull("/var is a link into /private");
        MacCaptureRules.WhyNoDiagnostics(Target(environment: new TargetEnvironment(["TMPDIR=/VAR/folders/ab/T"]))).ShouldNotBeNull("only the links macOS has are resolved");
        MacCaptureRules.WhyNoDiagnostics(Target(environment: new TargetEnvironment(["TMPDIR=/var/folders/AB/T"]))).ShouldBeNull("case does not count on macOS");
        MacCaptureRules.WhyNoDiagnostics(Target(user: null, environment: new TargetEnvironment(["TMPDIR=/x"]))).ShouldNotBeNull("an unreadable user id hides no other reason");
        MacCaptureRules.WhyNoDiagnostics(Target()).ShouldBeNull();
        MacCaptureRules.WhyNoDiagnostics(Target(environment: here, walk: new MacWalk(1, 1, 5, "errno 22", 1, null))).ShouldBeNull("a walk without the runtime may have ended early, so nothing says it is not .NET");
    }

    [Fact]
    public void The_evidence_says_what_the_decision_rests_on_and_no_value_but_the_temp_folder()
    {
        MacCaptureRules.Evidence(Target(environment: new TargetEnvironment(["TMPDIR=/t", "SECRET=orders-key"]), walk: new MacWalk(9, 4, 0x2000, "errno 22", 4096, new RuntimeFacts(null, Framework)))).ShouldBe(
            $"macOS check of pid 42: running yes; effective user 501, HeapScope's 501; environment readable; diagnostics off: none; TMPDIR /t, HeapScope's /var/folders/ab/T; regions 9 regions (4 with a file) to 0x2000, ended: errno 22; 4096 readable bytes; runtime {Framework}");
        MacCaptureRules.Evidence(Target(user: null)).ShouldBe(
            "macOS check of pid 42: running yes; effective user not readable (errno 3), HeapScope's 501; environment not readable (errno 22); diagnostics off: unknown; TMPDIR unknown, HeapScope's /var/folders/ab/T; regions not read");
    }

    [Fact]
    public void The_estimate_takes_the_larger_figure_where_a_walk_may_end_early_and_adds_what_it_leaves_out()
    {
        const long Mb = 1024 * 1024;
        const long Cache = 4000 * Mb;
        var fallback = DumpSizeEstimator.FromPrivateBytes(500 * Mb);
        DumpSizeEstimator.Choose(10 * Mb, 500 * Mb, walkMayEndEarly: true, Cache).ShouldBe(new DumpEstimate(fallback + Cache, 10 * Mb, 500 * Mb, Cache, FromWalk: false));
        DumpSizeEstimator.Choose(5000 * Mb, 500 * Mb, walkMayEndEarly: true, Cache).Bytes.ShouldBe((5000 * Mb) + DumpSizeEstimator.StreamBytes + Cache);
        DumpSizeEstimator.Choose(null, null, walkMayEndEarly: true, Cache).Bytes.ShouldBe(DumpSizeEstimator.FromPrivateBytes(null) + Cache);
        DumpSizeEstimator.Choose(10 * Mb, 500 * Mb, walkMayEndEarly: false, 0).Bytes.ShouldBe((10 * Mb) + DumpSizeEstimator.StreamBytes, "Windows and Linux as before");
        DumpSizeEstimator.Choose(null, 500 * Mb, walkMayEndEarly: false, 0).ToString().ShouldBe($"{fallback} bytes, from the private bytes (walk not readable, private bytes {500 * Mb}, not in the walk 0)");
    }

    [Fact]
    public void A_huge_estimate_is_refused_not_wrapped_into_a_pass()
    {
        var self = new DotNetProcess(1, "orders", null, null, ElevationState.NotElevated, null, null, null);
        Should.Throw<InsufficientDiskSpaceException>(() => new DumpCapturer(disk: new SomeDisk(), estimator: _ => long.MaxValue).Preflight(self, Path.Combine(Path.GetTempPath(), "a.dmp")));
    }

    [Fact]
    public void Macos_says_to_run_as_the_processs_user_and_never_offers_sudo()
    {
        string[] texts =
        [
            ElevationTexts.NeedsMoreOn(HostSystem.MacOS), ElevationTexts.HintOn(HostSystem.MacOS), ElevationTexts.CliNoteOn(HostSystem.MacOS), ElevationTexts.ShowHiddenTipOn(HostSystem.MacOS),
            ElevationTexts.HiddenNoteOn(3, HostSystem.MacOS), ElevationTexts.Refusal("app", 7, "dump it", "capture again", HostSystem.MacOS),
        ];
        texts.ShouldAllBe(t => !t.Contains("root", StringComparison.Ordinal) && !t.Contains("sudo", StringComparison.Ordinal) && !t.Contains("administrator", StringComparison.Ordinal));
        texts[0].ShouldBe("Needs its user");
        texts[5].ShouldBe("app (pid 7) runs as another user, so HeapScope cannot dump it. Run HeapScope as that user and capture again.");
        ElevationTexts.ColumnTipOn(HostSystem.MacOS).ShouldStartWith("\"Needs its user\" means you must run HeapScope as that process's user to capture it.");
        ElevationTexts.ShowHiddenOn(HostSystem.MacOS).ShouldBe("Show other users' processes");
        ElevationTexts.LearnNoteOn(HostSystem.MacOS)!.ShouldStartWith("Capture on macOS is untested.");
        ElevationTexts.NeedsMoreOn(HostSystem.Other).ShouldBe("Needs administrator");
    }

    private sealed class SomeDisk : IDiskSpace
    {
        public long? AvailableBytes(string directory) => 1L << 40;
    }

    private sealed class FakeFacts : IProcessFacts
    {
        public string? ImagePath(int processId) => null;

        public string? CommandLine(int processId) => null;

        public RuntimeFacts? Runtime(System.Diagnostics.Process process) => null;

        public int? EffectiveUserId(int processId) => processId == 2 ? 502 : processId == 3 ? null : 501;

        public long? ReadableBytes(int processId) => null;

        public long? PrivateBytes(System.Diagnostics.Process process) => null;
    }
}
