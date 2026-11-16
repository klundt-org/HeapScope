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

namespace HeapScope.Capture;

/// <summary>What a process list left out, by pid.</summary>
/// <param name="Own">HeapScope's own processes (this one, and the app, CLI and worker of this installation).</param>
/// <param name="NotDescribed">Processes that exited, or could not be read, while the list was taken.</param>
public sealed record DiscoveryOmissions(IReadOnlyList<int> Own, IReadOnlyList<int> NotDescribed)
{
    public static DiscoveryOmissions None { get; } = new([], []);
}
