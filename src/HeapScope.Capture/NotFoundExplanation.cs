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

/// <summary>
/// Why a process asked for by pid was not found (sprint 4.1), where the system can tell. <see cref="Reason"/> is for the
/// user, with what to do; null leaves the generic text. <see cref="Evidence"/> is for the log: what the reason rests on
/// (review H6), never a value read from the process other than its temp folder.
/// </summary>
public sealed record NotFoundExplanation(string? Reason, string? Evidence)
{
    /// <summary>Nothing to say: the generic text, and nothing for the log.</summary>
    public static NotFoundExplanation None { get; } = new(null, null);
}
