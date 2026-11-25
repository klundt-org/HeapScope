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
using HeapScope.Application.Allocations;
using HeapScope.Application.Presentation;
using HeapScope.Application.Sessions;
using HeapScope.Contracts;

namespace HeapScope.Application.Tests;

/// <summary>Which allocation trace lies between two snapshots, and what is said when none does (sprint 4.4).</summary>
public sealed class AllocatedBetweenTests : IDisposable
{
    private static readonly DateTimeOffset s_start = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));
    private readonly SessionLibrary _library;
    private readonly string _folder;

    public AllocatedBetweenTests()
    {
        _library = new SessionLibrary(_root);
        _folder = _library.Current.Folder;
        File.WriteAllText(Path.Combine(_folder, "run.nettrace"), "trace");
    }

    [Fact]
    public void The_run_that_watched_the_process_meanwhile_is_found_with_the_heaps_times()
    {
        var first = Snapshot("a.dmp", 10, heap: 9);
        var second = Snapshot("b.gcdump", 30, heap: 25, kind: SnapshotKindDto.GcDump);
        var run = Run();

        var found = AllocatedBetween.Find(first, second);

        (found.TracePath, found.RunId, found.FromUtc, found.ToUtc, found.NoHeapTime, found.Reason).ShouldBe(
            (Path.Combine(_folder, "run.nettrace"), run, s_start.AddSeconds(9), s_start.AddSeconds(25), false, (string?)null));
        found.Evidence.ShouldContain("its heap's time");
        found.Evidence.ShouldNotContain("reverse order");
        var reversed = AllocatedBetween.Find(second, first);
        (reversed with { Evidence = found.Evidence }).ShouldBe(found, "the interval is the same whichever is the baseline");
        reversed.Evidence.ShouldContain("in reverse order");
    }

    [Fact]
    public void A_heap_whose_time_was_not_recorded_is_marked()
    {
        var first = Snapshot("a.dmp", 10);
        var second = Snapshot("b.gcdump", 30, kind: SnapshotKindDto.GcDump);
        Run();

        var found = AllocatedBetween.Find(first, second);

        (found.NoHeapTime, found.ToUtc).ShouldBe((true, s_start.AddSeconds(30)));
        found.Evidence.ShouldContain("its heap's time is not known");
    }

    [Fact]
    public void Each_reason_is_said()
    {
        var first = Snapshot("a.dmp", 10);
        var second = Snapshot("b.dmp", 30);
        var other = Snapshot("other.dmp", 20, processId: 8);
        Reason(first, Path.Combine(_root, "elsewhere.dmp")).ShouldBe(AllocatedBetweenTexts.NotOneSession);
        Reason(first, Path.Combine(_folder, "unknown.dmp")).ShouldBe(AllocatedBetweenTexts.NotOneSession);
        Reason(first, second).ShouldBe(AllocatedBetweenTexts.NotOneRun);

        // Review H1: the record of a running run has its trace's name already; a run that does not record has none.
        var run = Run(ended: null, trace: "run.nettrace");
        Reason(first, second).ShouldBe(AllocatedBetweenTexts.NotEnded);
        Reason(first, other).ShouldBe(AllocatedBetweenTexts.NotOneRun);
        AllocatedBetween.Find(first, other).Evidence.ShouldContain("another process");
        Run(run, ended: null, trace: null);
        Reason(first, second).ShouldBe(AllocatedBetweenTexts.NoTrace);

        Run(run, ended: 60, trace: null);
        Reason(first, second).ShouldBe(AllocatedBetweenTexts.NoTrace);
        Run(run, ended: 60, trace: null, note: "The process exited.");
        Reason(first, second).ShouldBe(AllocatedBetweenTexts.NotRecorded("The process exited."));

        // A received session's paths are not followed out of its folder.
        Run(run, ended: 60, trace: Path.Combine("..", "..", "run.nettrace"));
        Reason(first, second)!.ShouldContain("outside its session folder");
        Run(run, ended: 60, trace: @"\\server\share\run.nettrace");
        Reason(first, second)!.ShouldContain("another machine");
        Run(run, ended: 60, trace: "gone.nettrace");
        Reason(first, second)!.ShouldContain("not there any more");
        Run(run, ended: 20, trace: "run.nettrace");
        Reason(first, second).ShouldBe(AllocatedBetweenTexts.NotOneRun, "the run ended before the second snapshot");
        AllocatedBetween.Find(first, second).Evidence.ShouldContain("ended before the later heap");
        Run(run, ended: 60, trace: "run.nettrace");
        AllocatedBetween.Find(first, second).Reason.ShouldBeNull();
    }

    /// <summary>Review M5: a run HeapScope did not live to end fits every later pair; the later run that ended has the trace.</summary>
    [Fact]
    public void A_later_run_that_ended_is_taken_before_one_whose_end_was_not_recorded()
    {
        var first = Snapshot("a.dmp", 10, heap: 10);
        var second = Snapshot("b.dmp", 30, heap: 30);
        File.WriteAllText(Path.Combine(_folder, "later.nettrace"), "trace");
        Run(ended: null, trace: "run.nettrace");
        var later = Run(trace: "later.nettrace", started: 5, shift: 2.5);

        var found = AllocatedBetween.Find(first, second);

        (found.RunId, Path.GetFileName(found.TracePath), found.ClockShiftSeconds).ShouldBe((later, "later.nettrace", 2.5));
        found.Evidence.ShouldContain("not ended");
    }

    /// <summary>Review M6: a session that cannot be read says so, not that the snapshots are elsewhere.</summary>
    [Fact]
    public void A_session_that_cannot_be_read_says_why()
    {
        var first = Snapshot("a.dmp", 10);
        var second = Snapshot("b.dmp", 30);
        File.WriteAllText(Path.Combine(_folder, "session.json"), "{ damaged");
        var read = SessionLibrary.Read(_folder);

        Reason(first, second).ShouldBe($"{read.Reason} {read.NextStep}");
    }

    [Fact]
    public void The_summary_says_what_was_counted_and_what_was_not()
    {
        static string Bytes(long b) => b.ToString(CultureInfo.InvariantCulture) + " B";
        var whole = new DiffAllocationInfo(2, 12.5, 2, 12.5, 20, 40, 4_000, 4_000, 0, 0, 0, 0);

        AllocatedBetweenTexts.Summary(whole, Bytes, CultureInfo.InvariantCulture)
            .ShouldBe("About 4000 B allocated between the snapshots (40 samples, 10.5 s), collected objects included.");
        AllocatedBetweenTexts.Summary(whole with { AskedFromSeconds = -1.5, AskedToSeconds = 22, AbsentBytes = 10, AbsentTypes = 2, SharedNameBytes = 5, LostEvents = 3, NameBytes = 20, UnnamedTypes = 4, ConversionIssues = 1 }, Bytes, CultureInfo.InvariantCulture, -2.5).ShouldBe(
            "About 4000 B allocated between the snapshots (40 samples, 10.5 s), collected objects included."
            + " 10 B of it in 2 types without an object in either snapshot, so without a row."
            + " 5 B of type names that several types share, which a gcdump cannot tell apart, so without a row."
            + " 20 B of it is matched by name only, as a gcdump has nothing else: it names a nested type without its declaring type, so a row can hold what another type of its name allocated."
            + " 4 types of a gcdump here have no name (another tool may have traced the process), so what was allocated of them has no row."
            + " The machine's clock moved 2.5 s against the recording during the run (it slept, or its clock was set), so the interval may be off by as much."
            + " The recording started 1.5 s after the earlier snapshot: what was allocated before is not counted."
            + " The recording ended 2.0 s before the later snapshot: what was allocated after is not counted."
            + " The runtime dropped 3 events while recording, so the figures may be low."
            + " " + AllocationTexts.Damaged(1));
    }

    private static string? Reason(string first, string second) => AllocatedBetween.Find(first, second).Reason;

    private string Snapshot(string file, int captured, int? heap = null, int processId = 7, SnapshotKindDto kind = SnapshotKindDto.FullDump)
    {
        var path = Path.Combine(_folder, file);
        File.WriteAllText(path, file);
        _library.AddTo(_folder, new SessionSnapshot(Guid.NewGuid(), path, "app", processId, "10.0.0", s_start.AddSeconds(captured), 4, Kind: kind, HeapUtc: heap is { } h ? s_start.AddSeconds(h) : null));
        return path;
    }

    private Guid Run(Guid? id = null, int? ended = 60, string? trace = "run.nettrace", string? note = null, int started = 0, double? shift = null)
    {
        var run = id ?? Guid.NewGuid();
        _library.RecordRun(_folder, new SessionRun(run, "app", 7, 1, "", SnapshotKindDto.FullDump, s_start.AddSeconds(started), ended is { } e ? s_start.AddSeconds(e) : null, null, trace, [], TraceNote: note, ClockShiftSeconds: shift));
        return run;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
