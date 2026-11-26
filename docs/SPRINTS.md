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
| 4.6 | Right answers | Closed | 2026-11-25 | sprint-4.6 |
| 4.7 | Runtimes, Linux and large heaps | In Progress | | |
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

## Sprint 4.7: Runtimes, Linux and large heaps

**Goal:** what README promises is what was tested: dumps of .NET 8, 9 and 10 give the known answers, the integration tier runs on
Linux and the app's window is shown there, and a large heap opens within the memory HeapScope says it has, on every system: the
findings H1, H5 to H8, H12 and M11 of [peer review 4.5](reviews/peer-review-4.5.md), with what 4.6 left for them.

**Tasks**
1. [x] Tests leave nothing in the temp folder: one helper for a test's folder (`TestFolders`: deleted with retries, one that stays is
   said on the error stream, never swallowed), used by the integration tier's dump folders; every test assembly's start sweeps the dump
   folders and the tests' `heapscope-*` folders a run left more than a day ago (the small shared data and sessions folders stay).
   Tests: `TestFoldersTests` (a held file said, a stale folder swept, a fresh one or one named by hand kept).
2. [x] H1: the fixtures (Leaks, LeakyConsole, LeakyApi) build for net8.0, net9.0 and net10.0; the tests pick a fixture's runtime by
   `HEAPSCOPE_FIXTURE_RUNTIME` (net10.0 when unset); the known answers (`KnownAnswerTests`, `LoaderKnownAnswerTests`,
   `DetectorTests`, `check-known-answers.sh`) pass on each, with any answer that differs by runtime stated in its file. CI runs them
   for .NET 8 and 9 on Windows and Linux. Tests: the known answer classes per runtime; a runtime's own answer where it differs.
   Found: .NET 8 reports its static storage only as strong handles, so a static ClrMD cannot name (a generic type's) blamed a
   handle; the loader now names a handle's array that holds a static's slot as the storage (loader version 7). And a heap dump of
   .NET 8 leaves out the memory its statics are in: the loader says so (`StaticsUnread`, when none of CoreLib's statics read),
   with a note, the unreachable set refused and the detectors noted as for unchecked handles. Tests: `HeapDumpStaticsTests`, the
   statics case in `UncheckedHandlesTests`; the tests take full dumps of .NET 8. No answer differs by runtime.
3. [x] H1: opening a dump of a runtime HeapScope was not tested on (before .NET 8) says so beside the result, in the app and the CLI, and
   the log names it; README, ADR-002 and INSTALL say which runtimes are tested (8, 9 and 10) and which open untested (5 to 7). Tests: the
   note for a .NET 6 version and none for 8 to 10; the CLI's line.
4. [ ] M11: thread statics have a fixture (`[ThreadStatic]` and `ThreadLocal<T>` holding leak objects) and a known answer; the loader
   names a thread static by its field where ClrMD lists it (loader version 7), else keeps "thread static". Tests: the known answer on
   each runtime.
5. [ ] H12: the integration tier runs on Linux: the project builds for net10.0, the paths of the fixtures, the CLI and the worker are
   the system's own, and the Windows-only tests (minidumps, elevation, `cmd.exe`) skip elsewhere and say why; CI runs the light
   integration tier on Linux. Tests: the tier itself, green in CI on Linux.
6. [ ] H12: the app's window is shown on Linux in CI under a virtual display (`xvfb-run`): it opens the fixture dump and drills down as the
   headless smoke run does. A real desktop (claude-dev-3) stays in the Backlog. Tests: the CI step.
7. [ ] H7: the memory budget reads the system's own numbers off Windows: `/proc/meminfo` (MemAvailable) and a cgroup's limit and use on
   Linux, `sysctl` and the VM statistics on macOS (untested); the worker's own memory on macOS is its physical footprint. One reader for the
   worker and the front ends. Tests: the parsers on sample texts; a cgroup limit below the machine's; on Linux CI, nonzero numbers before
   any collection.
8. [ ] H6: an inspection runs inside a memory reservation sized by its own estimate (objects scanned), refused with a next step when it
   cannot fit; Duplicate strings keeps the values that waste the most (a bounded set) and says how many more there were. Tests: a
   reservation refused; the bound and its note.
9. [ ] H8: a recording's default limit is one whose read fits beside two snapshots on the 16 GB baseline (512 MB), and a recording's
   limit is checked against the budget at its start, not only at its read. The one streaming reader stays in the Backlog. Tests: the
   default; a limit over the budget lowered with a note.
10. [ ] H5: the worker checks a first load: it reserves the load's estimate while it loads, refuses with a next step when the open was
    not allowed to overcommit, and logs the estimate against what was used. An 8 GB DenseHeap dump is measured (first load, peak, reopen
    in both modes, the indices; and with the working set capped); if the peak is over budget, the edges go into the cache as they are
    read. Tests: a load over budget refused; the measurement in budgets.md.
11. [ ] M11: the reverse index built without a cache takes a reservation; a capture's disk check counts the index caches the dump will
    need (by the graph's bytes per object); counters read the interval the target reports and log one that differs from the one asked.
    Tests: each.
12. [ ] The trace start that takes over 60 s under load (CI since 2026-11-20, the 4.6 gate): test the theory that the names session's
    rundown on stop holds the target's diagnostics server; fix it if so, else record what was measured. Tests: a start after a names
    session, timed.

**Decisions**
- Owner, 2026-11-25: .NET 8 and 9 runtimes may be downloaded (this machine has them already; CI gets them with setup-dotnet).
- Owner, 2026-11-25: about 20 GB in `artifacts/` for the 8 GB dump is fine, and the old fixture dumps there may be deleted (D: has
  1 TB free, so they are kept unless the space is needed).
- Owner, 2026-11-25: Linux runs the integration tier headless in CI now, and the app's window under a virtual display; a real desktop
  later (claude-dev-3).
- Decided autonomously: .NET 5 to 7 are opened, marked untested, not refused; ADR-002's decision stands, amended with what is tested.
- Decided autonomously: the worker protocol and the loader version are bumped only if a task needs it (task 4 does: loader 7).
- Decided autonomously: the 1 GB trace limit becomes 512 MB; the streaming reader for a finished trace is Backlog (L).
- Decided autonomously: the test temp cleanup (task 1) joins the sprint: a killed run left 17 GB in the temp folder (owner, 2026-11-25).
- Decided autonomously: the edges written straight into the cache only if the 8 GB measurement misses the 4 GB first load budget.

---

## Sprint 4.6: Right answers

Closed 2026-11-25; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.6.md](reviews/sprint-4.6.md). ADR-020 is amended for it.

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
