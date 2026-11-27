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

namespace HeapScope.Contracts;

/// <summary>
/// What a live trace's answer counted: the interval asked for, outward to whole stretches, which grow in a long
/// recording, and as of when. Questions asked within a second may be answered from one view, so it can be a little older than the
/// trace's state.
/// </summary>
/// <param name="Version">The version of the trace it counted (<see cref="LiveTraceDto.Version"/>).</param>
/// <param name="AsOfSeconds">How long the recording was by then.</param>
/// <param name="StretchSeconds">How long one stretch was.</param>
public sealed record AllocationIntervalDto(double FromSeconds, double? ToSeconds, long Version, double AsOfSeconds, double StretchSeconds);
