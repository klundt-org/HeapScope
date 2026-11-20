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

/// <summary>Sprint 2.3.5: capture on each system, and elevation off Windows.</summary>
public sealed class CapturePlatformTests
{
    [Fact]
    public void Capture_is_refused_off_windows_with_the_way_to_take_a_dump_there()
    {
        var refused = Should.Throw<PlatformNotSupportedException>(() => CapturePlatform.EnsureSupported(isWindows: false));

        refused.Message.ShouldContain("dotnet-dump collect");
        Should.NotThrow(() => CapturePlatform.EnsureSupported(isWindows: true));
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
}
