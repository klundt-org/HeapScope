# Sprint 4.2.1 review: Damaged gcdumps and traces

Reviewers: solution-architect, senior-dev, gc-specialist, support-engineer (2026-11-24), on `sprint/4.2.1` against `develop`
(with PR 82, the gcdump root index). Each reviewer read the working tree (none had a shell or ran anything). Findings that
overlap are merged and keep the highest severity. Counts: 3 High, 14 Moderate, 22 Low.

Before the review: the gate passed (0 warnings, 1,967 tests; the known answers were captured only, `jq` is not installed
here); 16,000 mutated traces read or refused within bounds.

The owner decided the Moderates' handling in advance (autonomy for Phase 4): each was applied as recommended unless it says
otherwise, marked "decided autonomously".

## High

### H1. A refusal did not say where in the file, or which rule
- Reviewers: support-engineer (High), senior-dev (Low).
- Why it matters: a contested refusal the log cannot back up.
- Fix: every refusal carries its byte and rule; `TraceStore` logs a refused trace with the file's size and write time, the
  reader's, the check's and TraceEvent's versions, and for a file TraceEvent failed on the deciding method and its problem
  lines; the check has a version on the worker's versions line. Tests: `A_refusal_says_where_in_the_file_it_was`,
  `TraceReadsTests`.

### H2. A file that states another version went around the check
- Reviewers: solution-architect, gc-specialist (High), senior-dev (Moderate).
- Fix: the check fails closed: the file must start as a trace of version 4, once, and any other version or head is refused
  (`A_head_of_another_format_is_refused`, `Another_version_of_the_format_is_refused`, `A_second_start_is_refused`). The walk
  follows TraceEvent's tags and stops where it stops; numbers have TraceEvent's lengths. A newer version of the format (6) used
  to reach the reader unchecked and is now refused: Backlog.

### H3. Lists that each fit could add up
- Reviewers: gc-specialist.
- Fix: one budget for all the lists of a description, a limit of 1 MB on a block of descriptions, and its buffer pooled
  (`Lists_that_fit_alone_but_not_together_are_refused`, `A_block_of_descriptions_larger_than_the_limit_is_refused_before_it_is_read`).

## Moderate

- M1 (solution-architect, senior-dev) `size + 1` was int arithmetic. Fixed: sizes are added as long, and the limit above.
- M2 (solution-architect, senior-dev, gc-specialist, support-engineer) A type the format does not have, in the first list, was
  said to be left to the reader and was not. Fixed in the confirmations (C1): such a type is refused in either list.
- M3 (solution-architect, senior-dev, gc-specialist) Only the conversion ran on the deep stack. Fixed: the thread is gone, since
  every trace that is read is now bounded at 64 levels.
- M4 (solution-architect, senior-dev, gc-specialist) The disk, access and too little memory could read as a damaged file. Fixed:
  `IsFileError` excludes them, and the conversion uses the same rule. Tests: `TraceReadsTests`.
- M5 (all four) Calls into TraceEvent outside `TraceReads`. Fixed: moved; `IsFileError` compares assemblies.
- M6 (senior-dev, solution-architect) No test of `IsFileError`, of the wiring, or of a real trace cut short. Fixed.
- M7 (gc-specialist) Nothing showed the walk agrees with the runtime's writer. Fixed: the check returns what it walked; the
  recorded LeakyApi trace is walked to its end tag, as is a recording of event sources with numbers, text, structs, arrays and
  arrays of structs (.NET 10 describes the fields of plain events only, so the list of sized fields is tested by hand), and a real
  trace cut at three places opens with what came before.
- M8 (solution-architect, senior-dev) The mutation test counted the whole process's memory beside other tests. Fixed: it runs
  alone; it truncates too, sums the samples, and fails when it refuses everything or nothing.
- M9 (gc-specialist) Amounts from the file could overflow the total. Fixed: an amount below 0 or above 1 TB, or a total past
  half of a long, is refused.
- M10 (gc-specialist) TraceEvent takes running out of memory as the trace's end. Confirmed by running it under a limit; fixed:
  the conversion log's line is recognized and reported as too little memory, with a next step.
- M11 (gc-specialist) The gcdump's root index was checked for range only. Fixed: it must be a described pseudo node, -1
  included; checked before anything is loaded.
- M12 (support-engineer) TraceEvent's text went into the message. Fixed: the exception's type and HeapScope's words; the log has
  its problem lines, as for an opened trace, and its message at Debug.
- M13 (support-engineer) The only next step was to record again. Fixed: copy it again and compare its size first. "Crafted" is
  gone from the traces' texts. The owner approved the texts on 2026-11-24.
- M14 (solution-architect, support-engineer) Docs. Fixed: ADR-028, logging.md, security-privacy.md, code map, ARCHITECTURE.
- The check's shape for 4.3 (solution-architect, Moderate): deferred to 4.3 task 4, and written in ADR-028.
- An uncompressed record's own size (senior-dev Moderate, gc-specialist in H2b): kept. TraceEvent steps by the payload's size in
  a block of descriptions, and so does the check; the runtime always compresses. Said in the code.

## Low

Fixed on the way: cancellation per description; named constants; a phase ("Checking the trace file"); `ProcessName` read once;
every flag of a compressed header tested; fuzz.yml's comment. Not fixed: the file is opened twice (Backlog); the mutation job's
duration is not recorded; the other gcdump refusals name no file; `IsFileError` walks the stack in a
filter; a zero-filled tail inside a block of descriptions is refused; the read left running after a test's timeout.

## Confirmations

Each reviewer read the fixes (2026-11-24) and confirmed its findings, but for these, fixed:

- C1 (gc-specialist, High) Where the check gives up a list and TraceEvent does not, the rest would go unchecked. Looked up in
  TraceEvent 4.0.0's source: it accepts a type of a later version in version 4. Fixed: version 4's types and no other.
- C2 (solution-architect, gc-specialist: to be answered) Does TraceEvent take a description from a block of events? Looked up in
  the same source: not in version 4; only its version 3 path does, and version 3 is refused. Said in the class.
- C3 (solution-architect, senior-dev, High) `CraftedTraceTests` joined the heavy collection without its trait, which
  `build/test.sh` refuses. Fixed.
- C4 (support-engineer, Moderate; senior-dev, Low) The refusals for no samples, an amount and too little memory were logged
  without the file's identity and the check's result. Fixed.
- C5 (support-engineer, Moderate; senior-dev, Low) A sound trace of another version and a damaged head got one text. Fixed: the
  version is named, and only a damaged file is told to be copied again.
- After that all four confirmed. Looked up for gc-specialist in the source the package is built from (10e5d27): TraceEvent reads
  every type the check accepts, and has no limit of its own on depth here.
- The gate then failed on two tests whose expectations were wrong (a refusal's new words; how many fields .NET describes) and
  passed with them corrected.
- Low, fixed: the type rule of `IsFileError` tested by itself; the conversion called through `TraceReads`; the memory line
  matched at its start; the event source test counts every field; ADR-037 points to ADR-028; the reader's version is 2.

## TraceEvent 4.0.2 (added after the confirmations)

The owner asked (2026-11-24) why not take TraceEvent 4.0.2 in this sprint. It is the only package that changed (MIT, the same
files and dependencies, signed by Microsoft). Checked:

- The package's commit is not public, so the two binaries were compared, method by method, by their compiled code. What reads a
  trace's descriptions and the blocks' contents, and so what the check follows, is the same. In EventPipe reading:
  `EventPipeEventSource.Process` and `DispatchEvent` and the loop over the blocks stop when asked to, and the read of a related
  activity's id is checked (its size is set by unchanged code). Elsewhere, among others: values read from an event must lie
  within it, call stacks are interned in arrays, the dynamic parsers and the reader of native modules changed.
  FastSerialization: a 64 KB write buffer and a bulk copy; its deserializer is the same.
- Traces, by hash of every sample, stack, frame, type, time and amount, and of refusals: 14 files (four distinct ones with
  samples, the same damaged file twice, the rest without samples), and three recorded for this on .NET 8.0.34, 9.0.23 and
  10.0.15 (allocations, collections, exceptions, tasks, counters, related activities, the rundown; about 1,450 samples each).
  All the same. .NET 11 was not tried.
- 9,500 damaged copies of three traces: 44 differ. 33 open with both and 11 are refused by both; none changed sides. Where they
  differ, 4.0.2 refuses to read a value past its event's end and the conversion stops there and says so, while 4.0.0 read on (9
  of them then gave another count of lost events; 2 gave samples after it). Longest read 675 ms, most allocated 17 MB.
- Too little memory (`DOTNET_GCHeapHardLimit` of 6 MB): both give the same words, which HeapScope recognizes; from 8 MB both
  read the whole trace.
- `allocations` benchmark, 5 alternating runs each: 0.4 to 0.6 s with both; growth 29 to 30 MB (4.9 MB trace) and 16 to 17 MB
  (0.7 MB) with both. 3 alternating runs of larger traces, while the gate ran (so the times say little: 1.7 to 3.5 s and 1.7 to
  2.8 s): growth 112 MB before and 104 MB after (36 MB trace), 98 MB before and 73 MB after (7 MB, .NET 10).
- Capturing, watching and triggering a collection are not traces that can be compared: the gate (pass, 0 warnings, 2,008 tests; the known
  answers captured only) ran `GcDumpTests`,
  `GcDumpSnapshotTests`, `CounterMonitorTests` and `LiveWorkflowTests` on 4.0.2. The gcdump capture ends its session before it
  asks TraceEvent to stop, and uses nothing of that session; nothing else that is live stops early.

Reviewed by all four (2026-11-24; none High):

- D1 (solution-architect, Moderate; senior-dev, support-engineer, Low) The check's comment credited 4.0.2 with 4.0.0's source, and
  "the same compiled code" was too broad. Fixed.
- D2 (solution-architect, Moderate; senior-dev, Low) The docs said every error on an event is a refusal. Fixed: while converting it
  ends the trace, with a note; after that it is a refusal; damage within an event's bounds is not noticed.
- D3 (solution-architect, gc-specialist, Moderate) The evidence covered traces only and overstated its 14 files. Fixed above.
- D4 (gc-specialist, Moderate) A related activity's size, if EventPipe did not set it, would cut sound traces short. Looked up (it is
  set) and tried on three runtimes.
- D5 (solution-architect, Moderate) No procedure for the next update. Fixed: DEVELOPER.md 2; the comparison tools and Dependabot's
  group are in the Backlog (decided autonomously).
- D6 (support-engineer, Moderate) The package notes named a command that does not exist, and both notes overclaimed and left out what
  4.0.2 changes for a user. Fixed.
- Low, fixed: an `InvalidDataException` case; the memory case rerun; xunit.v3's version in two lists. Not fixed: a damaged live
  stream's `InvalidDataException` is not caught by `CounterMonitor` and `GcTrigger` (a stream cut short does not give one: that
  code is unchanged); 4.3's probe is rerun in 4.3.
- All Moderates confirmed by the reviewers that raised them (gc-specialist, solution-architect, support-engineer); their new Lows
  ("can open", two sentences of DEVELOPER.md) are fixed.
