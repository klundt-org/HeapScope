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

/// <summary>Sprint 2.3.5: capture on each system, and elevation off Windows.</summary>
public sealed class CapturePlatformTests
{
    [Fact]
    public void Capture_is_refused_off_windows_with_the_way_to_take_a_dump_there()
    {
        var refused = Should.Throw<PlatformNotSupportedException>(() => CapturePlatform.EnsureSupported(supported: false));

        refused.Message.ShouldContain("dotnet-dump collect");
        refused.Message.ShouldStartWith("Capturing from HeapScope runs on Windows, Linux and macOS. Here,");
        CapturePlatform.IsSupported.ShouldBe(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        Should.NotThrow(() => CapturePlatform.EnsureSupported(supported: true));
    }

    [Fact]
    public void The_probe_for_this_system_is_chosen_and_the_unix_one_needs_no_windows_api()
    {
        CapturePlatform.ElevationProbe().ShouldBeOfType(OperatingSystem.IsWindows() ? typeof(WindowsElevationProbe) : typeof(UnixElevationProbe));
        UnixElevationProbe.Instance.IsCurrentProcessElevated.ShouldBe(Environment.IsPrivilegedProcess);
        UnixElevationProbe.Instance.GetState(Environment.ProcessId).ShouldBe(OperatingSystem.IsLinux() ? ElevationState.NotElevated : ElevationState.Unknown);
    }

    [Fact]
    public void On_linux_another_users_process_needs_root_or_that_user()
    {
        // Sprint 4.1 task 4: the rule on effective user ids (root is 0).
        UnixElevationProbe.StateOf(target: 1000, own: 1000).ShouldBe(ElevationState.NotElevated);
        UnixElevationProbe.StateOf(target: 1001, own: 1000).ShouldBe(ElevationState.Elevated);
        UnixElevationProbe.StateOf(target: 0, own: 1000).ShouldBe(ElevationState.Elevated);
        UnixElevationProbe.StateOf(target: 0, own: 0).ShouldBe(ElevationState.NotElevated);
        UnixElevationProbe.StateOf(target: null, own: 1000).ShouldBe(ElevationState.Unknown);
        UnixElevationProbe.StateOf(target: 1000, own: null).ShouldBe(ElevationState.Unknown);
    }

    [Fact]
    public void Each_system_has_its_own_facts_and_rules_and_one_without_a_reader_gets_none()
    {
        CapturePlatform.ProcessFacts(HostSystem.Windows).ShouldBeOfType<WindowsProcessFacts>();
        CapturePlatform.ProcessFacts(HostSystem.Linux).ShouldBeOfType<LinuxProcessFacts>();
        CapturePlatform.ProcessFacts(HostSystem.Other).ShouldBeOfType<NoProcessFacts>();
        CapturePlatform.Rules(HostSystem.Windows).ShouldBeOfType<WindowsCaptureRules>();
        CapturePlatform.Rules(HostSystem.Linux).ShouldBeOfType<LinuxCaptureRules>();
        CapturePlatform.IsSupportedOn(HostSystem.Other).ShouldBeFalse();
        NoProcessFacts.Instance.EffectiveUserId(Environment.ProcessId).ShouldBeNull();
        NoProcessFacts.Instance.ReadableBytes(Environment.ProcessId).ShouldBeNull();
    }

    [Theory]
    [InlineData("createdump failed 0x80070070", "the disk is full")]
    [InlineData("createdump failed 0x80070005", "the account orders runs as cannot write to D:\\dumps")]
    [InlineData("createdump failed 0x80004005", null)]
    public void On_windows_the_result_code_explains_a_dump_that_was_not_written(string message, string? reason) =>
        WindowsCaptureRules.Instance.WhyNotWritten(message, "orders", @"D:\dumps").Reason.ShouldBe(reason);
}
