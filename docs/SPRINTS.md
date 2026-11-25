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
| 4.3 | Live session | In Progress | | |
| 4.4 | Allocated between two snapshots | Planned | | |
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

## Sprint 4.3: Live session

**Goal:** while HeapScope watches a process, the Live page also shows what it is allocating, by type and by call stack, about
a quarter of a second behind it (ADR-037), and a snapshot can be taken that stops the process for less time than a full dump.

**Starting point.** A monitoring run already records the allocation session to a `.nettrace` file in the session folder
(`AllocationRecording`), and the worker reads finished traces (`AllocationTraceReader`, `TraceStore`). ADR-037 fixes where a
live trace is read (the worker tails the file) and sets two gates to measure before building on it. Refactor analysis row 4
says what to do first: the Live page's view model is 796 lines with three concerns.

**Tasks**
1. [x] The gates of ADR-037, measured: a tail in a process of its own (working set, delay, ticks lost), and folding stacks by
   module and offset as they arrive (CPU, memory, flat out and at about 70 ticks a second); whether a short rundown-only
   session at the start names methods compiled before it. Also the pause and size of a dump with the heap only
   (`DumpType.WithHeap`) against a full dump and a gcdump, and whether HeapScope opens it. On a quiet machine. If a gate
   fails, stop and report before building. Proof: the numbers in ADR-037's amendment.
2. [x] The Live view model split (Backlog 3.8 P11): live monitoring, past runs, the run's files over one state (`LiveRunState`);
   replaced runs disposed; no file reads on the UI thread. No behavior change, but a past run still being read is not shown
   over a run that started meanwhile. Tests: the existing Live tests, plus one per concern.
3. [x] The live file: each chunk of the session's stream written as it arrives, under its final name, owner-only, shared for
   reading and deleting; `AllocationRecording` says when it ended, after the flush. Tests: a reader beside the writer, an end
   in mid-chunk, a canceled run.
4. [x] A streaming reader in Loaders.Trace: tails the file, keeps aggregates (types and sizes per time bucket; stacks folded by
   module and offset, named when names are known), bounded in memory and reserved. Tests: a growing file replayed in pieces
   gives the same totals as the finished one; names that arrive late; a file that stops growing; a damaged file.
5. [x] The worker contract: open a live trace, "what changed since", "the recording ended"; one protocol bump; the identity of a
   live trace across a worker restart. Tests: contract, restart, and refusal tests.
6. [x] The Live page: allocations by type beside the timeline for the selected range, stacks on demand, and the delay shown when
   it grows; the Allocations page opens the same file afterwards. Wording drafted for the owner. Tests: view models, views,
   and the live smoke run with the UI's longest single operation.
7. [x] The faster snapshot, only if task 1 shows it is worth it: it does not (Findings), so nothing is added.
8. [x] The sample's scenario and lesson (ADR-042's extension contract) for the live session; drafts for the owner.
9. [x] Docs: ADR-037 amended, ARCHITECTURE and its pages, DEVELOPER, USER-GUIDE, info-text.md, Backlog.

**Decisions**
- The gates come first, and a failed gate stops the sprint for the owner. Decided autonomously.
- The Live view model split precedes the feature, as the refactor analysis asks. Decided autonomously.
- "Snapshots faster than a full dump" starts as a measurement of the heap-only dump; what is built follows the numbers, and if
  nothing beats a gcdump clearly, nothing is added. Decided autonomously.
- Monitoring runs always record live, in the app and wherever else; the Processes page's recording and `trace` keep their partial
  file. The live panel shows for runs that record allocations, which stays the user's choice. Decided autonomously.
- A live trace's queries are a finished trace's, over entries that stand for several samples, in place of new queries. Decided
  autonomously.
- Its identity across a worker restart is the recording's start (ADR-037 said creation time). Decided autonomously.
- Streaming stays request and answer, no push (ADR-037). Aggregates only; the file stays the record. Decided by ADR-037.

**Findings**
- Task 1, 2026-11-24 (reading TraceEvent 4.0.0's members, nothing run): `EventPipeEventSource`, which the 3.8 spike tails the
  file with, exposes no call stack (its stack cache and the event record are internal). TraceEvent's live stacks come from
  `TraceLog.CreateFromEventPipeSession`, which takes the session, not a stream or a file, so it does not fit the worker
  tailing the file (ADR-037). Candidates to measure: a small reader of the nettrace blocks of our own (stacks by instruction
  pointer as they arrive), or converting the file's prefix when stacks are asked for (`CreateFromEventPipeDataFile`). Its
  start-of-session rundown is a separate short session, as ADR-037 describes; HeapScope can start the same itself.
- Task 1, 2026-11-24, call stacks (`live-stacks`): the stack read from TraceEvent's internal event record, its layout checked
  first, in a reader process of its own with workstation GC. The developer machine (32 GB, 7.8 GB free) with light other use, 2
  to 7% of the processor before the runs; runs with and without stacks alternated. In every run with stacks, each stack read
  live was the one TraceLog gave for the finished file (compared as multisets; an earlier run of a minute: 1,051,222 samples,
  55,965,064 frames), and no event was lost.

  | Run (15 s each) | Samples | Delay p50 / p95 / max | Reader CPU (reading and folding) | Peak working set |
  |---|---|---|---|---|
  | flat out, no stacks, 4 runs | 291,764 to 311,862 | 180 to 183 / 235 to 237 / 252 to 261 ms | 438 to 500 ms | 52 to 53 MB |
  | flat out, stacks, 4 runs | 290,815 to 326,214 | 181 to 182 / 236 to 238 / 253 to 264 ms | 859 to 1,016 ms (345 to 398 ms) | 64 to 67 MB |
  | about 70 a second, no stacks, 2 runs | 1,081 and 1,085 | 184 and 186 / 233 and 235 / 249 and 252 ms | 516 and 594 ms | 39 MB |
  | about 70 a second, stacks, 2 runs | 1,083 and 1,084 | 171 and 185 / 219 and 234 / 236 and 239 ms | 562 and 578 ms (11 and 18 ms) | 40 to 41 MB |
  | the C# compiler building HeapScope, stacks, 2 runs | 16,072 and 18,833, up to 1,442 types and 10,272 stacks | 107 and 117 / 191 and 205 / 4,201 and 5,247 ms | 703 and 1,016 ms (94 and 118 ms) | 70 MB |

  The memory and some of the CPU with stacks are the probe's own (every sample kept for the comparison).
- A rundown-only session at the start took 123 to 144 ms on about 800 methods, and 316 to 491 ms (20.5 MB) on the compiler's
  45,600; it named every frame in both, the compiler being warm. A copy of the file cut at half time converted without an
  error (17 to 61 MB in 0.1 to 1.2 s) and named none of its frames: "Stacks so far" needs the start rundown too. Reading from
  a cold program, where methods are compiled while recording, was not measured.
- The late samples: when a thread stops allocating, the runtime keeps its last samples until it has something else to
  send. Shown with a target that works a second and idles three: a copy of the file taken a second into the idle time ended on
  a whole block and lacked exactly the samples that came late (294, 755 and 2 in three runs). No reader can avoid it, ours or
  TraceEvent's. With a System.Runtime counter every second in the same session the maximum delay fell from 3,147 to 3,182 ms to
  205 to 252 ms (4 runs each way), and on the compiler from 3,269 and 5,400 ms to 464 and 697 ms (2 runs each way).
- Task 1, 2026-11-24, snapshots (`dump-kinds`), a target holding 1,000 MB in 7.9 million objects, 5 runs each, NVMe:

  | Snapshot | Size | Target stopped (workstation GC / server GC) | Until the file is there |
  |---|---|---|---|
  | full dump | 1,105 to 1,109 MB | 489 to 528 / 503 to 580 ms | 589 to 695 ms |
  | heap-only dump | 1,014 to 1,035 MB | 1,255 to 1,316 / 1,259 to 1,310 ms | 1,343 to 1,393 ms |
  | gcdump | 135 to 140 MB | 662 to 837 / 1,127 to 1,152 ms | 3,398 to 4,252 ms (the command line's start included) |

  HeapScope opens all three (the same object count within 16); the heap-only dump gave 157 roots to the full dump's 253. The
  heap-only dump stops the process 2.5 times as long for 8% less file, so task 7 adds nothing, as its decision says.
- A cold program (`--cold-ms`, its methods compiled while recording, 3 runs): every stack matched, and 99.9% of the frames or more
  were named when their sample arrived (the start rundown, or a method event before the sample): 27 of 23,776, 24 of 23,767
  and 2,187 of 6,994,331 were not.
- Damaged files: 15,000 damaged copies of a small trace (5 seeds), each read with and without the stack access: it adds no
  failure seen. What a trace states is checked since 4.2.1 (`TraceFileCheck`, ADR-028); the live reader needs that check to keep
  its place in a file that grows (task 4).
- The probe again on TraceEvent 4.0.2 (2026-11-24, 10 s flat out): 175,213 samples, every stack the one TraceLog gave, every frame
  named, delay p50 185, p95 242 and max 267 ms, reading and folding 200 ms, peak working set 57 MB: as on 4.0.0.
- Linux (2026-11-24, TraceEvent 4.0.2, a GitHub runner of 4 processors, 10 s each, one run of each kind): every stack the one
  TraceLog gave, and no event lost.

  | Run | Samples | Delay p50 / p95 / max | Reading and folding | Peak working set | Frames not named when they arrived |
  |---|---|---|---|---|---|
  | flat out | 239,033 | 160 / 209 / 226 ms | 387 ms | 85 MB | 0 of 12,725,625 |
  | pausing every 200 | 10,054 | 160 / 207 / 220 ms | 36 ms | 64 MB | 0 of 535,261 |
  | a counter each second, idle 3 s of 4 | 70,421 | 144 / 239 / 318 ms | 159 ms | 80 MB | 5 of 3,749,021 |
  | a cold program | 208,791 | 160 / 210 / 225 ms | 293 ms | 84 MB | 1,904 of 11,116,357 |

  Both gates are met on Windows and Linux; ADR-037 is amended. macOS is not measured (no Mac).
- Task 4: a recording of LeakyApi under load replayed in pieces of 1 MB and of 100,003 bytes gives the finished reader's types,
  stacks by name, counts and bytes exactly. 1,500 damaged copies (seed 43) read live as well: none fails as HeapScope's error.
- A live trace at its limits (309,968 entries, 300,001 nodes, 300,000 methods of 37 MB): 75 MB kept, 98 MB with two views, a view in 6
  ms, the longest of 2 million samples 23 ms (a stretch that grew), 300,000 names sorted in 107 ms (.NET 10, workstation GC).
- Task 5: followed across a killed worker, a recording ends with the finished file's samples, bytes and lost events.
- Task 6: `--smoke-live-process` on LeakyApi under load: 612 to 921 samples in 30 s, figures 0.25 to 0.30 s behind, stacks asked
  for every second. On a real window the longest UI operation was 30 to 35 ms in 3 of 5 runs and 160 and 168 ms in 2, with no
  processor time used by the UI thread in that stall. `--smoke-live`, without the panel, has the same stall (139 ms in 1 of 6),
  and the panel headless has none (27 to 33 ms in 6 of 6): it is the window's, not the panel's, and in the Backlog.

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
