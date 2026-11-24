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

namespace HeapScope.Capture;

/// <summary>Capture on the system HeapScope runs on (sprint 2.3.5).</summary>
public static class CapturePlatform
{
    /// <summary>The elevation probe of this system.</summary>
    public static IElevationProbe ElevationProbe() =>
        OperatingSystem.IsWindows() ? WindowsElevationProbe.Instance : UnixElevationProbe.Instance;

    /// <summary>What this system lets HeapScope read about a process (sprint 4.1).</summary>
    internal static IProcessFacts ProcessFacts() => ProcessFacts(HostSystems.Current);

    /// <summary><see cref="ProcessFacts()"/> of <paramref name="system"/> (tests): one without a reader gives nothing (4.1 review M6).</summary>
    internal static IProcessFacts ProcessFacts(HostSystem system) => system switch
    {
        HostSystem.Windows => WindowsProcessFacts.Instance,
        HostSystem.Linux => LinuxProcessFacts.Instance,
        HostSystem.MacOS => MacProcessFacts.Instance,
        _ => NoProcessFacts.Instance,
    };

    /// <summary>Why this system refuses or fails a capture (sprint 4.2).</summary>
    internal static ICaptureRules Rules() => Rules(HostSystems.Current);

    /// <summary><see cref="Rules()"/> of <paramref name="system"/> (tests).</summary>
    internal static ICaptureRules Rules(HostSystem system) => system switch
    {
        HostSystem.Linux => LinuxCaptureRules.Instance,
        HostSystem.MacOS => MacCaptureRules.Instance,
        _ => WindowsCaptureRules.Instance,
    };

    /// <summary>
    /// The user a process acts as (its effective user id) where rights go by user, for the log (sprint 4.1 review H6): the
    /// rule that marks another user's process rests on it. Null on Windows, or when the process exited.
    /// </summary>
    public static int? EffectiveUserId(int processId) => ProcessFacts().EffectiveUserId(processId);

    /// <summary>Whether capture is built for this system: Windows, Linux (sprint 4.1) and macOS (sprint 4.2, untested).</summary>
    public static bool IsSupported { get; } = IsSupportedOn(HostSystems.Current);

    /// <summary><see cref="IsSupported"/> of <paramref name="system"/> (tests).</summary>
    internal static bool IsSupportedOn(HostSystem system) => system is HostSystem.Windows or HostSystem.Linux or HostSystem.MacOS;

    /// <summary>
    /// Refuses capture where it is not built. Opening and analyzing snapshots works everywhere.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">Not running on Windows, Linux or macOS.</exception>
    public static void EnsureSupported() => EnsureSupported(IsSupported);

    /// <summary><see cref="EnsureSupported()"/> for a system that does or does not support capture (tests).</summary>
    public static void EnsureSupported(bool supported)
    {
        if (UnsupportedReason(supported) is { } reason)
        {
            throw new PlatformNotSupportedException(reason);
        }
    }

    /// <summary>Why capture is not available on this system, or null where it is (sprint 2.3.6: views say so first).</summary>
    public static string? UnsupportedReason() => UnsupportedReason(IsSupported);

    /// <summary><see cref="UnsupportedReason()"/> for a system that does or does not support capture (tests).</summary>
    public static string? UnsupportedReason(bool supported) =>
        supported
            ? null
            : "Capturing from HeapScope runs on Windows, Linux and macOS. Here, take a dump with "
                + "'dotnet-dump collect --process-id <pid> --type Full' and open it with HeapScope.";
}
