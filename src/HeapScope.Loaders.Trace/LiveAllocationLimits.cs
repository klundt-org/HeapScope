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

namespace HeapScope.Loaders.Trace;

/// <summary>
/// What a live trace keeps at most (sprint 4.3). The defaults hold 30 minutes a second at a time; an entry takes about 50 bytes and
/// a stack node about 40.
/// </summary>
internal sealed record LiveAllocationLimits(double BucketMs = 1000, int MaxBuckets = 1800, int MaxEntries = 400_000, int MaxStackNodes = 300_000, int MaxTypes = 50_000)
{
    /// <summary>The memory a live trace reserves while it is read: the limits above, the names and TraceEvent's buffers.</summary>
    public const long ReservedBytes = 160L * 1024 * 1024;

    public static LiveAllocationLimits Default { get; } = new();
}
