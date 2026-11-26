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

Pending.

## Markdown sweep

Pending.
