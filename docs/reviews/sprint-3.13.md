# Sprint 3.13 review: Tutorial

Reviewers: solution-architect, senior-dev, gc-specialist, support-engineer (2026-11-15), on `sprint/3.13` against `sprint-3.12`.
Each reviewer read the working tree (none had a shell or ran anything). Findings that overlap are merged and keep the highest
severity. Counts: 5 High, 17 Moderate (2 from the confirmations), 33 Low (14 from the confirmations).

Before the review: Debug build with 0 warnings; 1,791 tests, all passed.

## High

### H1. Lessons whose steps lead to another answer than the one they check
- Reviewers: gc-specialist, solution-architect, senior-dev, support-engineer.
- Where: `LessonCatalog.cs` (steady-allocation, unbounded-dictionary, large-objects, closure, and the questions of static-cache,
  event-handlers, timers, pending-async); `SteadyAllocation.cs`; `ScenarioCatalog.cs` (Inspection's `Plants`); `SampleTests`.
- What: steady-allocation accepts `PricedOrder`, but each order allocates eight `OrderLine` records and an `OrderLine[8]`, so
  `OrderLine` tops the Allocations page by count and bytes. unbounded-dictionary sorts by Bytes +/-, where `System.Byte[]` and
  `System.String` (both decoys) grow more than `SessionRecord`. large-objects runs the Inspection scenario, which plants one
  object for field inspection and nothing on the large object heap, so the two inspections show only runtime objects (decoys).
  closure's "largest of the sample's own types by Retained" is the compiler's display class, which dominates the answer.
  Several questions ("Which type does X keep alive?") are true of the decoys `System.Byte[]` and `System.String` too. No test
  follows a lesson's steps on a real sample, which is how all of this passed.
- Why it matters: the tutorial's check contradicts what HeapScope correctly shows and grades a right reading wrong (incorrect
  result); ADR-042's promise that a lesson is held to the truth is not met.
- Fix: every question names what its steps measure, and its choices are the sample's own types (H1 with M1). The steps
  were rewritten against the screens (M6). Steady allocation's lines are a struct on the stack, so `PricedOrder` is its only
  allocation; a new sample-owned LargeObjects scenario plants `Reading[]` arrays on the large object heap beside small
  `Tag[]` arrays; PendingAsync's answer is the type the inspection row names (`PendingAsyncLeak`) and Waste's the type its
  collections hold (`WasteItem`); the closure step says the display class is the top row; Inspection has no lesson
  (`NoLesson`, with a Backlog item). The answers were read off real dumps first (the CLI's summary, inspect and diff on a dump
  of each scenario). `LessonTests` (Integration) follows every lesson's steps on a real sample through the CLI and checks
  the answer; `Every_scenario_with_a_lesson_has_its_steps_checked` fails when a scenario has no check. The allocation trace
  test checks that `PricedOrder` is the first sample type.
- Status: Fixed. Confirmed by gc-specialist, solution-architect, senior-dev and support-engineer (2026-11-15). gc-specialist
  and support-engineer noted that the dictionary step's "top row" under the Fixtures.Leaks filter is a System type holding
  the answer; the step now says "among the rows whose names start with Fixtures.Leaks, the top one grows".

### H2. A sample that cannot run breaks the Processes page and the Learn page
- Reviewers: support-engineer, senior-dev, solution-architect (Moderate).
- Where: `ProcessesViewModel.RefreshAsync`, `LoadSampleScenariosAsync`; `SampleService.ListScenariosAsync`, `StartAsync`;
  `LearnViewModel.StartLessonAsync`.
- What: Refresh reads the sample's catalog before it lists processes, with no timeout; a `Win32Exception` from starting the
  sample (antivirus, AppLocker, a missing execute bit) escapes the `SampleRefusedException` catch, so the page lists nothing
  and blames the process list. An empty `--list` is cached as an empty catalog for the session, its stderr thrown away; a
  catalog refusal shown in Status is overwritten once processes are listed.
- Why it matters: a fault in the optional tutorial takes down the core capture workflow, with a message that blames the
  wrong thing.
- Fix: `SampleService` turns a start that throws into a refusal naming the file and a next step (`RepairStep`, which says to
  update the dotnet tool when HeapScope runs as one); `--list` has a timeout that kills the sample; an empty, unreadable or
  invalid catalog is refused with the sample's exit code and message and is not kept, so the next read tries again. The
  Processes page lists processes first and reads the catalog after, its problem shown in the sample section (`SampleStatus`),
  never in place of the list. Tests: a start that throws, an empty and an invalid catalog, a sample that cannot run beside a
  full process list.
- Status: Fixed. Confirmed by support-engineer and senior-dev (2026-11-15). senior-dev's follow-up: a timeout on a pipe that
  does not honor cancellation ended the read at EOF and was reported as "exited with no message"; it is now told by the
  timeout itself, with a fake whose read ignores cancellation to test it, and a read error is a refusal too.

### H3. A refused lesson start grades the old lesson against the new answer; lesson and sample state drift apart
- Reviewers: support-engineer (High), senior-dev, solution-architect (Moderate), support-engineer (Low, the stale panel).
- Where: `LearnViewModel.StartLessonAsync`; `SampleService.Changed`, `Running`; `ProcessesViewModel.StopSample`.
- What: StartLesson stops the old sample and sets the new answer type before the new start can still be refused; after a
  refusal the old lesson's choices stay, checked against the new answer. `Changed` is never raised when a sample exits, no page
  subscribes, and exited entries are dropped without being disposed; a sample stopped from the Processes page or a terminal
  leaves the Learn page and the lesson panel showing a running lesson.
- Why it matters: "I answered right and it said wrong", with nothing in the log to explain it.
- Fix: a lesson start ends the running lesson first; the answer, choices and lesson are kept only once the new sample runs,
  so a refusal leaves none running and says which lesson stopped and why the other could not start. Samples report their
  exits (`ISampleProcess.Exited`): the service drops and disposes them, tells a stop, a stop from another HeapScope process
  and an exit on its own apart (`SampleExit`), and raises `Changed`; the Learn and Processes pages follow it through the UI
  dispatcher (the lesson says its sample is gone and can still be answered; the row loses its Sample mark).
- Status: Fixed. Confirmed by support-engineer (2026-11-15).

### H4. `sample stop` fails on Linux and macOS, and its test fails in CI's Linux tier
- Reviewers: senior-dev; support-engineer, solution-architect, senior-dev (Moderate and Low: matched by a substring of the
  command line, the logic in the front end, failures not caught).
- Where: `SampleCommands.cs` (stop), `SampleCommandTests.Stop_refuses_a_process_that_is_not_the_sample`.
- What: stop finds the sample through capture's process list, which refuses off Windows; it matches any command line holding
  "heapscope-sample" and "--scenarios" and kills the process tree; a timeout or access denied becomes an unexpected failure.
- Why it matters: a CLI command broken on two systems, a test that fails CI, and a loose guard in front of a kill.
- Fix: each running sample has a record in the data folder (`samples/<pid>.json`: pid, start time, scenarios, owner).
  `SampleService.StopRecorded` stops a pid only when its record's start time matches the running process, so a reused pid
  is never stopped; it kills that process alone; a failed kill writes the record back and is a refusal with a next step. No
  capture discovery is involved, so `sample stop` works on every system and its test runs on Linux.
- Status: Fixed. Confirmed by senior-dev and solution-architect (2026-11-15).

### H5. The logs cannot back up a contested lesson answer or a missing sample
- Reviewers: support-engineer.
- Where: `LearnViewModel` (start and answer lines), `SampleService` (resolve, catalog, start, stop, exits), `CaptureService`
  and `ProcessDiscovery` (what the list hid).
- What: the answer line has the chosen answer but not the expected type, the choices or the sample; the catalog read logs no
  folder, exit code or stderr; stop has no reason; exits on their own are counted, not named; nothing says which processes
  discovery hid as HeapScope's own.
- Why it matters: support cannot tell a wrong expected type from a wrong pick, or "not installed" from "could not run" from
  "hidden by discovery".
- Fix: the log now has the catalog read (file, executable or dotnet host, version, each scenario's type) or why not (exit
  code, the sample's message); each start (pid, scenarios, growing, time to ready, the command line), refusal (scenarios,
  reason, running pids), stop with who stopped it and exit with why (code, run time, the sample's last message, read to its
  end before it is reported); a lesson's start (answer type and scenario, catalog and lesson versions, choices), answer
  (chosen, expected, the sample and whether it still runs) and end. Every process list logs the pids it listed and those it
  left out as HeapScope's own or unreadable (`DiscoveryOmissions`), and the Processes page logs a started sample it does
  not show. logging.md describes the lines.
- Status: Fixed. support-engineer's re-check (2026-11-15) found two gaps, both Moderate and fixed the same day: the listed
  pids were not logged (they are now, 200 at most, with the page's line), and an exit's message was read before its last
  lines arrived (the report now waits for them, two seconds at most).

## Moderate

### M1. The answer check is passable without opening the app
- Reviewers: solution-architect, support-engineer.
- What: the decoys are always `System.String`, `System.Byte[]` and `System.Object[]`, so the planted type is always the only
  non-BCL choice; ADR-042 and decision 4 say "decoys from the same dump".
- Decision: fix now (decided autonomously). Each scenario names its decoys (its other types, which the steps rule out), and
  the choices are those, then other scenarios' answers: three of the sample's own types. ADR-042 amended.
- Status: Fixed. Confirmed by solution-architect (2026-11-15).

### M2. The catalog is not the only place a scenario lives
- Reviewers: solution-architect, senior-dev, gc-specialist (Low).
- What: the conflict refusal says "both block the finalizer thread" for any pair, in two places; Waste's finalizer backlog is a
  special case on its name in `Program.cs`.
- Decision: fix now (decided autonomously). The catalog holds the conflict's reason and an action for after the last
  collection; `Program.cs` names no scenario.
- Status: Fixed. Confirmed by solution-architect and senior-dev (2026-11-15).

### M3. Lessons run where they cannot be done (capture is Windows only)
- Reviewers: solution-architect, senior-dev, support-engineer.
- What: off Windows the Learn page starts samples for lessons whose first step is a capture; the Processes page shows the
  sample section without scenarios and answers "Choose at least one scenario"; a lesson whose scenario the sample lacks says
  "Reinstall".
- Decision: fix now for the pages, a per-lesson requirement deferred to the Backlog with the first lesson that needs one
  (decided autonomously). The Learn page says lessons need capture and starts none where it is not available; the Processes
  page does not offer the sample there; a lesson whose scenario the sample lacks says it is not available here.
- Status: Fixed. Confirmed by solution-architect and senior-dev (2026-11-15).

### M4. The catalog version in the sprint's scope was dropped without a decision
- Reviewers: solution-architect.
- What: neither catalog has a version, and `--list` is a bare array.
- Decision: fix now; tagging a capture with them deferred to the Backlog (decided autonomously). `--list` prints
  `{Version, Scenarios}`, `LessonCatalog.Version` exists, and a lesson start logs both.
- Status: Fixed. Confirmed by solution-architect (2026-11-15).

### M5. A growing sample grows without a limit
- Reviewers: gc-specialist, support-engineer.
- What: about 0.7 GB an hour per sample with every growing scenario, up to three samples, Keep growing on by default; the
  sample takes any count and payload, and `--grow-every 1`.
- Decision: fix now (decided autonomously). Growth stops when the sample's heap reaches 512 MB (it says so on its error
  output); count times payload is at most 256 MB and growth no faster than every 100 ms; the tooltip and the User Guide say so.
- Status: Fixed. Confirmed by gc-specialist (2026-11-15).

### M6. Lesson steps that do not match the screens
- Reviewers: support-engineer.
- What: the Retained column is hidden until Retained sizes is ticked; inspections are tiles you select, not run; only
  detector rows open as a set (the others open their objects on the Snapshot tab; Open as a set is on the right-click menu);
  the Allocations Types grid cannot be sorted (it is ordered by bytes); the capture step does not say to leave As gcdump off.
- Decision: fix now (decided autonomously). The steps were rewritten with the screens' own names and checked by `LessonTests`.
- Status: Fixed. Confirmed by support-engineer (2026-11-15).

### M7. Lesson progress: not owner-only, not atomic, lost silently
- Reviewers: support-engineer, solution-architect (Low), senior-dev (Low).
- Decision: fix now (decided autonomously). `LessonProgress` writes a new file owner-only and moves it over the old one, keeps a
  damaged file as `.bak` and logs it; a save that fails says so beside "Right"; its constructor takes a folder path, not a
  Core type.
- Status: Fixed. Confirmed by support-engineer (2026-11-15).

### M8. Refusals without a next step, or with a wrong one
- Reviewers: support-engineer.
- What: "it exited without a word", the timeout and "3 samples are running" give no next step; "Reinstall HeapScope from its
  release archive" is wrong for the dotnet tool and a source build; "no scenario X" does not say where they are listed.
- Decision: fix now (decided autonomously). Each refusal has a next step; `SampleService.RepairStep` says to update the tool
  when HeapScope runs as one. The texts are drafts in info-text.md for the owner.
- Status: Fixed. Confirmed by support-engineer (2026-11-15).

### M9. `sample start` reports a crash as a stop
- Reviewers: support-engineer, senior-dev (Low).
- What: a sample that exits on its own prints "Sample N stopped." and exits 0.
- Decision: fix now (decided autonomously). `sample start` waits on the sample's exit: a stop (Ctrl+C, `sample stop`) succeeds,
  an exit on its own fails with its exit code and message.
- Status: Fixed. Confirmed by support-engineer (2026-11-15).

### M10. The three-at-once limit is not atomic, and the catalog read can race
- Reviewers: senior-dev, solution-architect (Low), support-engineer (Low).
- Decision: fix now (decided autonomously). A slot is taken under the lock before a start and given back when it fails; the
  catalog is read under a gate. The limit is per HeapScope process, as the guide and ADR-042 now say.
- Status: Fixed. Confirmed by senior-dev and solution-architect (2026-11-15).

### M11. The start path's timeout, cancellation and error text are unreliable
- Reviewers: senior-dev.
- What: the ready timeout relies on a pipe read honoring cancellation, cannot be injected or tested, the UI passes no token,
  and stderr is read before its reader has drained, so a real refusal can read "it exited without a word".
- Decision: fix now, without a cancel button in the UI (the timeout bounds the wait; decided autonomously). The timeout kills
  the sample, is told apart however the read ends, and can be set for tests; the sample's message is read after it exited.
- Status: Fixed. Confirmed by senior-dev (2026-11-15), with the follow-up in H2.

### M12. Edge cases without a test
- Reviewers: senior-dev.
- What: the ready timeout and cancellation, Dispose, concurrent starts, an empty or invalid `--list`, a failing start, a
  refused start while a lesson runs, `NavigateRequested` driving the shell.
- Decision: fix now (decided autonomously). `SampleServiceTests` covers the timeout (both ways), cancellation, a start that
  throws, an empty and an invalid catalog, the limit with a start pending, exit reasons, records and a reused pid, and Dispose;
  `LearnViewModelTests` a refused second lesson, a sample that exits and no capture; `ProcessesSampleTests` an exit elsewhere
  and a sample that cannot run; one shared fake (`tests/SharedSample`, review L4).
- Status: Fixed. Confirmed by senior-dev (2026-11-15).

### M13. Three lessons sit exactly on their detectors' thresholds
- Reviewers: gc-specialist.
- What: the sample plants 1,000 items by default and the event handler, pending async and finalizer backlog detectors report
  from 1,000; no test runs a lesson's steps on a real sample.
- Decision: fix now (decided autonomously). The sample plants 2,000 by default, and `LessonTests` runs each lesson's steps.
- Status: Fixed. Confirmed by gc-specialist (2026-11-15).

### M14. The docs lag the sample
- Reviewers: support-engineer, solution-architect (Low).
- What: logging.md and security-privacy.md say nothing of the sample; ARCHITECTURE 3.1 has no HeapScope.Sample row or node;
  ARCHITECTURE 2 still says nothing under the installation is listed; ADR-039 is not amended; the Backlog still lists the
  tutorial as open; the User Guide's section is a summary, not the scenario by scenario walkthrough task 10 named.
- Decision: fix now (decided autonomously). logging.md, security-privacy.md, ARCHITECTURE 2 and 3.1 (row, node, rule), ADR-039
  and its index line, the Backlog (the tutorial's follow-ups) and a lesson by lesson table in the User Guide.
- Status: Fixed. Confirmed by solution-architect (2026-11-15).

### M15. The screenshot run's Learn picture does not show the lesson panel
- Reviewers: senior-dev (Low, raised to Moderate: task 10's Done note claims it).
- Decision: fix now (decided autonomously). The run starts the first lesson on the real sample, captures the page it opens with
  the lesson panel (`16-lesson-panel`) and the Learn page (`17-learn`), and fails when the lesson does not start; 34 pictures,
  both themes, on 2026-11-15.
- Status: Fixed.

### M16. The release check does not run the sample (from the confirmations)
- Reviewers: solution-architect (M6 of the first round kept open).
- What: `check-package.sh` only lists `sample/`; nothing shows the shipped sample runs on a machine without .NET.
- Decision: fix now (decided autonomously). `check-install.sh`, which CI runs on unpacked packages with .NET hidden, checks the
  sample's runtimeconfig is self-contained, runs the sample's `--list`, and, when not root (the Linux container runs as root,
  where HeapScope rightly refuses the sample), `heapscope-cli sample list`.
- Status: Fixed; runs in CI with the PR.

### M17. The logs still could not show a sample missing from the list, or a crash's last message (from the confirmations)
- Reviewers: support-engineer. Folded into H5's status above: both fixed.

## Low

Listed, not fixed (DEVELOPER.md 10).

- L1 (senior-dev). `!` suppressions without the comment DEVELOPER 4 asks for (`SampleService`, `LearnViewModel`,
  `ProcessesViewModel`, `SampleCommands`).
- L2 (senior-dev). `ISampleProcess.cs` also holds the real starter; `ProcessesViewModel` spells out
  `HeapScope.Application.Tutorial.*` instead of a using.
- L3 (senior-dev). The Learn page has two primary buttons; Start sample, Check answer and Answer on the Learn page have no
  tooltip.
- L4 (senior-dev). Three near-identical fake sample starters in the tests; `ProcessesSampleTests` does not dispose its service.
- L5 (senior-dev). A ready timeout is logged at Information.
- L6 (solution-architect). After READY nothing reads the sample's stdout; a scenario that writes there would block once the
  pipe fills.
- L7 (solution-architect). A lesson starting on a page outside `LessonPage` needs an enum value and a shell edit; answers can
  only be type names.
- L8 (solution-architect). `LessonProgress` has a public constructor taking a Core type.
- L9 (solution-architect, gc-specialist). The self-contained sample is 77 MB, untrimmed.
- L10 (gc-specialist). SPRINTS says every leak grows, but FinalizerBacklog does not (a full blocking collection per step).
- L11 (gc-specialist). `SampleTests`' whole-steps assertion can catch a growth step half done.
- L12 (gc-specialist). The steady allocation rate depends on the system's timer resolution.
- L13 (solution-architect). `ListScenariosAsync` starts the sample for `--list` even while HeapScope runs elevated.
- L14 (support-engineer). The Processes page says "Sample N stopped." even when Stop found it already gone.
- L15 (solution-architect). The ADR says the command line marks sample rows; `heapscope-cli list` does not.
- L16 (gc-specialist). Inspection's known answer (field values) is taught by no lesson.
- L17 (senior-dev). Exited samples' `Changed` would be raised on a thread pool thread.
- L18 (support-engineer). The start line lacks the launch file, grow interval and time to ready.
- L19 (senior-dev). `_catalog` is read and written outside the lock.

From the confirmations:

- L20 (gc-specialist). The 256 MB bound is per scenario, not for the whole sample, when it is run by hand.
- L21 (gc-specialist). The known answer check is a substring check (a prefix of a method key passes); `LessonTests` covers it.
- L22 (gc-specialist, also L12). A few seconds of the steady scenario hold 50 to 100 allocation samples; pace it or ask for ten.
- L23 (solution-architect). SteadyAllocation's decoy `OrderLine` never appears in a trace; the docs no longer claim decoys do.
- L24 (solution-architect). `ConflictReason` is one per scenario, not per pair.
- L25 (solution-architect). `heapscope-cli sample start` runs a sample off Windows, where the CLI cannot capture it.
- L26 (solution-architect, senior-dev). Records of a sample whose HeapScope crashed stay until that pid is asked about; no sweep
  at start.
- L27 (senior-dev). A kill that is refused in `Stop` was an unexpected failure (fixed with H2's follow-up: logged, false).
- L28 (senior-dev). `SampleStatus` stayed after a later read succeeded (fixed: cleared).
- L29 (senior-dev, support-engineer). A hung `--list` keeps Refresh busy up to 65 s after the list is shown.
- L30 (senior-dev). History notes ("Review H1: ...") in code comments, against DEVELOPER 4.
- L31 (support-engineer). The large-objects choices hold only one other array type.
- L32 (support-engineer). A sample that exits between its start and the lesson keeping its pid was not reported (fixed: the
  lesson checks once more).
- L33 (support-engineer). `ProcessDiscovery.LastOmitted` is shared state read after the list; a progress file that cannot be
  read for a moment is replaced without `.bak`; "Growth stopped" is only in the sample's error output.

## Markdown sweep

After the fixes, every tracked Markdown file was searched for what this sprint changed (the sample's scenarios, decoys and
limits, the CLI's sample commands and their texts, the Learn page, the keyboard's page shortcuts, package contents, test
names), and the files the sprint touched were read against the code. Changed by the sweep: INSTALL.md (the sample folder and
the SBOMs, with release.yml's comments), and before it, with the fixes: ARCHITECTURE.md, ADR-039, ADR-042, the ADR index,
BACKLOG.md, DEVELOPER.md, USER-GUIDE.md, budgets.md, code-map.md, logging.md, process-model.md, security-privacy.md,
info-text.md and SPRINTS.md. THIRD-PARTY-NOTICES.md needs nothing: the sample uses only HeapScope's own fixtures. Past review
files were left as written. No em or en dashes in the changed files.

## Final run

2026-11-15, after the fixes and the sweep: Debug build with 0 warnings; `dotnet test --solution`: 1,812 tests, 1,799 passed,
13 failed, all integration tests outside this sprint's code: `AllocationTraceTests` (12, at once: their shared recording of
LeakyApi failed under the full run's load) and `CounterMonitorTests.Cli_monitor_prints_samples_captures_on_a_rule_and_reports_errors_and_usage`
(already in the Backlog's list of tests that fail only under full local load). Both classes passed alone (16 of 16); the
Backlog's list now names `AllocationTraceTests`. The tutorial's integration tests (`SampleTests`, `LessonTests`, 16) passed.
