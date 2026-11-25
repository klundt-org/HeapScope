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


using HeapScope.Contracts;

namespace HeapScope.Application.Presentation;

/// <summary>What the app and the command line say about the allocations between two snapshots (sprint 4.4).</summary>
public static class AllocatedBetweenTexts
{
    public const string NotOneSession = "The two snapshots are not in one session folder, so no recorded run is known to lie between them. Capture both on the Live page with Record allocations ticked.";

    public const string NotOneRun = "The two snapshots were not captured during one monitoring run, so no recording lies between them. Capture both on the Live page with Record allocations ticked.";

    public const string NotEnded = "The monitoring run of these snapshots has not ended. Stop it, then compare again: its allocation trace is read when it is complete.";

    public const string NoTrace = "The monitoring run of these snapshots did not record allocations. Tick Record allocations on the Live page before Start.";

    public const string Late = "A gcdump here was captured by a version before 4.4, which recorded when its file was done: what was allocated in the seconds between counts on the wrong side of it.";

    public const string TickFirst = "Tick Allocated between first.";

    public const string NoSample = "No allocation of this row was sampled between the snapshots.";

    public const string TraceClosed = "The allocation trace was closed on the Allocations page. Tick Allocated between again to open it.";

    public static string NotRecorded(string note) => "The monitoring run of these snapshots has no allocation trace: " + note;

    /// <summary>What was counted, and what no row holds.</summary>
    public static string Summary(DiffAllocationInfo info, Func<long, string> bytes, IFormatProvider culture)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(bytes);
        var parts = new List<string>
        {
            string.Create(culture, $"About {bytes(info.Bytes)} allocated between the snapshots ({info.Samples:N0} samples, {info.ToSeconds - info.FromSeconds:N1} s), collected objects included."),
        };
        if (info.AbsentBytes > 0)
        {
            parts.Add(string.Create(culture, $"{bytes(info.AbsentBytes)} of it in {info.AbsentTypes:N0} types without an object in either snapshot, so without a row."));
        }

        if (info.SharedNameBytes > 0)
        {
            parts.Add(string.Create(culture, $"{bytes(info.SharedNameBytes)} of type names several modules share, which a gcdump cannot tell apart."));
        }

        if (info.AskedFromSeconds < 0)
        {
            parts.Add(string.Create(culture, $"The recording started {-info.AskedFromSeconds:N1} s after the earlier snapshot: what was allocated before is not counted."));
        }

        if (info.AskedToSeconds > info.RecordedSeconds)
        {
            parts.Add(string.Create(culture, $"The recording ended {info.AskedToSeconds - info.RecordedSeconds:N1} s before the later snapshot: what was allocated after is not counted."));
        }

        if (info.LostEvents > 0)
        {
            parts.Add(string.Create(culture, $"The runtime dropped {info.LostEvents:N0} events while recording, so the figures are low."));
        }

        return string.Join(" ", parts);
    }
}
