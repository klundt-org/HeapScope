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

using System.Globalization;

namespace HeapScope.Worker.Snapshots;

/// <summary>
/// The .NET versions whose dumps HeapScope checks against its known answers on every change (sprint 4.7, peer review 4.5 H1;
/// ADR-002): the fixtures run on each in CI. A dump of another version opens, with a note that its answers are not checked.
/// </summary>
internal static class TestedRuntimes
{
    /// <summary>The major versions tested: keep in step with the fixtures' TargetFrameworks and CI's runtimes.</summary>
    public static readonly IReadOnlyList<int> Majors = [8, 9, 10];

    /// <summary>The open note for a dump of <paramref name="runtimeVersion"/>, or null for a tested or unknown version.</summary>
    public static string? Note(string? runtimeVersion)
    {
        if (Major(runtimeVersion) is not { } major || Majors.Contains(major))
        {
            return null;
        }

        // .NET Framework's runtime is 4.x, and .NET Core 3.x's file version 4.700.x: neither is what HeapScope analyzes (ADR-002).
        if (major < 5)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"This dump's runtime reads as version {runtimeVersion}, older than .NET 5: HeapScope analyzes .NET 5 and later, not .NET Framework or .NET Core 3. Results may be wrong.");
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"This dump is of .NET {runtimeVersion}. HeapScope checks its answers on dumps of .NET {Majors[0]} to {Majors[^1]}; it reads other versions too, but has not checked them, so a result that looks wrong may be HeapScope's. Its log names what it read, for an issue on HeapScope's site.");
    }

    internal static int? Major(string? runtimeVersion)
    {
        var text = runtimeVersion.AsSpan().Trim();
        var dot = text.IndexOf('.');
        return int.TryParse(dot < 0 ? text : text[..dot], NumberStyles.None, CultureInfo.InvariantCulture, out var major) && major > 0 ? major : null;
    }
}
