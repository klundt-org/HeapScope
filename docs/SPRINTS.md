# HeapScope Sprints

Phases: **0.x** architecture, framework and skeleton; **1.x** quick wins; **2.x** heavy hitters; **3.x** depth to
match dotMemory (planned 2026-10-28); **Phase 4 candidates** what Phase 3 leaves out; the Backlog ([BACKLOG.md](BACKLOG.md)) everything else. Closed sprints are
in [sprints/](sprints/): [Phase 0](sprints/phase-0.md), [Phase 1](sprints/phase-1.md),
[Phase 2](sprints/phase-2.md), [Phase 3, the peer review sprints, 3.9 and 3.10](sprints/phase-3.md).

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
| 3.12 | First-show performance and refactor analysis | In Progress | | |
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

## Sprint 3.12: First-show performance and refactor analysis

**Goal**: know why pages hitch the first time they open, and what in the code is worth refactoring, before Phase 4 adds to both.

**Background**: the product owner asked for this on 2026-11-05, from the 3.10 review. M16: cold first shows over 100 ms went from 3 to 7 per
run to 5 to 13, exempt from the gate, with the worst dispatcher lag 184 of 200 ms and the cause only inferred.

**Scope, to settle at /start-sprint**:
- Trace a cold first show (`--smoke-trace`, with JIT events), attribute the blips, and fix what the trace supports. Consider a ceiling
  on logged first shows, and a run on a Medium dump, not only the demo dumps.
- A refactor analysis of the codebase: what to change, why and at what cost, as a ranked list the owner decides on. It proposes; any
  refactor it leads to is the owner's call. It includes `PlatformHints` (3.10 review M36).
- To consider pulling in: the Backlog's slower first load (the object pass), and the tests that fail only under full load.
- The older cache fallback, moved from 3.10 by the product owner (2026-11-05; 3.10 review M14, M29 to M31, M37). After a loader update,
  a dump whose DAC is gone or not trusted cannot rebuild its cache, so it does not open. Open the older cache instead, with a note. It must
  cover every way a rebuild can lack the DAC (a symbol server failure, an untrusted DAC after a "no"), set the oldest loader version it accepts,
  free the first read's memory before the second, and say which views are refused without a DAC. Its first version caused most of the
  findings on 3.10's Moderate fixes, so review it as a feature.

**Tasks**:

1. [x] A Medium dump made fresh from DenseHeap with the installed runtime, by a script in `fixtures/scripts` (git-ignored
   output). Proof: the script runs twice to the same object count; the dump opens.
   Done: `New-SampleDump.ps1 -Tier Medium` and `New-DenseDump.ps1` already did this; the 2026-11-05 dumps were taken on .NET
   10.0.14, whose DAC is gone since the 10.0.15 update, so they are now the fallback's case (task 9). Fresh Medium (4.5 GB) and
   Small dumps on 10.0.15, 2026-11-11. Under Windows PowerShell 5.1 the scripts run with `powershell -File`.
2. [x] Test machine independence: every test that opens a dump gets a fixed `PhysicalMemory` from one shared helper, so the
   machine's free memory cannot change an answer. Proof: a mutation that reads the real memory fails a test; a search finds
   no test opening a dump without the helper.
   Done: `tests/SharedSession/TestSessions.cs` (32 GB, 16 GB free) in the integration, view model and UI tests; 33 sessions moved
   to it; `TextRulesTests.Tests_make_snapshot_sessions_only_through_TestSessions`. Mutation: with 1 byte free, the three
   `ProcessesViewModelTests` that failed in 3.11 fail again. The worker's own budget still reads the machine (mapped or in memory,
   refusals); that is the product's behavior and is left as is.
3. [ ] A WPR recording of the smoke run (CPU sampling, context switches, ready thread; Windows' own `wpr.exe`, an admin prompt
   the owner approves) beside the `--smoke-trace` EventPipe trace with JIT events, read by a small TraceEvent tool in
   `build/` that attributes each first show over 100 ms and each dispatcher lag over 100 ms to JIT, styles and templates,
   layout, GC, or the UI thread waiting for the CPU. Proof: the tool's unit tests on a recorded trace's numbers; its report
   in `budgets.md`.
4. [ ] Fix what the trace supports for cold first shows (for example preloading templates or JIT warm up off the UI thread),
   each fix measured against the build before it. Proof: smoke runs on the demo dumps and on the Medium dump, before and after.
5. [ ] A ceiling on slow first shows: the smoke run fails above the measured count of first shows over 100 ms. Proof: a test
   of the rule; a mutation that lowers the ceiling fails the run.
6. [ ] The dispatcher lag under load (Backlog High, from 0.3) and the worker's Server GC threads: settled from task 3's
   trace, with a fix if it shows one (job object CPU rate, EcoQoS or GC heap affinity). Proof: the trace; smoke runs under load.
7. [ ] UI cold start measured again on Avalonia: the first window and the first Compare tab, published ReadyToRun. Proof:
   numbers in `budgets.md`.
8. [ ] The slower first load traced on the Medium dump on a quiet machine (the object pass), with the cause recorded and a
   fix proposed with its cost; built only with the owner's OK. Proof: the trace and the numbers in `budgets.md`.
9. [ ] The older cache fallback (3.10 M14, M29 to M31, M37), built and reviewed as a feature: a rebuild that cannot reach a
   trusted DAC (gone, symbol server failure, untrusted after a "no") opens the older cache with a note; caches older than the
   v3.9.0 loader are refused; the first read's memory freed before the second; graph views work and value reads are refused
   with the reason. Proof: worker tests with a stand-in loader for each failure; an integration test in both memory modes;
   a test per refused view; the log line naming the fallback and why.
10. [x] A negative `gc-heap-size` sample dropped and logged. Proof: a counter test with a negative sample.
    Done: `CounterSampleBuilder` drops it with a warning naming the value, the time and gen 0's size;
    `CounterSampleBuilderTests.A_sample_with_a_negative_heap_is_dropped_and_logged`, proved by mutation.
11. [x] A seam in front of `DumpCapturer`, so the capture gate and a cancel while writing are tested deterministically (one
    pid refused, two pids together, released on failure and cancel, a cancel while the dump is written). Proof: those tests;
    `CaptureTests`' cancel test no longer depends on timing.
    Done: the seam existed (`IDumpWriter`); `tests/SharedCapture/HeldDumpWriter.cs` holds a write until the test releases it.
    `CaptureGateTests` (four tests, every wait bounded) and the real cancel test hold the runtime's own writer; mutations of
    the gate and of the delete on cancel each fail a test.
12. [ ] M6 from the 3.11 review: a Type or Namespace group row's retained bytes marked as a lower bound in the Retainers tree
    and its CSV export. Wording approved by the owner in `docs/design/info-text.md` first. Proof: view model and export tests.
13. [ ] The refactor analysis: all four reviewers report candidates; `docs/design/refactor-analysis.md` ranks them by value and
    cost, with `PlatformHints` (3.10 M36). The owner decides; any refactor is built only on the owner's pick. Proof: the document.
14. [ ] Docs: `budgets.md`, ADR-006 and ADR-036 amended for the fallback, `DEVELOPER.md` for the new tools, the Backlog.

**Decisions** (product owner, 2026-11-11):

1. Tracing uses `--smoke-trace` and Windows' own WPR, read with TraceEvent; nothing downloaded.
2. A ceiling on slow first shows, set from the measured count after the fixes.
3. A fresh Medium dump from DenseHeap, made locally and kept out of git; the old 20261022 and 20261023 dumps may go.
4. The refactor analysis is a ranked list from all four reviewers; no refactor without the owner's pick.
5. The fallback accepts caches from the v3.9.0 loader or later.
6. From an older cache without a DAC, everything the graph holds works; reads of values from the dump are refused with the reason.
7. Pulled in: the slower first load (traced; a fix only with the owner's OK), the dispatcher lag under load, the UI cold
   start, test machine independence, the capturer seam, and M6.
8. A negative `gc-heap-size` sample is dropped and logged, not clamped.
9. M7 from the 3.11 review stays in the Backlog.

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

**Tasks**: to be filled by /start-sprint.

## Peer review sprints

3.6, 3.7 and 3.8 worked through `docs/reviews/peer-review-3.5.md`; all closed ([sprints/phase-3.md](sprints/phase-3.md)).

---

## Phase 4 candidates

Not planned; picked from when Phase 3 closes (product owner, 2026-10-28: what Phase 3 does not address).
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
