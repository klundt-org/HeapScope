# Refactor analysis (sprint 3.12)

Sprint 3.12, task 13. The four reviewers (solution-architect, senior-dev, gc-specialist, support-engineer) each read the whole
codebase through their own lens on 2026-11-11 and proposed candidates; this page merges them, drops duplicates and ranks them.
It proposes; the product owner decides. Nothing here is built without the owner's pick (decision 4 of the sprint).

Cost: **S** under a day, **M** a few days, **L** a week or more. Phase 4 means the candidates in [SPRINTS.md](../SPRINTS.md).
The reviewers found no layering violation: every project reference matches ARCHITECTURE.md 3.1.

## 1. Bugs the analysis found

These give wrong results, break a standing rule or fail silently today. They are fixes, not refactors.

| # | Bug | Where | Cost | Found by |
|---|---|---|---|---|
| B1 | **The CLI logs filter text.** Its command line log keeps any option value that parses as a number or names an existing file, whatever the option: `instances --filter 4111111111111111` logs the 16 digits. ARCHITECTURE.md 11 says filter text is never logged. The UI logs its own arguments by another rule. | `CliRun.cs:95-104` (`DescribeTokens`) | S | support-engineer |
| B2 | **A declined DAC still lists value reads as available.** After "no" to the DAC question, the snapshot's features list field and string values, and every inspection that reads fields shows as runnable, while the page says they are not. Two availability rules (`FeatureRules.HasReader`, `InspectionAvailability.Explain`) disagree. | `FeatureRules.cs:121`, `InspectionAvailability.cs:41`, `SnapshotKinds.cs:145` | S | solution-architect |
| B3 | **Paths compare without case on Linux.** The worker returns an open snapshot for another file whose name differs only in case (the wrong dump is shown), and the session deletion guard counts a sibling folder of another case as inside the session. | `SnapshotStore.cs:203`, `SessionLibrary.cs:700-736`, and seven more sites | S | solution-architect |
| B4 | **A newer build's session can make an older one fail to read it.** `session.json` enums throw on an unknown value, so a new snapshot kind or source written by a later build makes the whole session unreadable, against ARCHITECTURE.md 1's promise. | `SessionDocumentFile.cs:63-66` | S to M | solution-architect |
| B5 | **Bugs are logged as refusals.** The worker maps every `ArgumentException` (an index out of range in analysis code included) to InvalidArgument: logged at Information without a stack, its raw text shown to the user. An unexpected error's message (a NullReferenceException, ClrMD or TraceEvent text) reaches the status line and the CLI's "Error:" as is. | `WorkerErrors.cs:54-73`, `WorkerHost.cs:621` | M | support-engineer |
| B6 | **One malformed fragmentation result fails every inspection.** `ShowFragments` reads columns with `Single` and `First`; a missing column throws past `RunOneAsync`, and the run reports all inspections as failed. | `InspectionsViewModel.cs:441,462,621` | S | senior-dev |
| B7 | **Seven inspections record no basis**, and the collection inspections skip unreadable collections without counting them, so "why is my collection not listed" has no answer in the log. Detector row lists in the log are uncapped. | `ReaderInspections.cs`, `GraphInspections.cs`, detectors | M | support-engineer |
| B8 | **The logging doc overclaims**: the Inspections tab and the Object view do not run under an activity, though `logging.md` lines 19 and 43 say they do. | `docs/architecture/logging.md` | S | support-engineer |

Smaller drift, fixed when the file is next touched: `AttributionText.cs:132` is `async void` and catches three exception types
only; `_ = Bookmarks.LoadAsync(handle)` is never observed (`SnapshotsViewModel.cs:513`); `TrendViewModel` never removes its
handlers (69, 135); `QueryTypeSummaryAsync` takes its lease by hand (`HeapQueryHandler.cs:61`); the Inspections tab catches
`WorkerException` only, so a cancel counts as unexpected; the CLI console shows a worker start failure's stack (Backlog 3.9 M11).

## 2. Tied to this sprint's tasks

| Candidate | Task | What | Cost |
|---|---|---|---|
| R1 Snapshot opener | 9 | Move the open pipeline (about 400 of `SnapshotStore.cs`'s 623 lines) into a `SnapshotOpener`, so the older cache fallback is built and reviewed on its own. 3.10's fallback, built into this code, caused most of that sprint's findings. | M |
| R2 One value-reads state | 9 | One per-snapshot state, available or refused with a reason (no reader, declined or missing DAC, older cache), read by every rule. Fixes B2. | S |
| R3 Smoke phases | 4, 5 | A `SmokePhase` record and one runner for the watchdog bookkeeping (the lag log line is written 13 times), so the ceiling on slow first shows has one place to count them. | S to M |
| R4 Loader hot loop | 8 | Segments in address order (skips a 55M sort and permutation), pointer-free types skipped in the reference pass (about 40% of Medium's objects), the finalizable check by binary search, `Size` read once. The graph must come out byte for byte the same. | S |
| R5 Honest load timings | 8 | Freeze and collect as phases of their own (today inside "References"), GC pauses, CPU time and hard faults per phase in the load's log line, and a benchmark that runs the worker's GC settings. | S |

## 3. Before Phase 4, whichever candidate is picked

Ranked by value for cost.

| Rank | Candidate | Why now | Cost | Lens |
|---|---|---|---|---|
| 1 | **One query lifecycle for view models** (Backlog 3.8 P8, second wave). The progress closure is copied 8 times, catch chains by hand in 6 view models, the token swap 5 times; copies drift (some log refusals, some do not). A `LatestQuery` holder and every load through `QueryRunner`. | Each Phase 4 view adds queries; each copy can miss the refusal log line review provenance depends on | S to M | all three code lenses |
| 2 | **Refusals with a check name, in both front ends**: a `CliContext.Refuse` helper (20 unlogged "Error:" writes, 66 unlogged validators; Backlog 3.8 M10, 3.3 L15, 3.9 M13), `CaptureService` logging its refusals, `LogShown` with the error code, refused page reads logged once. | A contested CI gate exit 1 must be backed by the log; Linux capture adds refusals on these paths | S to M | support-engineer |
| 3 | **Shared test kit**: one `WaitUntilAsync` (8 copies), `TempFolder` (36 hand-made), a view model host builder (10 copies), one fake reader (3 copies), a `TimeProvider` in the grid debounce instead of a fixed sleep. | Fewer timing waits, fewer flaky tests; every Phase 4 feature adds view model tests | S to M | senior-dev |
| 4 | **One type key** (Backlog 3.8 S5, widened): three implementations of one equality rule and about 15 sites building `(name, module)` by hand. | Between-snapshot allocations join on this key | S | solution-architect |
| 5 | **Whole-graph scan hygiene**: spans taken once per loop, bit sets instead of per-object `HashSet` probes, a type mask instead of a delegate. Two inspections take 0.6 s on Medium where a hoisted one takes 0.05 s. Plus one reachability walk instead of three. | Every new detector adds such a loop; mapped mode pays most | S | gc-specialist |
| 6 | **Reservations for the first load, key paths and detector runs**, and a refusal that evicts the set cache first (Backlog 3.1 M2, 3.2, 3.5 M14). | Live sessions and Compare make concurrent large work normal | S to M | gc-specialist |
| 7 | **Capture availability in one place** (3.10 M36, wider): five places decide or word "can this system capture", Home binds a static. | Linux capture otherwise edits all five in step | S | solution-architect |
| 8 | **Tree grid base** for the Retainers and allocation trees (about 110 near-identical lines, the reveal state machine that took review rounds). | The sunburst and between-snapshot allocations are the third and fourth trees | S to M | senior-dev, solution-architect |
| 9 | **Derived slot plumbing in the worker**: six getters copy `SharedAsync`, a new slot needs four edits, linked token sources undisposed. | New per-pair and trace-derived slots | S | senior-dev |
| 10 | **Provenance declared per property** and shared by every process: today any string property with one of 23 names is logged verbatim, unchecked. | WPF detectors read binding paths and names, which are dump contents | S to M | support-engineer |
| 11 | **Worker refusal exception and a refusal catalog**: fixes B5, and gives each refusal an id the front ends render with their own next step (worker messages name CLI flags the UI shows). | One source for wording across front ends | M to L | support-engineer |
| 12 | **Smaller support items**: a capped list helper for log lines, `--log-level` for the CLI (3.9 M14), `AllocationTrace=` instead of a second `TraceId`, required loggers. | Streamed events need a rate cap | S each | support-engineer |
| 13 | **Type grid toolbar** (Backlog 2.3.7 M14; its trigger has fired, Trend is the third copy and has drifted) and **typed fake rows** (2.3.7 M15, 41 positional call sites). | Phase 4 adds type grids and columns | S to M | senior-dev |
| 14 | **Split `SessionLibrary`** (969 lines, nine public types) and `session.json` traces as entries. | Compare's allocations need traces in the session | M | senior-dev, solution-architect |

## 4. Only with a particular Phase 4 pick

| Candidate | Pick | Cost |
|---|---|---|
| Live page view model split (Backlog 3.8 P11): 764 lines, three concerns, replaced runs never disposed, file I/O on the UI thread | Profiler-style live session (do it first) | M |
| Allocation model keyed by type and frame identity, absolute times, built in chunks; a streaming trace reader (Backlog 2.5); one worker resource scope for snapshots and traces | Live session, or allocations between two snapshots | L |
| Analysis workspace per snapshot (Backlog 3.8 P21) | Analysis tabs | L |
| Capture availability per operation, with seams per system | Capture on Linux and macOS | M |
| Inspection applicability (`AppliesTo`) | WPF leak detectors | S |
| Parallel loader; graph built straight into the mapped cache; objects-by-type index; gcdump loader memory (Backlog 1.8) | Larger dumps, gcdumps at scale | M to L |
| Set retention bounded by the members' common dominator; field accessors resolved once (Backlog 1.6 M15); address index kept; path signature memo as an array | Faster analysis on large dumps | S to M each |

## Backlog items

The reviewers checked every Backlog refactor and memory item in their lens against the code: all still hold. The ones above
name their Backlog entry; the others are measurement gaps (mapped dominators on a 16 GB machine, the cost of the DAC trust check
on every cache open) that stay in the Backlog as they are.
