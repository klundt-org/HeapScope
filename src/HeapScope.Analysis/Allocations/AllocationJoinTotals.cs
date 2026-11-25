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


namespace HeapScope.Analysis.Allocations;

/// <summary>How an interval's sampled bytes met a diff's types (<see cref="AllocationJoin.Join"/>), for the result and the log.</summary>
/// <param name="ByMethodTable">Bytes joined by method table, to a full dump's types.</param>
/// <param name="ByName">Bytes joined by name, to a gcdump's types.</param>
/// <param name="SharedName">Bytes of names that types of several modules share in a gcdump: no row takes them.</param>
/// <param name="Absent">Bytes of types without an object in either snapshot.</param>
/// <param name="OtherNames">A method table whose names differ in the trace and the dump (joined all the same), for the log.</param>
public sealed record AllocationJoinTotals(long Bytes, int Samples, long ByMethodTable, long ByName, long SharedName, long Absent, int AbsentTypes, int OtherNames, IReadOnlyList<AllocatedType> TopAbsent);
