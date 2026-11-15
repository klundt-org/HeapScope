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
| 3.13 | Tutorial | In Progress | | |

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

**Tasks**:

1. [x] ADR-042, the tutorial: a shipped sample process whose scenarios and the app's lessons are catalogs, the extension contract
   for later features, the packaging, and what the sample may and may not do (never elevated, stops with HeapScope, at most
   three). DEVELOPER.md's Definition of Done gains the item "a feature a user would learn adds its scenario and lesson, or its
   sprint says why not". Proof: the ADR and its index line; the Markdown sweep.
   Done 2026-11-15: ADR-042 and its index line; DEVELOPER.md section 9's new item.
2. [x] The sample, `src/HeapScope.Sample` (executable `heapscope-sample`): the fixtures' leak library (`fixtures/Leaks`, shared
   so the known answers hold for both) behind a scenario catalog (`ScenarioCatalog`: name, title, what it plants, what it
   cannot run with, its known answer's name), `--scenarios a,b`, `--list` (the catalog as JSON, which the app reads, so the app
   never names a scenario its sample lacks), `--parent-pid`, and refusals with the reason for scenarios that cannot run
   together. Proof: unit tests of the catalog (names unique, conflicts both ways, every scenario's known answer exists) and of
   the argument rules; an architecture rule that the sample references only the leak library.
   Done 2026-11-15: eleven scenarios (the five classic leaks, the four detector leaks, Waste, Inspection), `--list` as JSON,
   the finalizer-blocking scenarios started after the last full collection as the fixtures do. `ScenarioCatalogTests`
   (unique names, known answers, conflicts both ways, the argument rules); `LayerRules` holds the sample to the leak library.
3. [x] Keeps growing: `--grow-every <ms>` adds a share of each chosen leak at an interval, and the steady allocation scenario
   allocates from known call sites, so Live, an allocation trace, Compare and Trend all show something. Proof: tests that the
   item counts grow by the step; an integration test that two dumps of a growing sample differ by the known step and a trace
   names the known call site.
   Done 2026-11-15: every classic and detector leak grows by a tenth of its count per tick; `SteadyAllocation` prices orders
   without end from `SteadyAllocation.PriceOrder`, keeping one in a hundred in a bounded window. Each scenario names the type a
   lesson's answer is (`AnswerType`, held to its known answer). `ScenarioCatalogTests`; `SampleTests` (two dumps a growing
   sample apart differ by whole steps; a CLI allocation trace of the steady scenario names `PriceOrder` and the sample's types).
4. [x] Packaging: the sample published into `sample/` beside `worker/` in every package (self-contained per system like the
   worker; framework-dependent in the CLI tool), signed and licensed like the rest, its size measured. Proof: `check-package.sh`
   finds it, its license entries and its signature; the size in budgets.md.
   Done 2026-11-15: `build/Sample.Deploy.targets` (as the worker's), imported by the app and the CLI; `check-package.sh`
   requires `sample/`, `pack-tool.sh` the tool's sample assembly and no sample executable; the release's SBOMs include the
   sample. win-x64: the sample 77.0 MB, the rest 356.5 MB (budgets.md). The release attests whole archives, the sample with
   them; no package of its own, so no license file of its own beyond the runtime's, already collected.
5. [ ] Starting and stopping (Application, `SampleService`): starts the sample as the user with the chosen scenarios, reads
   its catalog, marks its pid as a sample, refuses while HeapScope runs elevated (the sample would be elevated too) and above
   three running, stops it on request, and it exits with HeapScope (parent watch). Proof: tests with a fake process starter
   for each rule; an integration test that starts, lists and stops the real sample.
6. [ ] The Processes page starts and stops a sample (scenarios picked from its catalog) and marks its rows "Sample". Proof: view
   model tests; the view test binds cleanly; the screenshot run shows it.
7. [ ] Lessons (`LessonCatalog`, UI.ViewModels): one entry per lesson: its title, the scenarios it starts, the page it starts
   on, its steps, its question and the answer checked against the scenario's known answer (the planted type among decoys).
   Every scenario has a lesson or a reason, every lesson's scenarios exist in the sample's catalog. Proof: catalog tests both
   ways; a test per lesson that its answer is the known answer's type.
8. [ ] The Learn page, low on the left navigation above Settings: the lessons with their progress, a lesson started (its sample
   running), its steps beside the page it opens, the answer checked, progress kept per user in the data folder. Proof: view
   model tests (start, steps, a right and a wrong answer, progress saved and read back); the view test; the screenshot run.
9. [ ] `heapscope-cli sample list|start|stop` for a demo without the app. Proof: CLI tests; an integration test that starts the
   sample and captures it with the CLI.
10. [ ] A "Try it" walkthrough in the User Guide, scenario by scenario, and the README's demo; every text of the Learn page and the
   lessons as drafts in info-text.md for the owner. Proof: the doc link test; InfoIconTests for the page's icon.
11. [ ] Docs: ARCHITECTURE.md and its pages (code map, process model), DEVELOPER.md (building and testing the sample, adding a
   scenario and a lesson), THIRD-PARTY-NOTICES if anything is added. Proof: the Markdown sweep.

**Decisions** (2026-11-15; the owner: "just keep going, no reason to wait on me", so each was decided autonomously with its
recommended default):

1. One sample, `heapscope-sample`, built on the fixtures' leak library (shared code, so one known answer serves the fixture
   tests and the lessons), not a copy. Decided autonomously.
2. Self-contained in each per-system package, as the worker is, so it runs without an installed .NET; framework-dependent in
   the CLI tool, which needs .NET anyway. The measured size is recorded; if it adds more than half again to a package, the
   owner is asked. Decided autonomously.
3. The scenario catalog lives in the sample and the app reads it (`--list`), so the app never offers a scenario its sample
   lacks; lessons name scenarios, and a test checks they exist. Decided autonomously.
4. A lesson's answer is checked by a question the user answers after finding the problem with the app's pages (the planted
   type among decoys from the same dump), not by watching what the user clicks: robust to how the user gets there, and each
   new feature's lesson is one catalog entry. Decided autonomously.
5. Generated by default (the sample runs live); no pre-built files in this sprint (Backlog). Decided autonomously.
6. While HeapScope runs elevated the sample is refused with the reason (starting a non-elevated child from an elevated one is
   not portable); at most three samples at once. Decided autonomously.
7. The Learn page sits low on the left navigation, above Settings; starting and stopping is on the Processes page too.
   Decided autonomously.
8. `heapscope-cli sample` is in, for the README's demo. Decided autonomously.
9. Every user-facing text is drafted in info-text.md and built; the owner approves before the sprint closes. Decided
   autonomously.

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
