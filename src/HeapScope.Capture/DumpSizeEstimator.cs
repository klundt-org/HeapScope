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

/// <summary>
/// Estimates the size of a full dump (sprint 1.1 review). A full dump holds every committed, readable page of the
/// process: private memory, loaded images and mapped files. Walking the address space with <c>VirtualQueryEx</c> sums
/// exactly those pages; when the walk is not allowed, the estimate falls back to private bytes plus a fixed part for
/// images and mapped files (measured on the samples: 250 to 660 MB outside the GC heap). On Linux (sprint 4.1) the readable
/// mappings in <c>/proc/&lt;pid&gt;/maps</c> are summed instead (<see cref="ProcText.ReadableBytes"/>), with the same fallback
/// when another user's maps cannot be read.
/// </summary>
public static class DumpSizeEstimator
{
    /// <summary>Headers, module lists, thread contexts and other streams.</summary>
    public const long StreamBytes = 16L * 1024 * 1024;

    /// <summary>Fallback: images and mapped files outside private memory.</summary>
    public const long FallbackImageBytes = 384L * 1024 * 1024;

    /// <summary>Fallback headroom over private bytes.</summary>
    public const double FallbackFactor = 1.1;

    /// <summary>The fallback estimate from private bytes alone; errs high.</summary>
    public static long FromPrivateBytes(long? privateBytes) =>
        (long)((privateBytes ?? 0) * FallbackFactor) + FallbackImageBytes + StreamBytes;

    /// <summary>The estimate for a running process: the committed readable pages, or the fallback.</summary>
    public static long Estimate(DotNetProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return Choose(CommittedReadableBytes(process.ProcessId), process.PrivateBytes, HostSystems.Current);
    }

    /// <summary>
    /// The walk's bytes, or the fallback without one. On macOS (sprint 4.2, untested) the larger of the two: a region walk
    /// that ends early cannot be told from one that is complete, and the estimate must not err low.
    /// </summary>
    internal static long Choose(long? committed, long? privateBytes, HostSystem system)
    {
        var fallback = FromPrivateBytes(privateBytes);
        return committed is not { } bytes ? fallback
            : system == HostSystem.MacOS ? Math.Max(bytes > long.MaxValue - StreamBytes ? long.MaxValue : bytes + StreamBytes, fallback)
            : bytes + StreamBytes;
    }

    /// <summary>Sum of committed pages that are neither no-access nor guard pages; null when the walk is denied.</summary>
    public static long? CommittedReadableBytes(int processId) => CapturePlatform.ProcessFacts().ReadableBytes(processId);
}
