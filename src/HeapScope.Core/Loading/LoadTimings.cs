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

namespace HeapScope.Core.Loading;

/// <summary>How long each part of a load took.</summary>
/// <param name="Freeze">Freezing the objects and the collection after it (sprint 3.12, R5: before, inside References).</param>
/// <param name="Costs">What each phase cost besides its time (sprint 3.12, R5), in phase order; empty when not measured.</param>
public sealed record LoadTimings(TimeSpan Open, TimeSpan Objects, TimeSpan References, TimeSpan Roots, TimeSpan Build, TimeSpan Freeze = default, IReadOnlyList<LoadPhaseCost>? Costs = null)
{
    public TimeSpan Total => Open + Objects + Freeze + References + Roots + Build;

    /// <summary>The costs, or none.</summary>
    public IReadOnlyList<LoadPhaseCost> PhaseCosts => Costs ?? [];
}
