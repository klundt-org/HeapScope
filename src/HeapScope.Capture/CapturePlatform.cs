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

namespace HeapScope.Capture;

/// <summary>Capture on the system HeapScope runs on (sprint 2.3.5).</summary>
public static class CapturePlatform
{
    /// <summary>The elevation probe of this system.</summary>
    public static IElevationProbe ElevationProbe() =>
        OperatingSystem.IsWindows() ? WindowsElevationProbe.Instance : UnixElevationProbe.Instance;

    /// <summary>What this system lets HeapScope read about a process (sprint 4.1); macOS is refused before it is used.</summary>
    internal static IProcessFacts ProcessFacts() =>
        OperatingSystem.IsLinux() ? LinuxProcessFacts.Instance : WindowsProcessFacts.Instance;

    /// <summary>Whether capture is built for this system: Windows, and Linux since sprint 4.1 (macOS follows in 4.2).</summary>
    public static bool IsSupported { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <summary>
    /// Refuses capture where it is not built yet (macOS until sprint 4.2). Opening and analyzing snapshots works everywhere.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">Not running on Windows or Linux.</exception>
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
            : "Capturing from HeapScope runs on Windows and Linux for now. On macOS, take a dump with "
                + "'dotnet-dump collect --process-id <pid> --type Full' and open it with HeapScope.";
}
