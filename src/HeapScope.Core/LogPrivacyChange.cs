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

/// <summary>What the logs may hold after a change of the settings (sprint 3.13.2).</summary>
/// <param name="IncludeFilterText">Filter and search text.</param>
/// <param name="IncludeDumpValues">Values read from a dump.</param>
public readonly record struct LogPrivacyChange(bool IncludeFilterText, bool IncludeDumpValues)
{
    /// <summary>For the log: "filter text on, dump values off".</summary>
    public override string ToString() => Describe(IncludeFilterText, IncludeDumpValues);

    internal static string Describe(bool includeFilterText, bool includeDumpValues) =>
        $"filter text {(includeFilterText ? "on" : "off")}, dump values {(includeDumpValues ? "on" : "off")}";
}
