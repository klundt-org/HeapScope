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
using HeapScope.Application.Presentation;
using HeapScope.Application.Sessions;
using HeapScope.Contracts;

namespace HeapScope.Application.Allocations;

/// <summary>
/// The allocation trace that lies between two snapshots: that of the monitoring run during which both were captured,
/// from their session's record. Either <see cref="TracePath"/> or <see cref="Reason"/> is set.
/// </summary>
/// <param name="FromUtc">When the earlier heap was as its snapshot holds it.</param>
/// <param name="ToUtc">When the later one was.</param>
/// <param name="NoHeapTime">The time of one of the heaps was not recorded: when its file was done counts instead.</param>
/// <param name="Reason">Why there is no such trace, in words for the user.</param>
/// <param name="Evidence">What was found, for the log: the heaps' times and where they are from, the run or why each run was left out.</param>
public sealed record AllocatedBetween(string? TracePath, Guid RunId, DateTimeOffset FromUtc, DateTimeOffset ToUtc, bool NoHeapTime, string? Reason, string Evidence)
{
    /// <summary>How far the machine's clock moved against the recording's during the run, when it was recorded.</summary>
    public double ClockShiftSeconds { get; init; }

    /// <summary>Runs described in the log.</summary>
    private const int LoggedRuns = 5;

    /// <summary>Looks in the session folder that holds both snapshot files. Reads the disk: not for the UI thread.</summary>
    public static AllocatedBetween Find(string baselinePath, string currentPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(baselinePath);
        ArgumentException.ThrowIfNullOrEmpty(currentPath);
        var folder = Path.GetDirectoryName(Path.GetFullPath(baselinePath));
        if (folder is null || !PathIdentity.Same(folder, Path.GetDirectoryName(Path.GetFullPath(currentPath)) ?? string.Empty))
        {
            return None(AllocatedBetweenTexts.NotOneSession, "they are in different folders");
        }

        var read = SessionLibrary.Read(folder);
        if (read.Session is not { } session)
        {
            return read.Status == SessionReadStatus.Unreadable
                ? None(AllocatedBetweenTexts.Unreadable(read.Reason ?? "no reason was given"), $"the session of {folder} could not be read: {read.Reason}")
                : None(AllocatedBetweenTexts.NotOneSession, $"{folder} has no session");
        }

        if (SnapshotAt(session, baselinePath) is not { } baseline)
        {
            return None(AllocatedBetweenTexts.NotOneSession, $"the session of {folder} does not record the baseline among its {session.Document.Snapshots.Count} snapshots, at a path on this machine ({session.Document.UnknownSnapshots.Count} of a newer version set aside)");
        }

        if (SnapshotAt(session, currentPath) is not { } current)
        {
            return None(AllocatedBetweenTexts.NotOneSession, $"the session of {folder} does not record the current snapshot among its {session.Document.Snapshots.Count} snapshots, at a path on this machine ({session.Document.UnknownSnapshots.Count} of a newer version set aside)");
        }

        var heaps = string.Create(CultureInfo.InvariantCulture, $"baseline {Describe(baseline)}, current {Describe(current)}{(Heap(baseline) <= Heap(current) ? string.Empty : ", in reverse order")}");
        var (from, to) = Heap(baseline) <= Heap(current) ? (Heap(baseline), Heap(current)) : (Heap(current), Heap(baseline));
        var runs = session.Document.Runs ?? [];

        // A process id stays one process for as long as a run watches it: the run ends when the process does. A run whose end was not
        // recorded (HeapScope ended first) says nothing about what came after, so a later run that ended is taken before it.
        var left = runs.Select(r => (Run: r, Why: WhyNot(r, baseline, current, from, to))).ToList();
        var evidence = heaps + "; runs: " + (runs.Count == 0 ? "none" : string.Join("; ", left.OrderBy(r => r.Why is not null).ThenBy(r => r.Run.ProcessId != baseline.ProcessId).ThenBy(r => (r.Run.StartedUtc - from).Duration()).Take(LoggedRuns).Select(r => Describe(r.Run, r.Why)))
            + (runs.Count > LoggedRuns ? string.Create(CultureInfo.InvariantCulture, $"; {runs.Count - LoggedRuns} more") : string.Empty)
            + (session.Document.UnknownRuns.Count == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"; {session.Document.UnknownRuns.Count} of a newer version set aside")));
        var matching = left.Where(r => r.Why is null).Select(r => r.Run).OrderByDescending(r => r.StartedUtc).ToList();
        if (matching.Count == 0)
        {
            return None(AllocatedBetweenTexts.NotOneRun, evidence);
        }

        if (matching.FirstOrDefault(r => r.EndedUtc is not null && r.Trace is not null) is not { } run)
        {
            var reason = matching.Where(r => r.EndedUtc is not null).Select(r => Plain(r.TraceNote) is { } note ? AllocatedBetweenTexts.NotRecorded(note) : AllocatedBetweenTexts.NoTrace).FirstOrDefault()
                ?? (matching.Any(r => r.Trace is not null) ? AllocatedBetweenTexts.NotEnded : AllocatedBetweenTexts.NoTrace);
            return None(reason, evidence);
        }

        var check = Session.RunFiles(session.Folder, run).Trace;
        evidence += string.Create(CultureInfo.InvariantCulture, $"; taken {Describe(run, null)}, clock shift {run.ClockShiftSeconds?.ToString("F3", CultureInfo.InvariantCulture) ?? "not recorded"} s");
        return check.Path is { } trace
            ? new AllocatedBetween(trace, run.Id, from, to, baseline.HeapUtc is null || current.HeapUtc is null, null, evidence) { ClockShiftSeconds = run.ClockShiftSeconds ?? 0 }
            : None(check.Describe("The run's allocation trace"), $"{evidence}; its trace {Plain(run.Trace)} was refused ({check.Refusal})");
    }

    private static AllocatedBetween None(string reason, string evidence) => new(null, Guid.Empty, default, default, false, reason, evidence);

    private static DateTimeOffset Heap(SessionSnapshot snapshot) => snapshot.HeapUtc ?? snapshot.CapturedUtc;

    private static string? WhyNot(SessionRun run, SessionSnapshot baseline, SessionSnapshot current, DateTimeOffset from, DateTimeOffset to) =>
        baseline.ProcessId != run.ProcessId || current.ProcessId != run.ProcessId ? "another process"
        : run.StartedUtc > from ? "started after the earlier heap"
        : run.EndedUtc is { } end && to > end ? "ended before the later heap"
        : null;

    private static string Describe(SessionSnapshot snapshot) => string.Create(
        CultureInfo.InvariantCulture,
        $"{snapshot.Id} ({snapshot.Kind}, process {snapshot.ProcessId}) as of {Heap(snapshot):O} ({(snapshot.HeapUtc is null ? "when it was recorded: its heap's time is not known" : "its heap's time")})");

    private static string Describe(SessionRun run, string? why) => string.Create(
        CultureInfo.InvariantCulture,
        $"{run.Id} of process {run.ProcessId}, {run.StartedUtc:O} to {(run.EndedUtc is { } end ? end.ToString("O", CultureInfo.InvariantCulture) : "not ended")}, trace {Plain(run.Trace) ?? "none"}{(Plain(run.TraceNote) is { } note ? $" ({note})" : string.Empty)}: {why ?? "fits"}");

    // A session may be someone else's: what it says stays on one line of the log, and short.
    private static string? Plain(string? recorded) =>
        recorded is null ? null : new string([.. recorded.Take(300).Select(static c => char.IsControl(c) ? ' ' : c)]);

    private static SessionSnapshot? SnapshotAt(Session session, string path) =>
        session.Document.Snapshots.FirstOrDefault(s => Session.IsLocal(session.PathOf(s)) && PathIdentity.Same(Path.GetFullPath(session.PathOf(s)), Path.GetFullPath(path)));
}
