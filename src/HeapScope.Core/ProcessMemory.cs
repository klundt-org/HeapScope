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
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HeapScope.Core;

/// <summary>
/// The memory this process holds, for the worker's budget (peer review 4.5 H7, sprint 4.7): its private bytes, and on macOS, where
/// .NET's <c>PrivateMemorySize64</c> is always 0, its physical footprint (untested, like the rest of macOS).
/// </summary>
public static partial class ProcessMemory
{
    // rusage_info_v0: the footprint after a 16-byte uuid and seven 8-byte counts (sprint 4.2, HeapScope.Capture's MacProcData).
    private const int UsageSize = 96;
    private const int FootprintOffset = 72;

    public static long PrivateBytes()
    {
        if (OperatingSystem.IsMacOS() && Footprint() is { } footprint)
        {
            return footprint;
        }

        using var process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }

    private static long? Footprint()
    {
        try
        {
            var buffer = new byte[UsageSize];
            return ProcPidUsage(Environment.ProcessId, 0, buffer) == 0 && BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(FootprintOffset)) is > 0 and var bytes
                ? (long)bytes
                : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [LibraryImport("/usr/lib/libproc.dylib", EntryPoint = "proc_pid_rusage")]
    private static partial int ProcPidUsage(int processId, int flavor, [Out] byte[] buffer);
}
