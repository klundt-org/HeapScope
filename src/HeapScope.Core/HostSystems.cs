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

namespace HeapScope.Core;

/// <summary>The system this process runs on.</summary>
public static class HostSystems
{
    public static HostSystem Current { get; } =
        OperatingSystem.IsWindows() ? HostSystem.Windows
        : OperatingSystem.IsLinux() ? HostSystem.Linux
        : OperatingSystem.IsMacOS() ? HostSystem.MacOS
        : HostSystem.Other;

    /// <summary>
    /// Whether the right to reach a process goes by its user (Linux, macOS: the process's own user or root), not by
    /// elevation as on Windows. A system without capture has no such rule and keeps the Windows words.
    /// </summary>
    public static bool RightsGoByUser(HostSystem system) => system is HostSystem.Linux or HostSystem.MacOS;
}
