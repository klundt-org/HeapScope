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
/// The worker's peak memory for a first load of a dump (ARCHITECTURE.md 10): the front ends warn with it before they ask, and since
/// sprint 4.7 the worker reserves it while it loads and logs it against what the load used (peer review 4.5 H5). Refitted in sprint
/// 4.7, when a load's references went to a mapped file (budgets.md): above every first load peak measured (ClrMD cache capped at
/// 512 MB), the highest per dump byte being sprint 3.10's Medium (3,984 MB dump, 2,470 MB, before the file) and, after it, an 8 GB
/// DenseHeap dump (9,054 MB, 3,569 MB) and a 2 GB one (3,145 MB, 1,161 MB).
/// </summary>
public static class FirstLoadEstimate
{
    /// <summary>Peak private bytes per byte of dump, with headroom for denser heaps than the fixtures.</summary>
    public const double BytesPerDumpByte = 0.45;

    /// <summary>Fixed part: ClrMD's capped cache, runtime structures and the worker itself.</summary>
    public const long FixedBytes = 704L * 1024 * 1024;

    /// <summary>The part of <see cref="FixedBytes"/> a worker holds before any load (its runtime and code): not reserved again.</summary>
    public const long WorkerIdleBytes = 256L * 1024 * 1024;

    public static long PeakBytes(long dumpBytes) =>
        dumpBytes <= 0 ? FixedBytes : (long)(dumpBytes * BytesPerDumpByte) + FixedBytes;
}
