# HeapScope Sprints

Phases: **0.x** architecture, framework and skeleton; **1.x** quick wins; **2.x** heavy hitters; **3.x** depth to
match dotMemory (planned 2026-10-28); **4.x** what Phase 3 leaves out (planned 2026-11-18); the Backlog ([BACKLOG.md](BACKLOG.md)) everything else. Closed sprints are
in [sprints/](sprints/): [Phase 0](sprints/phase-0.md), [Phase 1](sprints/phase-1.md),
[Phase 2](sprints/phase-2.md), [Phase 3, the peer review sprints, 3.9 to 3.12](sprints/phase-3.md), [Phase 4](sprints/phase-4.md).

## Status

| Sprint | Title | Status | Closed | Tag |
|---|---|---|---|---|
| 0.0 | Project charter, architecture and process | Closed | 2026-10-22 | sprint-0.0 |
| 0.1 | Solution skeleton | Closed | 2026-10-22 | sprint-0.1 |
| 0.2 | Snapshot abstraction and compact graph | Closed | 2026-10-22 | sprint-0.2 |
| 0.3 | Worker process and IPC | Closed | 2026-10-22 | sprint-0.3 |
| 0.4 | Fixtures and test harness | Closed | 2026-10-22 | sprint-0.4 |
| 0.5 | Index cache format | Closed | 2026-10-22 | sprint-0.5 |
| 1.2 | Full dump loader | Closed | 2026-10-23 | sprint-1.2 |
| 1.1 | Process discovery and capture | Closed | 2026-10-23 | sprint-1.1 |
| 1.3 | Heap summary by type | Closed | 2026-10-23 | sprint-1.3 |
| 1.4 | Snapshot diff | Closed | 2026-10-23 | sprint-1.4 |
| 1.5 | Object search and inspection | Closed | 2026-10-23 | sprint-1.5 |
| 1.6 | Simple inspections | Closed | 2026-10-23 | sprint-1.6 |
| 1.7 | Live counters and timed snapshots | Closed | 2026-10-23 | sprint-1.7 |
| 1.8 | gcdump support | Closed | 2026-10-24 | sprint-1.8 |
| 1.9 | Phase 1 hardening | Closed | 2026-10-24 | sprint-1.9 |
| 2.1 | Reverse references | Closed | 2026-10-24 | sprint-2.1 |
| 2.2 | Retention paths | Closed | 2026-10-24 | sprint-2.2 |
| 2.3 | Dominator tree and retained size | Closed | 2026-10-25 | sprint-2.3 |
| 2.3.5 | Cross-platform and sharing decision | Closed | 2026-10-25 | sprint-2.3.5 |
| 2.3.6 | Avalonia port | Closed | 2026-10-26 | sprint-2.3.6 |
| 2.3.7 | Port peer review | Closed | 2026-10-26 | sprint-2.3.7 |
| 2.4 | Retention explorer UI | Closed | 2026-10-26 | sprint-2.4 |
| 2.5 | Allocation tracking | Closed | 2026-10-26 | sprint-2.5 |
| 2.6 | Smart leak detectors | Closed | 2026-10-27 | sprint-2.6 |
| 2.7 | Navigation and polish | Closed | 2026-10-27 | sprint-2.7 |
| 3.1 | Object sets | Closed | 2026-10-28 | sprint-3.1 |
| 3.2 | Retention views | Closed | 2026-10-28 | sprint-3.2 |
| 3.3 | Comparison depth | Closed | 2026-10-29 | sprint-3.3 |
| 3.4 | Live workflow and housekeeping | Closed | 2026-10-29 | sprint-3.4 |
| 3.5 | Leak detection depth | Closed | 2026-10-29 | sprint-3.5 |
| 3.6 | Urgent fixes | Closed | 2026-10-29 | sprint-3.6 |
| 3.7 | Hardening | Closed | 2026-10-30 | sprint-3.7 |
| 3.8 | Modularity | Closed | 2026-10-30 | sprint-3.8 |
| 3.9 | Packaging and distribution | Closed | 2026-11-01 | sprint-3.9 |
| 3.10 | Visual design and README | Closed | 2026-11-05 | sprint-3.10 |
| 3.11 | Screens that explain themselves | Closed | 2026-11-10 | sprint-3.11 |
| 3.12 | First-show performance and refactor analysis | Closed | 2026-11-15 | sprint-3.12 |
| 3.13 | Tutorial | Closed | 2026-11-15 | sprint-3.13 |
| 3.13.2 | Dump values in logs, by consent | Closed | 2026-11-19 | sprint-3.13.2 |
| 4.1 | Capture on Linux | Closed | 2026-11-20 | sprint-4.1 |
| 4.1.1 | Hide processes HeapScope cannot capture | Closed | 2026-11-21 | sprint-4.1.1 |
| 4.2 | Capture on macOS | Closed | 2026-11-23 | sprint-4.2 |
| 4.2.1 | Patch: damaged gcdumps and traces | Closed | 2026-11-24 | sprint-4.2.1 |
| 4.3 | Live session | Closed | 2026-11-25 | sprint-4.3 |
| 4.4 | Allocated between two snapshots | Closed | 2026-11-25 | sprint-4.4 |
| 4.5 | Peer review | Closed | 2026-11-25 | sprint-4.5 |
| 4.6 | Right answers | In Progress | | |
| 4.7 | Runtimes, Linux and large heaps | Planned | | |
| 4.8 | Ready to release | Planned | | |
| 4.9 | Workspaces | Planned | | |
| 4.10 | Analysis tabs | Planned | | |
| 4.11 | Sunburst | Planned | | |
| 4.12 | WPF leak detectors (optional) | Planned | | |

---

## Sprint 3.9: Packaging and distribution

Closed 2026-11-01; its section is in [sprints/phase-3.md](sprints/phase-3.md) and its review in
[reviews/sprint-3.9.md](reviews/sprint-3.9.md). ADR-039 records the distribution decisions.

---

## Sprint 3.10: Visual design and README

Closed 2026-11-05; its section is in [sprints/phase-3.md](sprints/phase-3.md) and its review in
[reviews/sprint-3.10.md](reviews/sprint-3.10.md). ADR-040 records the visual design.

---

## Sprint 3.11: Screens that explain themselves

Closed 2026-11-10; its section is in [sprints/phase-3.md](sprints/phase-3.md) and its review in
[reviews/sprint-3.11.md](reviews/sprint-3.11.md). ADR-040 records the info icons, and ADR-032 the match of pinned objects.

## Peer review sprints

3.6, 3.7 and 3.8 worked through `docs/reviews/peer-review-3.5.md`; all closed ([sprints/phase-3.md](sprints/phase-3.md)).

---

## Phase 4

Planned 2026-11-18 with the product owner from the Phase 4 candidates (what Phase 3 leaves out). Each sprint adds its
scenario to the sample and its lesson to the tutorial where it has one (sprint 3.13's extension contract, ADR-042).
Owner decisions, 2026-11-18:
- No Mac to test on: macOS is built and checked on Linux as far as it can be, and marked untested, like the macOS packages.
- The same autonomy as Phase 3: green sprint PRs are merged and tagged and the next sprint starts; costly or irreversible
  steps, user-facing wording and releases still go to the owner. At the end of the phase, stop and report.
- Releases after 4.2 (capture everywhere), after 4.4 (the live session and Compare) and at the end of the phase. 2026-11-25: 4.5.0 is released
  in place of 4.4.0, as a pre-release, and its CLI goes to nuget.org; the first full release comes after 4.8 and a trial by people outside.
- A peer review after 4.4, before the UI-heavy sprints, like 3.5's.
- The checks by hand (DEVELOPER.md 5) are automated as far as they can be, first, in 4.1.

| Sprint | Title | Size | What |
|---|---|---|---|
| 4.1 | Capture on Linux | L | Process list, full dumps and gcdumps, live counters and allocation recording on Linux, through the runtime's diagnostics channel; permission and ptrace rules; the tutorial on Linux; the checks by hand automated. |
| 4.1.1 | Hide processes HeapScope cannot capture | S | Owner, 2026-11-20: the Processes list hides the processes that need more rights than HeapScope has ("Needs administrator", on Linux "Needs root or its user"), with a check box to show them; when HeapScope runs elevated (as root) it can capture them, so all are shown. The list keeps marking which processes are elevated or another user's. |
| 4.2 | Capture on macOS | M | The same on macOS, built and checked on Linux, marked untested. |
| 4.2.1 | Patch: damaged gcdumps and traces | S | Owner, 2026-11-24: the nightly fuzz test failed on 2026-11-20, 22 and 23 (seeds 20, 22 and 23). A gcdump states the node its root tree starts at, and the loader did not check it: the reader's own error refused such a file, until the JIT had inlined the accessor that throws after many loads in one process, and then it was reported as a HeapScope error. Fixed and on `develop` (48,000 mutated files over 8 seeds since, none failing). Added 2026-11-24 (owner): a trace states sizes and counts too, and they are checked before it is read; a failure on its events is a refusal. TraceEvent is 4.0.2. |
| 4.3 | Live session | L | Allocations streamed beside the Live timeline from a trace read while it grows (ADR-037), and snapshots faster than a full dump. |
| 4.4 | Allocated between two snapshots | M | In Compare, from the trace recorded between them (from 2.5; ADR-028). |
| 4.5 | Peer review | M | A review of the phase so far, like 3.5's; its fixes plan the next sprints. |
| 4.6 | Right answers | L | Peer review 4.5 (the split agreed by the owner on 2026-11-25): H2, H3, H4, H9, H10, H11, H16, H19, M6 to M9, Compare's retained columns when only one side's GC handles were checked, and a cached load whose handles could not be checked tried again when its cause is gone. Also the trace of a run that had no bytes after 60 s in CI (3 times since 2026-11-20, `A_run_records_an_allocation_trace_for_its_whole_length`, which says its state since 4.5): a test or a start that hangs (near H9); and the bookmarks status of a damaged file not shown within 10 s once on Linux CI (`A_damaged_bookmarks_file_is_said_when_the_snapshot_opens`). |
| 4.7 | Runtimes, Linux and large heaps | L | Peer review 4.5: H1, H5 to H8, H12, M11. |
| 4.8 | Ready to release | L | Peer review 4.5: H14, H15, H17, M1 to M5, M13, M14; then a trial by people outside, then the first full release. |
| 4.9 | Workspaces | L | Peer review 4.5: H13 and M15, what analysis tabs need. |
| 4.10 | Analysis tabs | M to L | Several analyses of a snapshot side by side, each with its own path (from 2.7; peer review P21 and the Live page split). |
| 4.11 | Sunburst | S to M | (Peer review 4.5 M16) A sunburst view beside the icicle chart for dominators and call trees. |
| 4.12 | WPF leak detectors (optional) | M | (Peer review 4.5 M12) With a WPF fixture app (from 2.6): binding to a non-INotifyPropertyChanged source, collection bindings, dependency property descriptors, x:Name. Dropped if time runs short. |

---

## Sprint 4.6: Right answers

**Goal:** every answer HeapScope shows is about the snapshot it names, says when it did not check, and nothing hangs or crashes on the
way there: the High and Moderate findings of [peer review 4.5](reviews/peer-review-4.5.md) planned here, with what its fixes left.

**Tasks**
1. [x] H4: a grid whose subject changes (another snapshot, another pair, a snapshot closed) shows nothing of the old one: rows, totals,
   heap bar and notes cleared first, also when the new query fails; Trend falls back to its default sort for a column gone; Compare and
   Trend show the busy bar. Tests: a query of B that fails shows nothing of A, in Types, Compare and Trend; Trend after a close.
2. [x] H9: stopping an EventPipe session waits 5 s at most, then lets it go, in one helper for the trace recorder, the forced
   collection and the counters; closing the app waits a bounded time for its host, logs what it waited for and always shuts down.
   Tests: the helper with a stop that never answers; a recording and a forced collection of a suspended process end.
3. [x] H10: the file and process work at a job's start runs on the thread pool, never on the caller's (UI) thread: the trusted DACs an open reads, the session a recording takes, starting a worker (all jobs on the pool broke the synchronous start the views rely on). Test: work that
   records its thread is never run on the caller's.
4. [x] H11: a run's allocation trace checks the free space of its own folder's disk (DumpCapturer's probe) on every system, goes on
   after a failed check, and stops recording with a message after several in a row. Tests: the probe on a folder, failures counted.
5. [ ] H2: a detector that could not read objects says so: the result carries its counts (scanned, unread), the front ends show "not
   checked" for what was unread, and a detector that could read none of what it needs is a refusal. Tests per detector with fields
   missing; the CLI's and the app's text.
6. [ ] H3: the static storage bucket keeps its "static storage" detail always; statics of closed generic types and boxed struct statics
   are named where ClrMD can read them; the growing static collections detector notes storage slots no static field names. Tests on a
   fixture with `Cache<T>` statics and a struct static.
7. [ ] H16: the CLI has a boundary around its start: a mistake in `appsettings.json` is said with the file, the line and how to write a
   backslash in JSON, and logged; unhandled exceptions are logged. INSTALL gets a Windows example. Tests: a damaged and a mistyped file.
8. [ ] H19: a failed nightly fuzz run opens an issue, or comments on the open one, naming its seed. Test: the workflow step, by a dry run.
9. [ ] M6: one private JSON file helper (a lock, shared reads with retries, a flushed temp file, a replace with retries, unreadable is not
   empty) used by lesson progress, bookmarks and settings. Tests: a held file loses no progress; two writers lose no update.
10. [ ] M7: Inspections, Retainers and an object's steps that failed can be tried again (the latch is cleared, a Try again in the status
    note); Compare and Trend query only while shown and catch up when shown. Tests per view.
11. [ ] M8: a worker crash racing a restart is still a crash (WorkerCrashedException); SampleService raises its events safely and its
    process kill tolerates an exited process; a recording from the Processes page marks its session in use, and an open trace keeps its
    session. Tests: two callers of a crash; a throwing listener; deleting a session that records.
12. [ ] M9: "no root found" in place of "unreachable" stated as fact where stack roots of a running process can be incomplete, and the
    unreachable set's generation split. Wording drafts for the owner. Tests: the texts, the split.
13. [ ] What 4.5's fixes left: Compare's retained columns say when only one side's GC handles were checked; a cached load whose handles
    could not be checked is loaded again when its cause is gone (the second walk turned on again, the DAC found). Tests: both.
14. [ ] The tests that failed in CI now and then: the run's trace that had no bytes for 60 s (a start that hangs, near task 2), the
    bookmarks status of a damaged file on Linux, the live allocations follower (fixed 2026-11-25, watched). Each fixed or explained.

**Decisions**
- All of the findings the owner put in 4.6 (2026-11-25), in this order: what shows wrong answers first, then what hangs, then the rest.
  Decided autonomously.
- New user-facing texts are drafts in `docs/design/info-text.md` ("Sprint 4.6 additions") until the owner approves them; the sprint is built
  with them. Decided autonomously.
- A protocol change (task 5's counts, task 13's note) bumps the worker protocol once for the sprint. Decided autonomously.
- Settings for the DAC on the Settings page (H15) stay in 4.8 with the rest of "Ready to release"; task 7 only says the file and line.
  Decided autonomously.

---

## Sprint 4.5: Peer review

Closed 2026-11-25; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/peer-review-4.5.md](reviews/peer-review-4.5.md). ADR-010, ADR-034 and ADR-041 are amended for it.

---

## Sprint 4.4: Allocated between two snapshots

Closed 2026-11-25; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.4.md](reviews/sprint-4.4.md). ADR-028 is amended for it.

---

## Sprint 4.3: Live session

Closed 2026-11-25; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.3.md](reviews/sprint-4.3.md). ADR-037 is amended for it.

---

## Sprint 4.2.1: Damaged gcdumps and traces

Closed 2026-11-24; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.2.1.md](reviews/sprint-4.2.1.md). ADR-028 and ADR-037 are amended for it.

---

## Sprint 4.2: Capture on macOS

Closed 2026-11-23; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.2.md](reviews/sprint-4.2.md). ADR-025 and ADR-014 are amended for it.

---

## Sprint 4.1.1: Hide processes HeapScope cannot capture

Closed 2026-11-21; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.1.1.md](reviews/sprint-4.1.1.md). ADR-014 is amended for it.

---

## Sprint 4.1: Capture on Linux

Closed 2026-11-20; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.1.md](reviews/sprint-4.1.md). ADR-025 is amended for capture on Linux.
