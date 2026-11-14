// HeapScope
// Copyright (C) 2026 Ray Klundt
// https://github.com/rklundt/HeapScope
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

using System.Globalization;

namespace HeapScope.Core.Loading;

/// <summary>
/// What one load phase cost besides its wall time (sprint 3.12, R5): the process's CPU time (every thread: a load is one
/// thread plus the GC's), the time the runtime was paused for collections, and the page faults (a memory-mapped dump read
/// from disk faults pages in; null where the system does not say).
/// </summary>
public sealed record LoadPhaseCost(string Phase, TimeSpan Elapsed, TimeSpan Cpu, TimeSpan GcPause, long? PageFaults)
{
    /// <summary>"objects 5.0 s (CPU 5.1 s, GC 0.2 s, 1,234 page faults)", for the load's log line.</summary>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Phase} {Elapsed.TotalSeconds:N1} s (CPU {Cpu.TotalSeconds:N1} s, GC {GcPause.TotalSeconds:N1} s, {(PageFaults is { } faults ? faults.ToString("N0", CultureInfo.InvariantCulture) + " page faults" : "page faults not known")})");
}
