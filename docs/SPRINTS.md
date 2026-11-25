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
| 4.4 | Allocated between two snapshots | In Progress | | |
| 4.5 | Peer review | Planned | | |
| 4.6 | Analysis tabs | Planned | | |
| 4.7 | Sunburst | Planned | | |
| 4.8 | WPF leak detectors (optional) | Planned | | |

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
- Releases after 4.2 (capture everywhere), after 4.4 (the live session and Compare) and at the end of the phase.
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
| 4.6 | Analysis tabs | M to L | Several analyses of a snapshot side by side, each with its own path (from 2.7; peer review P21 and the Live page split). |
| 4.7 | Sunburst | S to M | A sunburst view beside the icicle chart for dominators and call trees. |
| 4.8 | WPF leak detectors (optional) | M | With a WPF fixture app (from 2.6): binding to a non-INotifyPropertyChanged source, collection bindings, dependency property descriptors, x:Name. Dropped if time runs short. |

---

## Sprint 4.4: Allocated between two snapshots

**Goal:** comparing two snapshots of one monitoring run that recorded its allocations, Compare says what was allocated between them, by
type, and one step leads to where.

**Starting point.** A comparison says what is there now and was not (New, matched by address, so a moved object counts too); it says
nothing of what was allocated and is gone again, or where. A run that records allocations has both snapshots and the trace in its
session (`SessionRun`: `SnapshotIds`, `Trace`), each with a time; ADR-028 left out joining them, since it needs one clock and one
name for a type.

**Tasks**
1. [x] Measured before building: how a capture's recorded time lies to the trace's (the target is paused while a dump is written), and
   how much of the sampled bytes join a diff row by name, for full dumps and gcdumps (generics, arrays, nested types, names in two
   modules). If names do not join for most of the bytes, stop and report. Proof: the numbers in Findings and ADR-028's amendment.
2. [x] Which trace lies between two snapshots: the run of one session that holds both and a trace, the interval, and a reason when
   there is none (another process, another run, no trace, a trace that ended before). Tests: each reason; a received session's
   paths.
3. [x] One name for a type: the trace's names brought to the snapshots' form in one place, tested against real dumps. Tests: table and
   integration.
4. [x] The worker: a diff with a trace and an interval gets Allocated columns (estimates), with the sampled bytes that no row took;
   one protocol bump. Tests: contract, worker, a real run.
5. [x] Compare: a check box like New and dead objects, the note that says what the figures are, and a step from a row to the
   Allocations page with the interval and type chosen. Wording drafted for the owner. Tests: view models, views.
6. [x] The command line: `diff --allocated`, with the trace found in the session (decision below). Tests: CLI.
7. [x] The sample's scenario and lesson (ADR-042), or why not; drafts for the owner.
8. [x] Docs: ADR-028 amended, ARCHITECTURE pages, DEVELOPER, USER-GUIDE, info-text.md, the CLI's README, Backlog.

**Decisions**
- Only a trace of the same monitoring run is linked. A trace recorded on the Processes page between two captures is not looked for: its
  session does not say what it belongs to. Decided autonomously.
- Figures are estimates and marked so, as everywhere; rows without a sample say nothing rather than zero bytes. Decided autonomously.
- Task 1 showed that the tick's type id is the type's method table, so full dumps are joined by it, not by name. Only a gcdump is joined by
  name; a name that types of several modules share goes to no row, and the note says how many bytes that is. Decided autonomously.
- The run is found by its process and time, not by its list of captures, so a capture from the Processes page during the run counts too.
  Decided autonomously.
- When a capture is asked for what was allocated since, its heap's time counts, not its file's: `SessionSnapshot.HeapUtc`. Decided
  autonomously.
- Types without an object in either snapshot have no row; the note says their bytes, and the Allocations page has them. Rows for them are in
  the Backlog. Decided autonomously.
- `diff --allocated` takes the session's trace only; a trace named by the user has no heap times to go with it. Decided autonomously.
- Task 7: a scenario of its own, Churn, since the bursts keep nothing a comparison could have a row for. Decided autonomously.

**Findings** (task 1, .NET 10.0, Windows, LeakyApi under LoadGen and DenseHeap compiling with Roslyn, 2026-11-25)
- Time. The recorded time of a full dump lay 3 to 160 ms after the target's pause (up to 220 MB at the rate DenseHeap allocates), that
  of a gcdump 3 to 6 s after (170 MB of LeakyApi's, up to 1,860 MB of DenseHeap's). A dump's own time has whole seconds only. Now: a full
  dump's last write and a gcdump's first heap node, within the pause or at most 3 ms after it (0 to 1.9 MB).
- Names. Of the sampled bytes between two full dumps, 61 to 83% join by name and 74 to 100% by the tick's type id; the rest are types
  without an object in either dump (74%: both dumps just after a collection, of a compiler's temporaries). The runtime's events name a
  nested type by its innermost name, as a gcdump does: between gcdumps 80 to 95% join by name, and the rest are types that left nothing
  behind (Activity, log scopes, compiler temporaries). Names several modules share: 0 to 0.4%; names the trace has for several types,
  which the review left without a row: 1.1%.
- Every gcdump but the first that was captured while a trace recorded had types without a name (it said that it was incomplete). The
  runtime describes a type once while a session has its type keyword, and forgets only when the keyword is turned off, which the flush
  before a gcdump did not do beside another session. So since 3.4, when runs could record allocations, the second gcdump of such a run
  was of no use. Fixed in the vendored flush, for HeapScope's three kinds of recording; another tool's session that has the type
  keyword still does it (Backlog). Test: `Each_gcdump_beside_a_recording_names_its_types`, failing without the fix for all three.

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
