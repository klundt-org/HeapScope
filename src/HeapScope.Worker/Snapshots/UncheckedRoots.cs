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

namespace HeapScope.Worker.Snapshots;

/// <summary>
/// Why a snapshot's roots are not all known, so an object held only by one of them can look unreachable (peer review 4.5, sprint
/// 4.7): <paramref name="Reason"/> as a clause ("not all GC handles could be checked for this snapshot") and <paramref name="HeldBy"/>
/// as what such an object is held by ("a handle").
/// </summary>
internal sealed record UncheckedRoots(string Reason, string HeldBy)
{
    /// <summary>The GC handles of a Server GC process could not all be checked (<c>SnapshotFacts.HandlesChecked</c> missing).</summary>
    public static readonly UncheckedRoots Handles = new("not all GC handles could be checked for this snapshot", "a handle");

    /// <summary>No static field could be read (<c>SnapshotFacts.StaticsUnread</c>): a heap dump of .NET 8 or before leaves them out.</summary>
    public static readonly UncheckedRoots Statics = new("the static fields of this dump could not be read", "a static field");

    /// <summary><see cref="Reason"/> as the start of a sentence.</summary>
    public string Sentence => string.Concat(Reason[..1].ToUpperInvariant(), Reason[1..]);
}
