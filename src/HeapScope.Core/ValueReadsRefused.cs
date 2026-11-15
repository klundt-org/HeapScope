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

namespace HeapScope.Core;

/// <summary>
/// Why a full dump's field values and strings are not read (sprint 3.12): its DAC was declined, or its index cache came from
/// an older loader because a rebuild could not reach a DAC. A snapshot that carries it has no <see cref="IObjectReader"/>;
/// every rule that offers or refuses a value read words its refusal with <see cref="Reason"/>.
/// </summary>
/// <param name="Reason">The user-facing reason, with a next step; never dump contents.</param>
public sealed record ValueReadsRefused(string Reason);
