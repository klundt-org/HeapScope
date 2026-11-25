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
    public const string NotOneSession = "The two snapshots are not in one session folder, so no recorded run is known to lie between them. Capture both while a run on the Live page records allocations.";

    public const string NotOneRun = "The two snapshots were not captured during one monitoring run, so no recording lies between them. Capture both while a run on the Live page records allocations.";

    public const string NotEnded = "The monitoring run of these snapshots has not ended, or HeapScope closed before its end was recorded. If it runs, stop it and compare again. If not, open its trace on the Allocations page.";

    public const string NoTrace = "The monitoring run of these snapshots did not record allocations. Tick Record allocations on the Live page before Start.";

    public const string NoHeapTime = "When a heap here was captured was not recorded (a version before 4.4, or its events did not say), so when its file was done counts: what was allocated in between, for a gcdump some seconds, counts on the wrong side of it.";

    public const string TickFirst = "Tick Allocated between first.";

    public const string StillReading = "The allocation trace is still being read.";

    public const string ReadCanceled = "The allocation trace was not read: its job was canceled. Untick and tick Allocated between to read it.";

    public const string EveryType = "This row is of several types in the trace, so the Allocations page shows every type of the interval.";

    public const string NoSample = "No allocation of this row was sampled between the snapshots.";

    public const string TraceClosed = "The allocation trace was closed on the Allocations page. Tick Allocated between again to open it.";

    public static string Unreadable(string reason) => "The session of these snapshots cannot be read (" + reason + "), so no recorded run is known to lie between them. Close programs that may hold its session.json and try again, or open the run's trace on the Allocations page.";

    /// <summary>On the Allocations page, for a row of a nested type: the trace has only its innermost name.</summary>
    public static string EveryOfName(string name) => "The trace names a nested type by its own name only, so this shows every type named " + name + ", which can be more than the row in Compare.";

    public static string NotRecorded(string note) => "The monitoring run of these snapshots has no allocation trace: " + note;

    /// <summary>What was counted, and what no row holds.</summary>
    /// <param name="clockShiftSeconds">How far the machine's clock moved against the recording's during the run.</param>
    public static string Summary(DiffAllocationInfo info, Func<long, string> bytes, IFormatProvider culture, double clockShiftSeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(bytes);
        var parts = new List<string>
        {
            string.Create(culture, $"About {bytes(info.Bytes)} allocated between the snapshots ({info.Samples:N0} samples, {info.ToSeconds - info.FromSeconds:N1} s), collected objects included."),
        };
        if (info.AbsentBytes > 0)
        {
            parts.Add(string.Create(culture, $"{bytes(info.AbsentBytes)} of it in {info.AbsentTypes:N0} types {(info.UnnamedTypes > 0 ? "not found" : "without an object")} in either snapshot, so without a row."));
        }

        if (info.SharedNameBytes > 0)
        {
            parts.Add(string.Create(culture, $"{bytes(info.SharedNameBytes)} of type names that several types share, which a gcdump cannot tell apart, so without a row."));
        }

        if (info.NameBytes > 0)
        {
            parts.Add(string.Create(culture, $"{bytes(info.NameBytes)} of it is matched by name only, as a gcdump has nothing else: it names a nested type without its declaring type, so a row can hold what another type of its name allocated."));
        }

        if (info.UnnamedTypes > 0)
        {
            parts.Add(string.Create(culture, $"{info.UnnamedTypes:N0} types of a gcdump here have no name (another tool may have traced the process), so what was allocated of them has no row."));
        }

        if (Math.Abs(clockShiftSeconds) >= 1)
        {
            parts.Add(string.Create(culture, $"The machine's clock moved {Math.Abs(clockShiftSeconds):N1} s against the recording during the run (it slept, or its clock was set), so the interval may be off by as much."));
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
            parts.Add(string.Create(culture, $"The runtime dropped {info.LostEvents:N0} events while recording, so the figures may be low."));
        }

        if (info.ConversionIssues > 0)
        {
            parts.Add(AllocationTexts.Damaged(info.ConversionIssues));
        }

        return string.Join(" ", parts);
    }
}
