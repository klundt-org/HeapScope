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

/// <summary>
/// How a full dump's GC handles were read (peer review 4.5), for the log and the snapshot's notes.
/// </summary>
/// <param name="Heaps">GC heaps the process had when it was dumped.</param>
/// <param name="Tables">Handle tables walked.</param>
/// <param name="Processors">Processors the dump records, when it does: of one processor group, so a process can have more tables.</param>
/// <param name="Added">Handles the runtime's own walk left out.</param>
/// <param name="How">What was done, in words for the log.</param>
public sealed record HandleWalk(HandleWalkOutcome Outcome, int Heaps, int Tables, int? Processors, int Added, string How);
