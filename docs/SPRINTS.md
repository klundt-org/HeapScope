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
3. [x] A WPR recording of the smoke run (CPU sampling, context switches, ready thread; Windows' own `wpr.exe`, an admin prompt
   the owner approves) beside the `--smoke-trace` EventPipe trace with JIT events, read by a small TraceEvent tool in
   `build/` that attributes each first show over 100 ms and each dispatcher lag over 100 ms to JIT, styles and templates,
   layout, GC, or the UI thread waiting for the CPU. Proof: the tool's unit tests on a recorded trace's numbers; its report
   in `budgets.md`.
   Done 2026-11-13: five WPR recordings (2026-11-12, the quiet window: two quiet smoke runs, one with four busy cores, a
   Medium first load, and one more smoke run); the WPR profile's own .NET events gave the JIT events, so no separate
   EventPipe trace was needed. The tool is `ui-stalls` in `perf/HeapScope.Benchmarks`, beside the existing `trace-report`,
   not in `build/`: it shares that project's TraceEvent reference. Its rules (`UiStallAttribution`) are tested in the new
   `tests/HeapScope.Benchmarks.Tests` on one recorded stall's numbers (a mutation of the split fails two tests). Report in
   `budgets.md`: no stall waited for a CPU, even under load; first shows are mostly styles and templates, then GC; under load
   GC grows to a quarter of the lag; cold start is JIT, the runtime's own code, and waits (one inside Defender's scan).
4. [ ] Fix what the trace supports for cold first shows (for example preloading templates or JIT warm up off the UI thread),
   each fix measured against the build before it. Proof: smoke runs on the demo dumps and on the Medium dump, before and after.
5. [x] A ceiling on slow first shows: the smoke run fails above the measured count of first shows over 100 ms. Proof: a test
   of the rule; a mutation that lowers the ceiling fails the run.
   Done 2026-11-13: the open run counts UI operations over 100 ms in every phase (R3's `SmokePhases`), logs them by phase
   ("Smoke slow operations"), and a passing run with more than 12 fails with exit 7 (`SmokeVerdict.Ceiling`; not headless).
   Measured before it: 0 to 4 per run on the development machine, 5 to 10 with four more cores busy. `SmokeGateTests` test the
   rule; a mutation that moves the threshold fails them. A smoke run with the new loader and the gen 0 budget passed with 1.
6. [x] The dispatcher lag under load (Backlog High, from 0.3) and the worker's Server GC threads: settled from task 3's
   trace, with a fix if it shows one (job object CPU rate, EcoQoS or GC heap affinity). Proof: the trace; smoke runs under load.
   Done 2026-11-13: no stall waited for a processor, even with four cores busy, so none of the three would help; the UI
   process's own gen 0 and gen 1 collections were a quarter of the lag under load. The app runs with a 64 MB gen 0 budget
   (`System.GC.Gen0Size`), which halved the GC time inside the lags under load in A/B smoke runs (budgets.md); the rest of
   the lag is styles and templates (task 4). The worker's Server GC threads are not in the UI's lags.
7. [x] UI cold start measured again on Avalonia: the first window and the first Compare tab, published ReadyToRun. Proof:
   numbers in `budgets.md`.
   Done 2026-11-13: first window 0.77 to 0.87 s warm, 6.9 s on the first run after publishing, about 1.0 to 1.1 s with four
   cores busy; the Compare tab's first show at most 60 ms of lag and 121 ms of operation (budgets.md).
8. [x] The slower first load traced on the Medium dump on a quiet machine (the object pass), with the cause recorded and a
   fix proposed with its cost; built only with the owner's OK. Proof: the trace and the numbers in `budgets.md`.
   Done 2026-11-13: CPU work, not disk (the WPR recording: 50.7 of 56.5 s on a processor); ClrMD's type factory per object
   was the largest part, and the machine's load changed one build's time from 24 to 52 s. The owner approved the fix
   (2026-11-13, "do all recos"): the loader maps method tables to types once per type (with R4); Medium's first load went from
   about 21 s to 13 s with the same graph (budgets.md).
9. [x] The older cache fallback (3.10 M14, M29 to M31, M37), built and reviewed as a feature: a rebuild that cannot reach a
   trusted DAC (gone, symbol server failure, untrusted after a "no") opens the older cache with a note; caches older than the
   v3.9.0 loader are refused; the first read's memory freed before the second; graph views work and value reads are refused
   with the reason. Proof: worker tests with a stand-in loader for each failure; an integration test in both memory modes;
   a test per refused view; the log line naming the fallback and why.
   Done: `IndexCacheReader.ReadMetadata` reads the loader version without the graph; a cache from loader version 3 (v3.9.0) or
   later opens in place of a rebuild that fails with `UntrustedDacException` or `RuntimeComponentMissingException` (now also a
   failed symbol server download), after the failed load's memory is collected; never on a damaged dump, an explicit Rebuild or
   a reopen after a worker restart. Its values are refused through task 16's state, so references, referrers and paths open
   without field names (M37). `CacheRebuildTests` (both reasons in both memory modes, the floor, a damaged dump), each change
   proved by mutation; the warning names both loader versions and the failure.
   Reopened 2026-11-12 by its feature review (all four reviewers; senior-dev H), done again 2026-11-13 (ADR-041): every full
   dump opened from a cache checks its DAC first, so a missing DAC refuses value reads from the start too (the review's
   other H); the refusals, the note and the statuses use the owner's wording, with a next step that works while the snapshot
   is open, and the full detail in the log; the process name is read again from the dump; a download is resolved before the
   runtime is created, so a downloaded DAC that fails to load is a damaged dump, not a missing DAC; the DAC check runs before
   the cache is read (no graph to leak); a cancel is a cancel; a reopen after a restart has its own message; the app offers
   Trust this DAC and reopen (`SnapshotDescriptor.UntrustedDac`, protocol 22). Proofs: `SnapshotTests` on a real dump with
   a version 3 cache, in both memory modes, on a worker that sees no installed DAC (`--test-hide-installed-dacs`, test hooks
   only), and with an untrusted DAC trusted after the open; `RefusedValueReadsTests` (object details, references, referrers,
   paths, string instances and a value filter, interface grouping); `CacheRebuildTests` (missing and untrusted on a current
   cache, the older cache failing too, cancel, the restart message, the log lines); `MissingDacTests` on the fixture dump
   (the open's check, a failed download, a downloaded DAC that cannot load); `SnapshotsViewModelTests` (the button, the
   status). Mutations of the missing-DAC refusal and of the download rule fail them. Not covered: the command line end to
   end (its worker cannot hide the installed DAC); the worker path under it is.
   Re-reviewed 2026-11-13 by all four; their one High (the download looked a build-id DAC up under the host's platform,
   not the dump's) and their Moderates fixed: the status after a "no" is the approved one; Trust this DAC and reopen asks
   the memory question first (the probe now reports an older loader's cache as one to rebuild), keeps the memory mode and
   reopens in place under the same handle; a tripwire test makes a loader version bump reword the note; the cancel test
   fails when the cancel check is removed (mutation); `MissingDacTests` run in CI on Windows and Linux; the test switch
   cannot be set from configuration and is logged; the graph cannot leak after the fallback; a failed fallback collects
   once; an unreadable metadata section is rebuilt without being read; a cache replaced meanwhile is not opened as the
   older one. Their Lows are in the Backlog. The owner approved the button's label and tooltip and the refusal for a DAC of
   another system on 2026-11-13, with the B1 status lines.
10. [x] A negative `gc-heap-size` sample dropped and logged. Proof: a counter test with a negative sample.
    Done: `CounterSampleBuilder` drops it with a warning naming the value, the time and gen 0's size;
    `CounterSampleBuilderTests.A_sample_with_a_negative_heap_is_dropped_and_logged`, proved by mutation.
11. [x] A seam in front of `DumpCapturer`, so the capture gate and a cancel while writing are tested deterministically (one
    pid refused, two pids together, released on failure and cancel, a cancel while the dump is written). Proof: those tests;
    `CaptureTests`' cancel test no longer depends on timing.
    Done: the seam existed (`IDumpWriter`); `tests/SharedCapture/HeldDumpWriter.cs` holds a write until the test releases it.
    `CaptureGateTests` (four tests, every wait bounded) and the real cancel test hold the runtime's own writer; mutations of
    the gate and of the delete on cancel each fail a test.
12. [x] M6 from the 3.11 review: a Type or Namespace group row's retained bytes marked as a lower bound in the Retainers tree
    and its CSV export. Wording approved by the owner in `docs/design/info-text.md` first. Proof: view model and export tests.
    Done 2026-11-13: the worker's tree rows carry "Retained is" (`DominatorTreeColumns.RetainedIs`: exact for an object, at
    least for a group or a "more" row), which the CSV export writes and the grid shows as "≥ " before a lower bound; grouped by
    Type or Namespace, the tree's note is the owner's text. `RetainersViewModelTests`: the cell, the note, and the export's
    column per row.
13. [x] The refactor analysis: all four reviewers report candidates; `docs/design/refactor-analysis.md` ranks them by value and
    cost, with `PlatformHints` (3.10 M36). The owner decides; any refactor is built only on the owner's pick. Proof: the document.
    Done: [design/refactor-analysis.md](design/refactor-analysis.md), 2026-11-11: eight bugs (B1 to B8), five candidates tied to this
    sprint's tasks, fourteen ranked for before Phase 4, and those that wait for a Phase 4 pick. Waiting for the owner's picks.
14. [ ] Docs: `budgets.md`, ADR-006 and ADR-036 amended for the fallback, `DEVELOPER.md` for the new tools, the Backlog.

From the refactor analysis (owner, 2026-11-12: every bug and every candidate tied to this sprint's tasks):

15. [x] R1, before task 9: the snapshot open pipeline moved out of `SnapshotStore` into a `SnapshotOpener`, behavior unchanged.
    Proof: the existing open, cache and rebuild tests unchanged; new opener tests per cache refusal reason.
    Done: `SnapshotOpener.cs` (444 lines; the store is 243, from 623); it logs under the store's category, so every line
    reads as before. Worker (180), architecture (43) and integration open tests (37) pass unchanged. The opener tests per
    refusal reason come with task 9, which changes what the older loader reason does.
16. [x] R2 and B2, with task 9: one value-reads state per snapshot (available, or refused with a reason: no reader, declined or
    missing DAC, older cache), read by the feature rules, inspection availability, object reads and value filters. Proof: a
    worker test opening with the DAC declined lists value reads and every field-reading inspection as unavailable, with the reason.
    Done: `ValueReadsRefused` (Core) on a `DumpSnapshot` without a reader; `FeatureRules` words every value refusal from it and
    `InspectionAvailability` goes through `FeatureRules`. An open without the DAC question (the user's "no", the CLI) checks the
    DAC once and refuses from the start; the reason keeps the CLI's `--trust-dac` and the app's question.
    `InspectionAvailabilityTests` and `CacheRebuildTests`, proved by mutation.
17. [x] R3, before task 5: one smoke phase runner for the watchdog bookkeeping and the first-show log lines; "Smoke open types"
    unchanged (CI parses it). Proof: smoke runs, a test that a failing phase names its check.
    Done: `SmokePhases` and `SmokePhase` keep each phase's lag, longest operation and slow operations (the watchdog now
    counts operations over the budget since its reset); the checked maxima are the warm phases', every log line takes its
    words from one place and reads as before (a smoke run on the demo dumps, 2026-11-13). `SmokeGateTests`: the phases keep
    each measure and check only the warm ones; a failing phase names each check that failed.
18. [x] R4, with task 8: the loader's hot loop (segments in address order, pointer-free types skipped in the reference pass,
    the finalizable check by binary search, `Size` read once). Proof: the graph's columns hash the same before and after on the
    fixtures and the Medium dump; known answers; load phase times.
    Done 2026-11-13: `ClrMdDumpLoader.AddObjects` walks segments in address order reading method tables itself (ClrMD's own
    walk takes over a segment at an object it cannot read, so damaged dumps count unreadable objects as before), computes each
    size once as ClrMD does (a string's terminator, the minimum object size), merges the sorted finalizable list, and
    `AddReferences` reads each object's references with its type (`HeapGraphBuilder.FrozenTypeIndices`), skipping types
    without pointers. `load-dump` prints a canonical hash of the graph: the same before and after on the fixture dump and the
    Medium dump; the loader tests and the integration tier (known answers) pass.
19. [x] R5, with task 8: freeze and collection as load phases of their own, GC pauses, CPU time and hard faults per phase in
    the load's log line, and the load benchmark refusing a GC set up differently from the worker's. Proof: the log line; budgets.md.
    Done 2026-11-13: `LoadMeter` (the ClrMD loader) ends each phase with its wall time, the process's CPU time, GC pause time
    and page faults (every fault on Windows, major faults on Linux); `LoadTimings.Freeze` and `Costs`; the worker's "Loaded
    ... from the dump" line names freeze and collect and every phase's costs; `load-dump` prints them and refuses another GC
    setup than the worker's (Server GC, not concurrent, half the processors as heaps) unless `--any-gc 1`. A Medium load:
    objects 4.6 s (CPU 4.9 s, 1.17M page faults), freeze and collect 0.4 s, references 4.8 s (CPU 5.5 s).
20. [x] B1, a Settings page above About, with a Logging section: "Include filter text in logs" (on by default; it governs
    the app, the worker and the CLI, which read the same setting), "Purge logs" (the log being written is kept until HeapScope
    closes) and "Purge all" (logs, every session in the library, HeapScope's own index caches and bookmarks, after a
    confirmation; never a dump opened from the user's own folders or its caches; refused while a capture or recording runs).
    The CLI describes its command line by option, so filter text follows the setting there too. Wording approved
    (`docs/design/info-text.md`). Proof: view model tests of each action, a CLI test with the setting on and off, a purge test
    that leaves an outside dump alone, info icon and tooltip tests.
    Done: `HeapScopeSettings` (`settings.json` in the data folder; a member a newer build wrote is kept) and `LogPrivacy` (each
    process follows a change within a second; a worker given `--data-folder` reads that folder's); the worker's provenance
    writes a filter quoted on one line plus its shape, or its shape only; the CLI describes its command line by option, so
    `--filter 4111111111111111` follows the setting instead of passing as a number; the growth gate's line too.
    `SettingsService` (Application) saves the setting and purges: logs but the one being written; and, refused while a job
    runs or a session is in use, every session of the library (the current one let go of first) with HeapScope's caches of
    their dumps, the bookmarks and the logs, after closing the library's snapshots and traces. The page (`SettingsPage`,
    above About, with its own icon), the confirmation with the owner's words and buttons ("Delete everything", Cancel the
    default). Tests: `HeapScopeSettingsTests`, `ProvenanceLogTests` (on and off), `CommandLineLogTests` (on and off),
    `SettingsViewModelTests` (the setting, Purge logs, Purge all's question, its "no", what it deletes and keeps, refused
    while a job runs), `InfoIconTests` (the approved texts on screen), the shell's page order; a mutation that deletes the
    log being written fails two of them. The page's status lines are drafts for the owner (`info-text.md`).
21. [x] B3: one path identity rule (case-insensitive on Windows, ordinal elsewhere) at every path comparison. Proof: unit tests
    for both systems; a Linux CI test that two files differing only in case open as two snapshots.
    Done 2026-11-13: `PathIdentity` (Contracts: `Same`, `IsUnder`, `Comparer`) at every path comparison of the worker's open,
    the session library (its folders, deletions, in-use marks), bookmarks, settings, the app's open and trace lists and the CLI's
    trend. `PathIdentityTests` (Worker, so CI's Linux job runs it): both systems' rules, whole folder names, and two dumps
    whose names differ only in case opening as two snapshots where the system tells them apart.
22. [x] B4: `session.json` entries with an enum value this build does not know are kept as they were and saved unchanged,
    instead of failing the session. Proof: a session with an unknown kind opens, and saving keeps the entry.
    Done: `SessionDocumentFile` sets aside a snapshot or run entry whose enum value this build does not know
    (`SessionDocument.UnknownSnapshots`, `UnknownRuns`) and writes it back on save; any other damage still makes the
    session unreadable. `SessionLibraryTests.Entries_with_a_value_a_newer_build_added_are_kept_and_saved_back_unchanged`,
    proved by mutation.
23. [ ] B5: worker refusals thrown as a refusal type of their own; any other exception is logged as an error with its stack,
    and the user sees a fixed message with the trace id (wording to the owner first). Proof: provenance log tests both ways.
24. [x] B6: the fragmentation bars read their columns as every other row does; a missing column fails that inspection only.
    Proof: an inspections test with a column missing.
    Done: `ShowFragments` uses `ResultColumns.Require`; a result this build cannot show fails its own tile with "This result
    could not be shown" and an error log line. `InspectionsViewModelTests.A_result_missing_a_column_fails_its_own_tile_only`,
    proved by mutation.
25. [x] B7: every inspection records a basis (scanned, left out, reported with one cap); the collection inspections count what
    they could not read. Proof: a catalog test that every inspection's basis has scanned and left-out counts.
    Done 2026-11-13: `InspectionCounts` (scanned, left out; rows listed at most 50) on every inspection's result, logged
    before its own basis; the collection inspections count the collections whose count they could not read; the detectors'
    row lists in the log stop at 50 (`DetectorGraph.Listed`). `LeakDetectorTests`: every one of the 17 inspections records its
    counts, an unreadable list is scanned and left out, the cap.
26. [x] B8: `logging.md` matches what runs under an activity. Proof: the Markdown sweep.
    Done: the Correlation item and the Retainers paragraph say which loads have no activity yet and how to match their lines.

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
10. (2026-11-12) Every bug of the refactor analysis (B1 to B8) and the candidates tied to this sprint's tasks (R1 to R5) are in
    this sprint; the ranked candidates wait for the owner's scheduling.
11. (2026-11-12) The rule "never log filter text" becomes: filter text is logged unless the user turns it off in Settings
    (on by default, since logs stay on the user's machine; the text tells them to turn it off before sharing a log).
12. (2026-11-12) Purge all deletes logs, the library's sessions, HeapScope's own index caches and bookmarks; it keeps the
    trusted DACs and the cache key, and never touches a dump outside the library.
13. (2026-11-12) The Settings page sits above About; its three texts and the Purge all confirmation are approved.
14. (2026-11-12) M6 as proposed: the group note, "≥" in a group row's Retained cell, and a "Retained is" column in the CSV.
15. (2026-11-12) R1 is done before task 9.
16. (2026-11-13) Task 9's texts as worded in `docs/design/info-text.md` (Sprint 3.12 additions). A DAC next to a dump is
    still trusted only when the user says so (ADR-035 stands); after a "no" the snapshot offers Trust this DAC and reopen,
    which asks the same question. No status line of its own for the older cache: the snapshot's note says it.
17. (2026-11-13) The Settings page's info text is approved.

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
