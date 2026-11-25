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
/// What a live trace keeps at most (sprint 4.3). The defaults hold 30 minutes a second at a time. At the limits all
/// of it takes 75 MB, names included, and two views 23 MB more (measured, budgets.md); folding the stacks again holds the nodes and one
/// stretch twice.
/// </summary>
/// <param name="FewestBuckets">Entries past their limit make stretches longer down to this many, and then give up the stacks.</param>
/// <param name="MaxFrames">Addresses whose frame is remembered between samples.</param>
/// <param name="MaxUnnamed">Addresses without a name that are told apart.</param>
/// <param name="MaxTypeText">Characters of type names.</param>
internal sealed record LiveAllocationLimits(
    double BucketMs = 1000,
    int MaxBuckets = 1800,
    int FewestBuckets = 60,
    int MaxEntries = 400_000,
    int MaxStackNodes = 300_000,
    int MaxFrames = 100_000,
    int MaxUnnamed = 20_000,
    int MaxTypes = 50_000,
    int MaxTypeText = 4_000_000)
{
    /// <summary>
    /// The memory a live trace reserves while it is read: what it keeps at the limits (98 MB with two views), the folding and
    /// TraceEvent's buffers.
    /// </summary>
    public const long ReservedBytes = 192L * 1024 * 1024;

    public static LiveAllocationLimits Default { get; } = new();
}
