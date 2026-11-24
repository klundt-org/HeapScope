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

    private static MacTarget Target(
        int? user = 501,
        int? own = 501,
        TargetEnvironment? environment = null,
        bool regionsRead = true,
        string? runtime = null,
        bool? carries = null,
        string ownTemp = "/var/folders/ab/T/") =>
        new(42, user, own, environment, regionsRead, runtime, carries, ownTemp);

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
    public void A_walk_follows_the_regions_and_stops_where_they_end_or_do_not_move_on()
    {
        MacRegion[] regions = [new(0x1000, 0x1000, true, null), new(0x8000, 0x1000, false, null), new(0x9000, 0x3000, true, null)];
        MacRegion? Next(ulong address) => regions.Where(r => r.Address + r.Size > address).Select(r => (MacRegion?)r).FirstOrDefault();

        MacProcData.Walk(Next).ShouldBe(regions);
        MacProcData.ReadableBytes(MacProcData.Walk(Next)).ShouldBe(0x4000);
        MacProcData.Walk(_ => null).ShouldBeEmpty();
        MacProcData.ReadableBytes([]).ShouldBeNull("not allowed is not zero bytes");
        MacProcData.Walk(_ => new MacRegion(0, 0, true, null)).Count().ShouldBe(1, "a region that does not move the walk on ends it");
        MacProcData.Walk(a => new MacRegion(a, ulong.MaxValue, true, null)).Count().ShouldBeLessThan(3, "an end past the address space wraps");
        MacProcData.ReadableBytes([new(0, ulong.MaxValue, true, null), new(0, 5, true, null)]).ShouldBe(long.MaxValue);
    }

    [Fact]
    public void The_runtime_is_the_folder_of_the_mapped_libcoreclr()
    {
        MacRegion[] regions = [new(1, 1, true, "/usr/lib/dyld"), new(2, 1, true, null), new(3, 1, true, Framework + "/libcoreclr.dylib")];
        MacProcData.Runtime(regions).ShouldBe(new RuntimeFacts("10.0.1", Framework));
        MacProcData.Runtime([new(1, 1, true, "/Applications/Orders.app/libcoreclr.dylib")]).ShouldBe(new RuntimeFacts(null, "/Applications/Orders.app"));
        MacProcData.Runtime([new(1, 1, true, "/opt/orders/OrdersWorker")]).ShouldBeNull("a single-file app links the runtime in");
    }

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
    public void Off_macos_every_fact_is_empty()
    {
        Assert.SkipWhen(OperatingSystem.IsMacOS(), "The calls exist there.");
        var pid = Environment.ProcessId;
        MacProcessFacts.Instance.ImagePath(pid).ShouldBeNull();
        MacProcessFacts.Instance.CommandLine(pid).ShouldBeNull();
        MacProcessFacts.Instance.EffectiveUserId(pid).ShouldBeNull();
        MacProcessFacts.Instance.ReadableBytes(pid).ShouldBeNull();
        MacCaptureRules.Instance.WhyNotFound(pid).Reason.ShouldBeNull();
    }

    [Fact]
    public void On_macos_the_test_process_has_its_facts()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Calls libproc and sysctl, which macOS has.");
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        var facts = CapturePlatform.ProcessFacts();
        facts.ImagePath(self.Id).ShouldBe(Environment.ProcessPath);
        facts.CommandLine(self.Id).ShouldNotBeNullOrEmpty();
        facts.EffectiveUserId(self.Id).ShouldNotBeNull();
        facts.ReadableBytes(self.Id)!.Value.ShouldBeGreaterThan(16 * 1024 * 1024);
        facts.Runtime(self)!.Directory.ShouldNotBeNull();
        MacProcessFacts.Instance.Environment(self.Id).ShouldNotBeNull();
    }

    [Fact]
    public void Macos_has_its_own_facts_and_rules()
    {
        CapturePlatform.ProcessFacts(HostSystem.MacOS).ShouldBeOfType<MacProcessFacts>();
        CapturePlatform.Rules(HostSystem.MacOS).ShouldBeOfType<MacCaptureRules>();
        CapturePlatform.IsSupportedOn(HostSystem.MacOS).ShouldBeTrue();
    }

    [Fact]
    public void The_elevation_probe_compares_the_ids_the_systems_facts_give()
    {
        var probe = new UnixElevationProbe(new FakeFacts());
        probe.GetState(1).ShouldBe(ElevationState.NotElevated);
        probe.GetState(2).ShouldBe(ElevationState.Elevated);
        probe.GetState(3).ShouldBe(ElevationState.Unknown);
    }

    [Theory]
    [InlineData("[createdump] Invalid process id: task_for_pid(42) FAILED (os/kern) failure (5)\nThis failure may be because createdump or the application is not properly signed and entitled.", "task_for_pid refused")]
    [InlineData("No space left on device", "no space left")]
    [InlineData("Could not open output file: Permission denied", "the folder is not writable by the target's account")]
    [InlineData("Writing dump FAILED", "none matched")]
    public void A_write_failure_is_named_from_what_createdump_reported(string message, string rule)
    {
        var reason = MacCaptureRules.WriteFailure(message, "orders", "/Users/a/dumps", out var decided);
        decided.ShouldBe(rule);
        (reason is null).ShouldBe(rule == "none matched");
        MacCaptureRules.Instance.WhyNotWritten(message, "orders", "/Users/a/dumps").ShouldBe(new WriteFailureExplanation(reason, "macOS; rule: " + rule));
    }

    [Fact]
    public void A_task_for_pid_failure_offers_a_gcdump() =>
        MacCaptureRules.WriteFailure("task_for_pid(42) FAILED", "orders", "/d", out _)!.ShouldEndWith("A gcdump does not need this: capture one instead");

    [Fact]
    public void A_process_that_is_not_found_is_explained_where_macos_can_tell()
    {
        var here = new TargetEnvironment(["TMPDIR=/private/var/folders/ab/T/"]);
        MacCaptureRules.WhyNoDiagnostics(Target(user: null)).ShouldBeNull();
        MacCaptureRules.WhyNoDiagnostics(Target(user: 502))!.ShouldContain("runs as another user");
        MacCaptureRules.WhyNoDiagnostics(Target(own: 0, environment: here, runtime: Framework)).ShouldBeNull("root reaches it in the same folder");
        MacCaptureRules.WhyNoDiagnostics(Target(environment: new TargetEnvironment(["COMPlus_EnableDiagnostics=0"])))!.ShouldContain("(COMPlus_EnableDiagnostics=0)");
        MacCaptureRules.WhyNoDiagnostics(Target(own: 0, environment: new TargetEnvironment(["TMPDIR=/var/folders/zz/T/"])))!.ShouldContain("TMPDIR=/var/folders/zz/T,");
        MacCaptureRules.WhyNoDiagnostics(Target(environment: here, runtime: Framework)).ShouldBeNull("/var is a link into /private");
        MacCaptureRules.WhyNoDiagnostics(Target(environment: here, carries: false))!.ShouldContain("not a .NET process");
        MacCaptureRules.WhyNoDiagnostics(Target(environment: here, carries: true)).ShouldBeNull();
        MacCaptureRules.WhyNoDiagnostics(Target(environment: here, carries: null)).ShouldBeNull();
        MacCaptureRules.WhyNoDiagnostics(Target(environment: here, regionsRead: false)).ShouldBeNull();
        MacCaptureRules.WhyNoDiagnostics(Target(environment: null, runtime: Framework)).ShouldBeNull();
    }

    [Fact]
    public void The_evidence_says_what_the_decision_rests_on_and_no_value_but_the_temp_folder()
    {
        MacCaptureRules.Evidence(Target(environment: new TargetEnvironment(["TMPDIR=/t", "SECRET=orders-key"]), runtime: Framework)).ShouldBe(
            $"macOS check of pid 42: running yes; effective user 501, HeapScope's 501; environment readable; diagnostics off: none; TMPDIR /t, HeapScope's /var/folders/ab/T; runtime libcoreclr.dylib in {Framework}");
        MacCaptureRules.Evidence(Target(user: null, regionsRead: false)).ShouldBe(
            "macOS check of pid 42: running no; effective user unknown, HeapScope's 501; environment not given; diagnostics off: unknown; TMPDIR unknown, HeapScope's /var/folders/ab/T; runtime regions not readable");
    }

    [Fact]
    public void The_estimate_takes_the_larger_figure_on_macos_where_a_walk_may_end_early()
    {
        const long Mb = 1024 * 1024;
        var fallback = DumpSizeEstimator.FromPrivateBytes(500 * Mb);
        DumpSizeEstimator.Choose(10 * Mb, 500 * Mb, HostSystem.MacOS).ShouldBe(fallback);
        DumpSizeEstimator.Choose(5000 * Mb, 500 * Mb, HostSystem.MacOS).ShouldBe((5000 * Mb) + DumpSizeEstimator.StreamBytes);
        DumpSizeEstimator.Choose(long.MaxValue, 500 * Mb, HostSystem.MacOS).ShouldBe(long.MaxValue);
        DumpSizeEstimator.Choose(10 * Mb, 500 * Mb, HostSystem.Linux).ShouldBe((10 * Mb) + DumpSizeEstimator.StreamBytes);
        DumpSizeEstimator.Choose(null, 500 * Mb, HostSystem.Windows).ShouldBe(fallback);
    }

    [Fact]
    public void Macos_has_the_words_of_a_system_where_rights_go_by_user()
    {
        ElevationTexts.NeedsMoreOn(HostSystem.MacOS).ShouldBe("Needs root or its user");
        ElevationTexts.ShowHiddenOn(HostSystem.MacOS).ShouldBe("Show other users' processes");
        ElevationTexts.Refusal("app", 7, "dump it", "capture again", HostSystem.MacOS).ShouldBe(ElevationTexts.Refusal("app", 7, "dump it", "capture again", HostSystem.Linux));
        ElevationTexts.LearnNoteOn(HostSystem.MacOS)!.ShouldStartWith("Capture on macOS is untested.");
        ElevationTexts.NeedsMoreOn(HostSystem.Other).ShouldBe("Needs administrator");
    }

    private sealed class FakeFacts : IProcessFacts
    {
        public string? ImagePath(int processId) => null;

        public string? CommandLine(int processId) => null;

        public RuntimeFacts? Runtime(System.Diagnostics.Process process) => null;

        public int? EffectiveUserId(int processId) => processId == 2 ? 502 : processId == 3 ? null : 501;

        public long? ReadableBytes(int processId) => null;
    }
}
