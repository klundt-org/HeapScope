# Sprint 4.7 review: Runtimes, Linux and large heaps

Reviewed 2026-11-26 against sprint-4.6 by all four reviewers, while the 8 GB measurement (task 10) waited for free memory. Findings
that overlap are listed once, under the highest severity. Moderates decided autonomously: fixed, or deferred to the Backlog where said.

## High

- H1 (solution-architect; gc-specialist and senior-dev, Low and Moderate) `StaticsUnread` was a negative fact: a loader 5 or 6 cache of a
  .NET 8 heap dump, opened in place of a rebuild (ADR-041), lacks it and so claimed its statics were read, and the check failed open when
  CoreLib's module could not be found by its file name (a single-file app). Fixed: a positive fact, `StaticsChecked`, set only when
  CoreLib (`ClrRuntime.BaseClassLibrary`) has initialized reference statics; a full dump without it is unchecked, the fallback cache's
  note and the tripwire say so. Status: Fixed.
- H2 (support-engineer) The retained sizes' note said "Not all GC handles could be checked" for a dump whose statics were the cause.
  Fixed: worded from the roots not known, as everywhere else. Status: Fixed.
- H3 (support-engineer) The statics check logged only its verdict, and its note claimed a cause it never measured (".NET 8 or before",
  "capture a full dump" even of one). Fixed: the load logs CoreLib's initialized and listed statics and the slots found; the note says
  what was measured and names the heap dump of .NET 8 as the known cause. Status: Fixed.
- H4 (gc-specialist) A `[ThreadStatic]` struct field holding references, boxed per thread, was not rooted: on .NET 8 and on Linux, where
  no thread static root is reported, what it holds looked unreachable. Fixed: rooted at its box per thread, as struct statics are; the
  fixture has one. Status: Fixed.
- H5 (senior-dev) The growing static collections detector, which does not use the reachable set, gave a clean answer on a dump whose statics
  could not be read. Fixed: refused there, saying why. Status: Fixed.
- H6 (senior-dev, solution-architect) Linux CI ran only the CLI's known answers on .NET 8 and 9, not the known answer classes the task and
  ADR-002 name. Fixed: the same loop as Windows. Status: Fixed.
- H7 (senior-dev, solution-architect, gc-specialist) Task 10 claimed the worker refuses a load that may not fit; it only logs, and the
  reservation counted the worker's own memory twice (its fixed part and what it already committed), while the log's peak was the
  process's whole life. Fixed: the task says what was built (the front ends refuse, the worker reserves and logs; a refusal in the worker
  needs a protocol change, Backlog), the reservation is the estimate less what the worker holds already, and the log gives the peak's rise
  during the load (Windows) or the private bytes after it, named as such, and the load's outcome. Status: Fixed.

## Moderate

- M1 (gc-specialist, senior-dev) Thread statics were read on dead threads too. Fixed: live threads only, in the loader and the tests' walk.
- M2 (gc-specialist, solution-architect) A cgroup's memory files were looked for under the process's own cgroup path only, which in a
  container without its own cgroup namespace names the host's; and its use counts page cache, the mapped dump included. Fixed: the root's
  files when the path's are missing, inactive file pages subtracted, the total capped by the runtime's own limit. Deferred (Backlog): a
  parent cgroup's lower limit.
- M3 (senior-dev, support-engineer, solution-architect) The sweep of the tests' leftovers matched only Windows' random names. Fixed: Unix's
  too, and a sweep that fails says so.
- M4 (support-engineer, senior-dev, solution-architect) Docs and the Live info text still gave 1 GB for the trace and the integration tier
  as Windows only, and worker-memory.md, ARCHITECTURE.md and code-map.md did not describe the new reservations or the readers. Fixed;
  the info text is a draft for the owner.
- M5 (support-engineer) A memory refusal of an inspection logged no basis. Fixed: its estimate and inputs, and the budget's source.
- M6 (support-engineer) A dump of .NET Framework or .NET Core 3.x was told "it reads other versions too". Fixed: before .NET 5 the note
  says it is not supported (ADR-002); an unknown version is logged. README says the note is for full dumps.
- M7 (support-engineer) check-known-answers.sh deleted HeapScope's log with its work folder. Fixed: the log's tail on failure.
- M8 (senior-dev) Task 3 named a test of the CLI's line that was not written. Fixed in SPRINTS.md: the note's test is the worker's; the
  CLI prints notes as for every other note.
- M9 (senior-dev) The Duplicate strings bound's test had no ties and no totals. Fixed: ties and the totals of every row.
- Deferred (Backlog), decided autonomously:
  - M10 (gc-specialist) A reverse index whose sidecar could not be written is built in memory without a reservation.
  - M11 (gc-specialist) Inspections other than Duplicate strings that list rows (sparse arrays, large arrays) estimate a byte per object.
  - M12 (solution-architect) The X11 run's exit 3 (a dispatcher over its budget on a virtual display) hides the codes after it; a
    virtual display mode of the smoke run that exempts the timing gates as the headless run does.
  - M13 (support-engineer) A lowered trace limit is logged, not said in the stop's text.

## Low

Listed, not fixed: a thread static's root names no thread; slots of uninitialized statics are not recorded; the thread static passes
cost a DAC query per field and thread; the tests' reference walk reads thread statics as the loader does (not independent); only one
reason is given when both statics and handles are unchecked; Duplicate strings' basis lacks its cutoff; TraceReadCost logs nothing when
memory cannot be read; "loading anyway, as asked" claims what the worker does not know; the counters' interval line has no run id; the
idle recording test does not stop its recording when it fails; TopRows reads awkwardly; malformed XML in DumpCapturer; comments cite the
sprint; new fixture types share a file; Core's P/Invokes repeat Capture's; DEVELOPER.md does not name HEAPSCOPE_FIXTURE_RUNTIME; the ADR
index does not list 4.7's amendments.

## Confirmations

All four reviewers checked the fixes on 2026-11-26. Confirmed: H1 to H7, M1 to M3 and M5 to M9; the deferrals accepted. Found in the
confirmations, and fixed:
- (all four, Moderate) The first load's reservation subtracted everything the worker held, so a load beside open snapshots reserved
  nothing. Fixed: only the worker's idle memory (`FirstLoadEstimate.WorkerIdleBytes`, 256 MB) is subtracted.
- (all four, Moderate and Low) `StaticsChecked` reused the bit `StaticsUnread` had, with the other meaning, at the same loader version.
  Fixed: loader version 8, so caches of earlier builds of this sprint are rebuilt.
- (senior-dev, solution-architect) The sweep's Unix form matched names made by hand (heapscope-perf-traces). Fixed: off Windows only,
  and only a name with a digit or a capital, as mkdtemp makes; tested with a name made by hand.
- (solution-architect, M4 not confirmed) worker-memory.md, ARCHITECTURE.md and code-map.md did not describe the reservations and the
  readers, and the old fact's name was left in three docs. Fixed; ADR-041 amended for the older cache's statics.
- (support-engineer) The memory refusal's line lacked its snapshot; the note before .NET 5 had no next step; README did not say full
  dumps; logging.md lacked the new lines. Fixed. Deferred (Low): the statics' counts kept in the cache for a reopen's log.
- (gc-specialist, Low) v1's memory.stat: `total_inactive_file` preferred; the runtime's limit also caps what is available by its load.
- Low, listed: the dead thread filter has no fixture; a tie in Duplicate strings' bound is not tested; the refusal test catches any
  exception; a thread static's detail names no thread; no test that the opener adds the runtime note.

## Task 10 follow-up: the references' temporary file

The 8 GB measurement missed the 4 GB first load budget (5,279 MB), so the planned fix was built (owner, 2026-11-26) and reviewed by
all four reviewers on 2026-11-26 (commit 263ce87). Moderates decided autonomously.

High:
- F1 (support-engineer) A fallback to memory was said only in a successful load's line, so a load that failed afterwards (the
  fallback brings the doubled peak back) lost its cause. Fixed: the store reports each fallback when it happens; the worker
  logs it as a warning with the dump, the reason and the next step (`ClrMdLoaderOptions.EdgesKeptInMemory`). Status: Fixed.
- F2 (gc-specialist) The 8 GB dump was not measured after the fix. Status: open; the 2 GB dump is measured (budgets.md), and the
  8 GB rerun waits for the owner (claude-dev-2's application control blocked the rebuilt benchmark).

Moderate, fixed:
- F3 (senior-dev, gc-specialist) A failure between the build and the load's result (a cancel in the truncation check) leaked the
  graph with its mapping and file until the worker exited. Fixed: the loader and the opener dispose it on any failure.
- F4 (senior-dev, gc-specialist, support-engineer) A mapping that failed failed the load, and a write that failed read the file
  back into chunks, holding the references twice. Fixed: both read what the file holds straight into the final column, so the
  references are held once plus the chunks after; a failed read back names both causes and the next step.
- F5 (solution-architect, support-engineer) The folder and file did not go through `PrivateFiles`. Fixed: owner-only, a
  folder of another user refused (into the fallback); security-privacy.md lists it.
- F6 (solution-architect, support-engineer) No free space check; the file lives as long as a snapshot kept in memory, beside
  its cache on the same drive. Fixed: checked before the file is made (16 bytes per object plus 1 GB kept free); the lifetime
  and disk cost documented (graph-format.md 6.3, ADR-015, HeapScopeFolders, the loader option).
- F7 (gc-specialist) Freezing became the peak, by when the GC ran. Fixed: a graph of a million objects or more collects its
  copied chunks after the addresses and after the other columns (2 GB dump: 1,411 to 1,161 MB).
- F8 (senior-dev) The cleanup tests proved nothing off Windows, and the fault paths were not tested. Fixed: Linux counts the
  process's open files and mappings; tests for a faulted pass, a build canceled after the mapping, the last write failing,
  a mapping failing, the space check and the private folder.
- F9 (solution-architect) ADR-015, graph-format.md 6.1 and 6.5, ARCHITECTURE.md's Core rule and SPRINTS.md said otherwise.
  Fixed: ADR-015 amended (why not the cache: the count is not known until the pass ends), the rest brought in line.
- F10 (support-engineer) The success line named no file. Fixed: its path (Windows) or folder, and the count.
- Deferred until the 8 GB rerun, as part of closing task 10: F11 (gc-specialist) `FirstLoadEstimate` is now about 2.15 times the
  2 GB dump's peak (2,498 MB against 1,161 MB); refitted once the 8 GB dump is measured. Deferred (Backlog): F12 (support-engineer) a snapshot note for a load that fell back (user-facing
  wording; the log has it); F13 (solution-architect) `ColumnBytes` split into private and file-backed for the mode log.

Confirmations (all four reviewers, 2026-11-26): F1 and F3 to F10 confirmed; F2 open until the 8 GB rerun; the deferrals accepted.
Found in the confirmations, and fixed:
- (solution-architect, senior-dev, gc-specialist, Moderate) The read back of the file into the column was one span, which as bytes
  overflows past 2 GiB (537M references): the fallback would fail a load past the Medium tier. Fixed: read in 64 MB slices,
  tested across slices with a small one.
- (solution-architect, support-engineer, Moderate) The warning's next step said to move HeapScope's data folder, which also moves
  the cache key, the library, trusted DACs and settings. Fixed: a next step by cause (free space; a folder that is not the user's
  or cannot be written; a mapping refused, report if it repeats), no data folder advice.
- (support-engineer, Moderate) No test of the warning's wiring from the loader to the worker's log. Fixed:
  `A_load_that_cannot_make_its_references_file_warns_before_it_ends_and_loads_in_memory` (the CLI with an `edges` that is a file).
- (senior-dev, gc-specialist, Low) The free space check ran before the folder existed, so it was skipped off Windows on a first
  load. Fixed: the folder is made first. (senior-dev, Low) Two cleanup tests could not pass on macOS, one test's name claimed a
  fault it did not make, the read back's message read badly: fixed.
- Low, listed: no loader-level test of a cancel after the build; a refusal for space keeps every reference in chunks, then
  copies them (the old peak, logged); three full collections at a large freeze (the two new ones measured: 1,411 to 1,161 MB on
  2 GB, 7% more time); the free space at a write failure is not logged; the cache write's warning does not name the references'
  file.

Low, listed: no sweep of files a power loss leaves (Windows); EdgeMapping repeats Storage's mapping code; the extension point is
a folder, not a sink interface; the owner pair is taken before the graph's shape check; tmpfs data folders save nothing; the
benchmark accepts any `--edges` value; `ColumnBytes` counts the file-backed edges on Linux twice near the threshold; comments
cite the sprint.

## Markdown sweep

Every tracked Markdown file read against the code as it stands (2026-11-26). Changed:
- README.md and INSTALL.md: a heap dump of .NET 8 leaves out the statics when taken on Windows (Linux's createdump keeps them);
  INSTALL now says which runtimes are tested and what a dump of another one shows, as task 3 said (README's line a draft for the owner).
- docs/design/info-text.md: the drafts of README's line, the note before .NET 5 and the growing static collections refusal match the code.
- docs/DEVELOPER.md: `TestFolders` and its sweep, the scripts that default `HEAPSCOPE_DATA_FOLDER`, the fixtures' runtimes and thread
  statics, and the Linux CI job's sentence put back in order.
- docs/ARCHITECTURE.md, docs/architecture/code-map.md, snapshots.md and budgets.md: the integration project's Linux tier, the fixtures'
  runtimes, loader 8's statics, `TestedRuntimes`, `UncheckedRoots`, the reservations, the unbuffered trace, `StaticsChecked` in the
  unreachable rule and the older cache, and `FirstLoadEstimate`.
- ADRs: ADR-002 (before .NET 5; heap dumps on Windows), ADR-020, ADR-025, ADR-034 and ADR-037 amended for 4.7; the ADR index lists
  4.7's amendments (the review's Low).
- docs/BACKLOG.md: the 60 s trace start explained; the integration tier on Linux. fixtures/KnownAnswers/README.md: the runtimes.

No package changed (THIRD-PARTY-NOTICES.md as is); the reviewer briefs and skills hold no stale fact. No em or en dash in a changed file.
