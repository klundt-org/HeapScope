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

using HeapScope.Application.Sessions;

namespace HeapScope.Application.Settings;

/// <summary>
/// What Purge all would delete (sprint 3.12, B1): the library's sessions, and with them every file below, or why it is
/// refused now. Nothing is deleted by making the plan.
/// </summary>
/// <param name="Sessions">The sessions in the library, the current one included.</param>
/// <param name="Files">Every file that would go (logs, session folders, HeapScope's caches of their dumps, bookmarks) and their bytes.</param>
/// <param name="Refused">Why it cannot run now (a capture, recording or open running, or a session in use), or null.</param>
public sealed record PurgePlan(int Sessions, Deletion Files, string? Refused);
