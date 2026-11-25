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


using HeapScope.Application.Presentation;
using HeapScope.Application.Sessions;
using HeapScope.Contracts;

namespace HeapScope.Application.Allocations;

/// <summary>
/// The allocation trace that lies between two snapshots (sprint 4.4): that of the monitoring run during which both were captured,
/// from their session's record. Either <see cref="TracePath"/> or <see cref="Reason"/> is set.
/// </summary>
/// <param name="FromUtc">When the earlier heap was as its snapshot holds it.</param>
/// <param name="ToUtc">When the later one was.</param>
/// <param name="Late">A gcdump captured before 4.4 is one of them: its recorded time is its file's, seconds after its heap.</param>
/// <param name="Reason">Why there is no such trace, in words for the user.</param>
public sealed record AllocatedBetween(string? TracePath, Guid RunId, DateTimeOffset FromUtc, DateTimeOffset ToUtc, bool Late, string? Reason)
{
    /// <summary>Looks in the session folder that holds both snapshot files. Reads the disk: not for the UI thread.</summary>
    public static AllocatedBetween Find(string baselinePath, string currentPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(baselinePath);
        ArgumentException.ThrowIfNullOrEmpty(currentPath);
        var folder = Path.GetDirectoryName(Path.GetFullPath(baselinePath));
        if (folder is null || !PathIdentity.Same(folder, Path.GetDirectoryName(Path.GetFullPath(currentPath)) ?? string.Empty)
            || SessionLibrary.TryRead(folder) is not { } session
            || SnapshotAt(session, baselinePath) is not { } baseline
            || SnapshotAt(session, currentPath) is not { } current)
        {
            return None(AllocatedBetweenTexts.NotOneSession);
        }

        var (from, to) = Heap(baseline) <= Heap(current) ? (Heap(baseline), Heap(current)) : (Heap(current), Heap(baseline));

        // A process id stays one process for as long as a run watches it: the run ends when the process does.
        var runs = (session.Document.Runs ?? [])
            .Where(r => baseline.ProcessId == r.ProcessId && current.ProcessId == r.ProcessId && r.StartedUtc <= from && (r.EndedUtc is null || to <= r.EndedUtc))
            .ToList();
        if (runs.Count == 0)
        {
            return None(AllocatedBetweenTexts.NotOneRun);
        }

        if (runs.FirstOrDefault(r => r.Trace is not null) is not { } run)
        {
            return None(runs.Any(r => r.EndedUtc is null)
                ? AllocatedBetweenTexts.NotEnded
                : runs.Select(r => r.TraceNote).FirstOrDefault(n => n is not null) is { } note ? AllocatedBetweenTexts.NotRecorded(note) : AllocatedBetweenTexts.NoTrace);
        }

        var check = Session.RunFiles(session.Folder, run).Trace;
        return check.Path is { } trace
            ? new AllocatedBetween(trace, run.Id, from, to, IsLate(baseline) || IsLate(current), null)
            : None(check.Describe("The run's allocation trace"));
    }

    private static AllocatedBetween None(string reason) => new(null, Guid.Empty, default, default, false, reason);

    private static DateTimeOffset Heap(SessionSnapshot snapshot) => snapshot.HeapUtc ?? snapshot.CapturedUtc;

    private static bool IsLate(SessionSnapshot snapshot) => snapshot.HeapUtc is null && snapshot.Kind == SnapshotKindDto.GcDump;

    private static SessionSnapshot? SnapshotAt(Session session, string path) =>
        session.Document.Snapshots.FirstOrDefault(s => Session.IsLocal(session.PathOf(s)) && PathIdentity.Same(Path.GetFullPath(session.PathOf(s)), Path.GetFullPath(path)));
}
