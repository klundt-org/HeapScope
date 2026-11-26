# Sprint 4.6 review: Right answers

Reviewed 2026-11-25 against sprint-4.5 by all four reviewers. Gate before the fixes: failed on two tests, both explained below (H5, H6), 0
warnings, 2,205 tests. Findings that overlap are listed once, under the highest severity. Moderates decided autonomously: fixed, or
deferred to the Backlog where said.

## High

- H1 (support-engineer, solution-architect, senior-dev) The CLI's new log of a failed start, and of a crash in a run, wrote the raw command
  line, so a filter's text reached the log with Include filter text in logs off. Fixed: described as every command's line is
  (`CliApp.Arguments`, through `CliCommands.DescribeTokens` and the saved setting); a crash and an unobserved task have lines of their
  own, and the start's message names the log folder. Test: `The_command_line_of_a_failed_start_is_logged_without_the_filter_text_unless_allowed`.
  Status: Fixed.
- H2 (solution-architect; gc-specialist and support-engineer, Low and Moderate) A cache written with the second walk of GC handles off was
  loaded again by itself once the walk was on: the dump that needed the walk off would end the worker on every open, and a rebuild that
  could not reach the DAC failed an open that used to work. Fixed: taken out; the note's step (`heapscope-cli open --rebuild`) is the way.
  Status: Fixed.
- H3 (gc-specialist) The growing static collections detector, which H3 of 4.5 changed, still said "0 findings" for a List whose count
  could not be read, and named it "collection type not counted". Fixed: a counted type whose count is unread is `Unread` ("static
  collections") and "count unreadable" in the basis. Test: `A_static_collection_whose_count_cannot_be_read_is_not_checked_and_said_so`.
  Status: Fixed.
- H4 (senior-dev) A grid's notes survived a subject change: Compare's identity and retained notes, Types' retained note and generations,
  Trend's columns and order. Fixed: each grid's `OnCleared` resets everything its answer set; the tests assert the notes. Status: Fixed.
- H5 (senior-dev) Tasks were ticked whose tests were not written. Fixed in SPRINTS.md: what was built is said as built, and the tests not
  written (H10's thread, the crash race, a throwing listener, a recording's session in use, Retainers and Object retry, hidden Trend, a
  forced collection of a suspended process) are in the Backlog. Status: Fixed (as a record).
- H6 (the gate) Two tests failed: `The_unreachable_set_is_the_objects_no_root_reaches` compared with ClrMD's walk from the roots, which
  lacks the GC handles 4.5's second walk finds in LeakyApi (Server GC): the reference was wrong, by up to 100 objects. Fixed in the
  tests' `DumpAnalysis`, whose walk adds the left-out handles as the loader does (every known answer on a Server GC fixture uses it).
  `A_live_trace_can_be_read_beside_its_recording_and_ends_whole`: a recording's stop writes its rundown, which took more than the 5 s a stop
  now waits; the session was let go and the copy failed on the closed pipe. Fixed: a recording's stop waits 30 s, and a copy a let-go
  session ends keeps what it wrote. Status: Fixed.

## Moderate

- M1 (support-engineer) A refusal for reading nothing lost the inspection's rule and basis in the log. Fixed: logged before it.
- M2 (support-engineer, gc-specialist) That refusal had no next step, was offered Try again, and counted only reachable objects without
  saying so. Fixed: "reachable", the dump's .NET version and the log for an issue; a refusal is not offered again.
- M3 (support-engineer) A failed rerun of the growth detectors had no next step. Fixed: Try again.
- M4 (senior-dev) Try again dropped the chosen baseline. Fixed.
- M5 (gc-specialist, support-engineer) The count of collections no field names fired on every dump (a framework's empty arrays) and logged
  no types. Fixed: only those large enough to report, their types in the basis, the note says "such as". Test: the fixture's
  `List<GenericStaticEntry>` among them.
- M6 (solution-architect, senior-dev) Bookmarks and settings did not use the one file helper for their lock and read. Fixed; the helper's
  lock says what to do when it times out.
- M7 (solution-architect) A recording's start had no bound: the likely cause of the CI run with no bytes for 60 s. Fixed: 30 s, then a
  message.
- M8 (senior-dev) A sample's kill swallowed real failures. Fixed: only a kill its timeout fires is quiet, and logged.
- M9 (senior-dev) The suspend test had no system guard. Fixed: Windows only.
- M10 (support-engineer) Closing did not log what it waited for. Fixed: the jobs canceled and any still running.
- M11 (support-engineer) The fuzz issue did not say how to replay a failure. Fixed: iterations, the base files, the step.
- M12 (support-engineer) logging.md described none of 4.6. Fixed.
- M13 (solution-architect) No ADR or page recorded the decisions. Fixed: ADR-020 amended; threading.md, security-privacy.md, process-model.md
  (4.6 without a version of its own), code-map.md.
- M14 (solution-architect) SPRINTS.md said the protocol was bumped. Fixed: the decision says it was not.
- Deferred (Backlog), decided autonomously:
  - M15 (solution-architect, senior-dev) Settings and lesson progress wait for their file's lock and retries on the UI thread (up to seconds
    when another HeapScope holds the file): async calls in their view models.
  - M16 (solution-architect) The unread counts reach clients as words only; a DTO field and an error code of its own when exports need them
    (4.9).
  - M17 (solution-architect) The event handler detector, and (gc-specialist) the disposed objects detector when no flag can be read at all,
    and the two collection inspections when an items field is unread, still give a clean answer; with a test per detector with fields
    missing (gc-specialist).
  - M18 (gc-specialist) A collection inside a struct static is named now but not checked by the growing static collections detector.
  - M19 (support-engineer) A stop let go after its wait is not logged by the forced collection and the counters.

## Low

Listed, not fixed: net8.0 runs of the struct static and generic static fixtures (gc-specialist; with 4.7's runtimes); struct statics
without pointers and thread static structs are not named; the root service scope counted open when its flag is unread; the unreachable
split by heap kind is now an array; `RecordNamesAsync` now stops through `SessionStop`; the older cache note's tripwire says why version 6
needs no words; a lesson progress refusal for a permission problem names the wrong step; the trace watch's warnings have no cap; an
object's failed tab says nothing of being tried again when shown again; Compare's and Trend's "only while shown" rule is written twice
(before 4.10); text scans in two tests see one spelling; `PrivateJsonFile` has no direct tests; hidden Trend keeps its result set; an
unobserved stop task; review ids in new comments.

## Confirmations

All four reviewers checked the fixes on 2026-11-25. Confirmed: H1 to H4, H6, M1, M3, M5, M6, M8 to M14; the deferrals accepted. Found in
the confirmations, and fixed:
- (support-engineer, gc-specialist) "reachable" was said of the collection inspections, which count every object, and the refusal said the
  log names the fields it read. Fixed: each detector says "reachable" in its own words; the refusal says the log has what was probed, and
  its line holds the rule's thresholds.
- (gc-specialist, Moderate, there before 4.6) A count the baseline could not read was growth from 0. Fixed: not checked, and said so. Test:
  `A_count_the_baseline_could_not_read_is_not_growth`.
- (solution-architect, Moderate) The counters' start had no bound. Fixed: 30 s and a message, and both starts wait on their own token.
- (solution-architect, senior-dev) The log's privacy check now waited up to 5 s on a held settings file. Fixed: it reads without waiting,
  closed at once as before.
- (senior-dev) Clearing Compare's generations made a sort by a generation column come back sorted by bytes. Fixed: kept; its answer sets it.
- (senior-dev) SPRINTS.md still claimed three things not built. Fixed (tasks 1, 2 and 10).
- (senior-dev) Try again chose the baseline after the run started. Fixed: before it.
- (senior-dev) Four kills of the sample stayed raw. Fixed: quiet and logged.
- (senior-dev) A crash line written beside the host's could be overwritten by it. Fixed: once the host started, through its logger; an
  unobserved task at Error.
- (senior-dev, Low) A let-go names session faulted its copy unseen. Fixed.
- The gate before these: `AllocatedBetweenTests`' recorded run did not start its trace within 60 s once, under the gate's load (it passes
  alone, 9 of 9): the CI symptom, seen locally. Its fixture waits 120 s and says what the run saw; the cause stays in the Backlog.

## Markdown sweep

Every changed page was checked against the code; the docs were searched for what the sprint changed (the loader version, test totals,
the reload taken out, the file helper, the bounded stops and starts). Changed: logging.md, threading.md, security-privacy.md,
process-model.md, code-map.md, graph-format.md, snapshots.md, ADR-020 and the ADR index, DEVELOPER.md, INSTALL.md (a Windows example),
info-text.md (the drafts), SPRINTS.md, BACKLOG.md, RELEASE-NOTES.md. No em or en dashes in changed files.
