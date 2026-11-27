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

/// <summary>
/// What reading a finished allocation trace costs the worker, and the largest trace a recording may write so that its read fits:
/// a fixed part for TraceEvent and the runtime's names, plus a part per byte of the file. Measured
/// at about 76 MB plus 2.1 bytes per file byte with the worker's GC settings; these leave a margin.
/// </summary>
public static class TraceReadCost
{
    public const long FixedBytes = 96L * 1024 * 1024;

    public const int BytesPerFileByte = 3;

    /// <summary>
    /// The share of physical memory a trace's read may take: a quarter, half of the worker's budget, so two snapshots of the
    /// recorded process still fit beside it ("allocated between" opens all three).
    /// </summary>
    public const double ShareOfMemory = 0.25;

    /// <summary>The smallest limit a recording is given, whatever the machine: below it a trace says too little.</summary>
    public const long LeastLimitBytes = 64L * 1024 * 1024;

    /// <summary>The memory a read of a file of <paramref name="fileBytes"/> reserves.</summary>
    public static long Reservation(long fileBytes) => FixedBytes + (fileBytes * BytesPerFileByte);

    /// <summary>
    /// <paramref name="limitBytes"/>, or a lower limit whose read fits <see cref="ShareOfMemory"/> of <paramref name="totalPhysicalBytes"/>
    /// (never below <see cref="LeastLimitBytes"/>); the machine's total is used when it cannot be read (0).
    /// </summary>
    public static long Fit(long limitBytes, long totalPhysicalBytes)
    {
        if (totalPhysicalBytes <= 0)
        {
            return limitBytes;
        }

        var fitting = ((long)(totalPhysicalBytes * ShareOfMemory) - FixedBytes) / BytesPerFileByte;
        return Math.Min(limitBytes, Math.Max(LeastLimitBytes, fitting));
    }
}
