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

/// <summary>A live recording that has started (sprint 4.3, ADR-037): the file grows from here on.</summary>
/// <param name="Path">The trace, under its final name.</param>
/// <param name="NamesPath">A short trace that names the methods compiled before, deleted when the recording ends; null without one.</param>
/// <param name="NamesProblem">Why there is none, in words for the log.</param>
/// <param name="NamesTime">How long recording the names took.</param>
public sealed record LiveTraceStart(string Path, string? NamesPath, string? NamesProblem, TimeSpan NamesTime);
