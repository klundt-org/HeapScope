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

namespace HeapScope.Analysis.Inspections;

/// <summary>
/// What an inspection's answer rests on, in the same terms for every inspection (sprint 3.12, B7): the objects, collections
/// or types it scanned and those it left out (could not read, or excluded by its rules), so "why is mine not listed?" has an
/// answer in the log. Lists of rows in a basis are cut at <see cref="ListedCap"/>.
/// </summary>
/// <param name="ListsRows">Whether the inspection's basis lists rows, so its line says the cap (the review's M15).</param>
public sealed record InspectionCounts(long Scanned, long LeftOut, bool ListsRows = false)
{
    /// <summary>The most rows a log line lists; the rest are counted ("and N more").</summary>
    public const int ListedCap = 50;

    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"scanned {Scanned}, left out {LeftOut}{(ListsRows ? $", rows listed at most {ListedCap}" : string.Empty)}");
}
