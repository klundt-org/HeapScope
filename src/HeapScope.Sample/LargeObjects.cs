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

namespace HeapScope.Sample;

/// <summary>
/// The large objects scenario: three arrays of readings, each 160 KB and so on the large object heap,
/// beside 500 small arrays of tags that are not; a static list holds them all. The Large arrays and Large object heap
/// inspections list the readings' arrays, not the tags'.
/// </summary>
internal static class LargeObjects
{
    /// <summary>The type a lesson's answer names: the arrays on the large object heap.</summary>
    public const string AnswerType = "HeapScope.Sample.Reading[]";

    /// <summary>The small arrays' type, a decoy.</summary>
    public const string SmallType = "HeapScope.Sample.Tag[]";

    private static readonly List<Array> s_held = [];

    public static void Start()
    {
        for (var i = 0; i < 3; i++)
        {
            var readings = new Reading[10_000];
            for (var j = 0; j < readings.Length; j++)
            {
                readings[j] = new Reading(j, j * 0.5);
            }

            s_held.Add(readings);
        }

        for (var i = 0; i < 500; i++)
        {
            s_held.Add(new Tag[8]);
        }
    }
}

/// <summary>One reading: 16 bytes, so 10,000 of them make an array of about 160 KB.</summary>
internal readonly record struct Reading(long Ticks, double Value);

/// <summary>One tag of a small array.</summary>
internal readonly record struct Tag(int Id, int Value);
