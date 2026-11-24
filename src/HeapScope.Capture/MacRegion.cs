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

namespace HeapScope.Capture;

/// <summary>A range of a process's memory on macOS (sprint 4.2).</summary>
/// <param name="Address">Where it starts.</param>
/// <param name="Size">Its bytes.</param>
/// <param name="Readable">Whether a dump holds it: it can be read and is not an empty reservation.</param>
/// <param name="Path">The file mapped there, or null.</param>
internal readonly record struct MacRegion(ulong Address, ulong Size, bool Readable, string? Path);
