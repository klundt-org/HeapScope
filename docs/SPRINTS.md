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
| 4.7 | Runtimes, Linux and large heaps | Closed | 2026-11-26 | sprint-4.7 |
| 4.8 | Ready to release | Closed | 2026-11-27 | sprint-4.8 |
| 4.9 | Workspaces | In Progress | | |
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

## Sprint 4.9: Workspaces

**Goal:** each open snapshot gets a workspace of its own (its types grid, analysis path, inspections and retainers), so switching
snapshots keeps every view as it was and links land in their snapshot's workspace, with the worker's result sets kept for the
workspace shown; and the code is ready for analysis tabs (4.10): classes kept small, comments that explain without sprint ids, and
view model tests on a real UI thread. Peer review 4.5: H13 (analysis tabs do not fit the page model: one set of views re-pointed,
paths per snapshot, links without a workspace, 32 result sets) and M15 (classes grow back, sprint and review ids in about 1,600
comments, tests without a UI thread).

**Tasks**
1. [x] A workspace per open snapshot: `SnapshotWorkspaceViewModel` owns its types grid, analysis path (navigator), inspections and
   retainers; the Snapshots page keeps one per open snapshot and shows the current one; closing a snapshot disposes its workspace.
   Views bind to the current workspace. Compare stays on the page. Tests: switching between two snapshots keeps each one's filter, sort,
   path, inspection results and retainers as they were, without a new query; closing one drops its workspace.
2. [x] Links land in their workspace: a `NavigationTarget` names its snapshot, and following one shows that snapshot's workspace
   first (Compare, bookmarks, Retainers links, the Live page's files). Tests: a link into a snapshot not shown shows its workspace and
   step; a link of a closed snapshot is refused with why.
3. [x] The worker's result sets follow the workspace shown: the client tells the worker which snapshot is shown, and the result set
   store evicts the sets of snapshots not shown first; a workspace shown again re-runs only what was evicted. Protocol version bump.
   Tests: three snapshots open, the shown one's sets survive the others' queries; the eviction order; the re-run.
4. [x] M15, classes: the view models over 600 lines are split along their parts (CompareViewModel, LiveAllocationsViewModel,
   InspectionsViewModel, SnapshotsViewModel once the workspace is out), and an architecture test caps a view model file at 600 lines,
   with a named, shrinking list of exceptions. Tests: the cap; the split classes' existing tests unchanged.
5. [x] M15, comments: source comments say why without sprint or review ids (a decision keeps its ADR number); an architecture test
   refuses new ones in src. Tests: the rule, with its patterns.
6. [x] M15, the UI thread: a test dispatcher that runs on a thread of its own, and checks that a view model's state changes there;
   the workspace and page tests use it. Tests: a property set off that thread fails the test; the workspace tests pass on it.
7. [ ] Docs: an ADR for workspaces (amending ADR-029's shared types grid), ARCHITECTURE.md's page model, process-model.md's result set
   store, code-map.md, DEVELOPER.md's view model and comment conventions.

**Decisions**
- Decided autonomously: a workspace per open snapshot now; several workspaces of one snapshot (analysis tabs) are 4.10's, on this model.
- Decided autonomously: Compare stays on the Snapshots page, across workspaces; it takes its two snapshots as before.
- Decided autonomously: the worker is told which snapshot is shown (a protocol call) and evicts other snapshots' result sets first,
  rather than the client releasing them eagerly: a workspace shown again keeps what still fits.
- Decided autonomously: comment ids go from src only; tests and docs keep theirs (their records); an ADR number stays in a comment.
- Decided autonomously: 600 lines per view model file, with a list of named exceptions that only shrinks.
- Decided autonomously: page and workspace tests move to the single-thread dispatcher; other view model tests keep the immediate one.

---

## Sprint 4.8: Ready to release

Closed 2026-11-27; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.8.md](reviews/sprint-4.8.md). ADR-008, ADR-012, ADR-014, ADR-015, ADR-017 and ADR-039 are amended for it.

---

## Sprint 4.7: Runtimes, Linux and large heaps

Closed 2026-11-26; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.7.md](reviews/sprint-4.7.md). ADR-002, ADR-015, ADR-020, ADR-025, ADR-033, ADR-034, ADR-037 and ADR-041 are amended for it.
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
