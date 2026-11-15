# HeapScope Sprints

Phases: **0.x** architecture, framework and skeleton; **1.x** quick wins; **2.x** heavy hitters; **3.x** depth to
match dotMemory (planned 2026-10-28); **Phase 4 candidates** what Phase 3 leaves out; the Backlog ([BACKLOG.md](BACKLOG.md)) everything else. Closed sprints are
in [sprints/](sprints/): [Phase 0](sprints/phase-0.md), [Phase 1](sprints/phase-1.md),
[Phase 2](sprints/phase-2.md), [Phase 3, the peer review sprints, 3.9 to 3.12](sprints/phase-3.md).

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
| 3.13 | Tutorial | Planned | | |

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

## Sprint 3.13: Tutorial

**Goal**: a user can start a sample process of HeapScope's own from the app, choose what it does, look at it with every view
(capture, Live, allocations, Compare, Trend, inspections) and stop it, without a process of their own or elevation.

**Background**: the product owner asked for this on 2026-11-10, after trouble finding a process whose allocations could be
recorded. The test fixtures already do most of it (LeakyConsole's leaks, detector and waste patterns, its parent watch;
LeakyApi and LoadGen's steady load), but none ships, and LeakyConsole leaks once and then idles, so a trace of it is empty.

**Scope, to settle at /start-sprint**:
- Ship one sample app in every package, started as the user, never elevated, signed and licensed like the rest. Framework
  dependent (small, needs the .NET runtime) or self-contained (larger packages): decide with a measured size.
- The user picks one or more scenarios: the five classic leaks (static cache, event handler, closure, undisposed list,
  unbounded dictionary), the detector leaks (timer, disposed but referenced, pending async, finalizer backlog), waste and
  inspection patterns, and steady allocation from known call sites. Scenarios that cannot run together (finalizer backlog
  and waste) are refused with the reason.
- A mode that keeps allocating and keeps growing, so Live, an allocation trace, Compare and Trend all show something.
- Start and stop from the UI (likely the Processes page), the sample's row marked as such; it stops when HeapScope exits or
  crashes (the parent watch), and never more than a few run at once.
- Perhaps `heapscope-cli sample start|stop`, so the README can walk through a demo.
- A "try it" walkthrough in the User Guide, scenario by scenario, saying what each view should show; the known answers of
  each scenario hold the walkthrough to the truth.
- How the sample is built and tested apart from the fixtures (shared code, or the fixtures built from it), and an ADR.
- The hands-on tutorial (product owner, 2026-11-14; the Backlog's "phase 3.5 candidate", the same feature from the other side):
  a Learn item low on the left navigation whose lessons walk the user through finding and diagnosing each planted problem with
  the app's own pages, generated by the sample by default; each lesson checks the answer against what the sample planted, as
  the fixtures' known answers do. Its texts go through info-text.md.
- Built to grow (product owner, 2026-11-15): Phase 4 adds features, and each must be able to add its scenario and its lesson
  as it is built, without reworking the tutorial. So scenarios and lessons are catalogs, as inspections are
  (`InspectionCatalog`): a scenario is one entry in the sample (what it plants, its known answer, what it cannot run with), a
  lesson one entry in the app (the page it starts on, its steps, the check of the answer), each with a test that the catalog
  covers it, and a version so a session can say which ones it ran. The sample and the lessons ship together, so a lesson never
  points at a scenario its build lacks. The ADR records the extension contract; DEVELOPER.md's Definition of Done gains an
  item from this sprint on: a feature a user would learn adds its scenario and lesson, or says in its sprint why not.

**Tasks**: to be filled by /start-sprint.

## Peer review sprints

3.6, 3.7 and 3.8 worked through `docs/reviews/peer-review-3.5.md`; all closed ([sprints/phase-3.md](sprints/phase-3.md)).

---

## Phase 4 candidates

Not planned; picked from when Phase 3 closes (product owner, 2026-10-28: what Phase 3 does not address). Each one, when
built, adds its scenario to the sample and its lesson to the tutorial (sprint 3.13's extension contract; product owner,
2026-11-15), for example a WPF leak scenario with the WPF detectors, a sunburst lesson, or a live session lesson.
- A profiler-style live session: allocation data streamed beside the timeline, and snapshots faster than a full dump.
- What was allocated between two snapshots in Compare, from a trace recorded between them (from 2.5; needs traces in
  `session.json` on the snapshots' clock, and each tick's type id and module to join a diff's rows, ADR-028).
- A sunburst view beside the icicle chart for dominators and call trees.
- Analysis tabs: several analyses of a snapshot side by side, each with its own path (from 2.7; the branching analysis
  path covers coming back to an earlier analysis for now).
- Capture on Linux and macOS (from 2.3.5, ADR-025): process discovery through `/proc` and the diagnostics client, full
  dumps through the runtime's createdump (the diagnostics client's `WriteDump`), gcdumps through the vendored code;
  elevation and ptrace rules per system. Until then capture refuses there and points to `dotnet-dump`.
- WPF leak detectors with a WPF fixture app (from 2.6): binding to a non-INotifyPropertyChanged source, collection
  bindings, dependency property descriptors, x:Name.
