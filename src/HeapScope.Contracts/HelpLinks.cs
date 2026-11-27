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
/// Where a user gets help (peer review 4.5 H14): one place, the project's issues, with SUPPORT.md saying what to include and what
/// the log holds. Every message that sends the user for help names these, never a "support" that does not exist.
/// </summary>
public static class HelpLinks
{
    /// <summary>The project's issues, where problems and questions go.</summary>
    public const string Issues = "https://github.com/klundt-org/HeapScope/issues";

    /// <summary>What to include in an issue, and how to check a log before attaching it.</summary>
    public const string Support = "https://github.com/klundt-org/HeapScope/blob/main/SUPPORT.md";
}
