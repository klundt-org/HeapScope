# HeapScope Sprints

Phases: **0.x** architecture, framework and skeleton; **1.x** quick wins; **2.x** heavy hitters; **3.x** depth to
match dotMemory (planned 2026-10-28); **4.x** what Phase 3 leaves out (planned 2026-11-18); the Backlog ([BACKLOG.md](BACKLOG.md)) everything else. Closed sprints are
in [sprints/](sprints/): [Phase 0](sprints/phase-0.md), [Phase 1](sprints/phase-1.md),
[Phase 2](sprints/phase-2.md), [Phase 3, the peer review sprints, 3.9 to 3.12](sprints/phase-3.md).

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
| 4.1 | Capture on Linux | In Progress | | |
| 4.2 | Capture on macOS | Planned | | |
| 4.3 | Live session | Planned | | |
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
| 4.2 | Capture on macOS | M | The same on macOS, built and checked on Linux, marked untested. |
| 4.3 | Live session | L | Allocations streamed beside the Live timeline from a trace read while it grows (ADR-037), and snapshots faster than a full dump. |
| 4.4 | Allocated between two snapshots | M | In Compare, from the trace recorded between them (from 2.5; ADR-028). |
| 4.5 | Peer review | M | A review of the phase so far, like 3.5's; its fixes plan the next sprints. |
| 4.6 | Analysis tabs | M to L | Several analyses of a snapshot side by side, each with its own path (from 2.7; peer review P21 and the Live page split). |
| 4.7 | Sunburst | S to M | A sunburst view beside the icicle chart for dominators and call trees. |
| 4.8 | WPF leak detectors (optional) | M | With a WPF fixture app (from 2.6): binding to a non-INotifyPropertyChanged source, collection bindings, dependency property descriptors, x:Name. Dropped if time runs short. |

---

## Sprint 4.1: Capture on Linux

**Goal:** everything HeapScope captures on Windows (the process list, full dumps, gcdumps, live counters, allocation
recording, the tutorial's sample) works on Linux, with the same refusals and next steps where the system says no.

**Starting point.** Capture already talks to targets through the runtime's diagnostics channel (`DiagnosticsClient`:
discovery, `WriteDump`, EventPipe), which works on Linux. What is Windows only: `CapturePlatform` refuses off Windows;
describing a process (image path, command line, runtime folder) and the dump size estimate call kernel32 and ntdll; free
disk space calls `GetDiskFreeSpaceEx`; `UnixElevationProbe` cannot tell another process's state; the UI's
`PlatformHints.CanCapture` is Windows only.

**Tasks**
1. [x] The checks by hand, automated where they can be: the headless screenshot mode (`--headless --screenshots`) also
   captures an info icon's popup open (one beside a turned-off button too), the Retainers chart with a bar's and the
   "+N smaller" bar's popups, and Settings' Purge all confirmation; DEVELOPER.md 5 keeps only what needs a person (looks
   on a real monitor, an elevated target). Tests: the screenshot run's list of files; the popup states in UI tests.
   Done: the run opens each popup (Purge all's question shown in its real window and answered Cancel, never through the
   command, which a smoke run would answer yes), fails when one does not open, and `build/screenshots.sh` fails on a
   missing picture; `SettingsViewModelTests.Purge_alls_question_can_be_shown_without_asking_or_deleting`. The popup
   states were already tested (`InfoIconTests`, `IcicleChartTests`).
2. [x] Process facts per system: an `IProcessFacts` with the Windows calls as they are and a Linux reader of `/proc/<pid>`
   (`exe`, `cmdline`, `status` for the owner, `maps` for the runtime's `libcoreclr.so` folder and version and the
   readable mappings). The parsers take text and run on every system. Tests: parser tests on recorded `/proc` text
   (cmdline separators, deleted executables, the runtime folder and version from the shared framework path, a
   single-file app without `libcoreclr.so`); on Linux, the facts of the test process itself.
   Done: `ProcFactsTests` (11; the one on the test process itself runs once the capture tests run on Linux, task 6).
   On Linux the runtime's version is the shared framework folder's name (`10.0.1`), where Windows gives coreclr.dll's
   file version (`10.0.125.57005`); a self-contained app's is unknown.
3. [x] The dump size estimate on Linux from the readable mappings, erring high like Windows'; free disk space through the
   file system's own figure (`DriveInfo` on the folder). Tests: the estimate on sample `maps` text; free space of the
   temp folder is positive on every system; the estimate is logged beside the written size so CI shows the margin.
   Done: `ProcFactsTests` (the readable mappings, free space); a Windows capture logs, for example, 146,213,206 bytes
   written against 162,721,792 estimated (90%).
4. [x] Elevation on Linux: another user's process needs HeapScope to run as root or as that user (its diagnostics socket
   is that user's); the same user's process is not elevated; root reaches all. Tests: the rule on uid pairs; the probe
   on the test process.
   Done: `UnixElevationProbe.StateOf` on effective user ids, `CapturePlatformTests` (the rule; the probe on the test
   process). The texts that say "administrator" (the Processes page, the refusal, the CLI) get Linux wording in task 5.
5. [x] Refusals with a next step on Linux: `WriteDump` failing for ptrace rules (`/proc/sys/kernel/yama/ptrace_scope`
   3, or a container without `SYS_PTRACE`), a target whose diagnostics socket is in another `TMPDIR`, and a target that
   disabled diagnostics (`DOTNET_EnableDiagnostics=0`). Wording drafted for the owner. Tests: the translation of each
   failure; the discovery log names what it skipped.
   Done (wording drafted, waiting for the owner: info-text.md, Sprint 4.1 additions): `LinuxCaptureRules` (the write
   failure from `ptrace_scope` or a container; a pid not found: another user's, diagnostics off, another `TMPDIR`, not
   .NET) and `ElevationTexts` in Core (the texts that said "administrator", per system; front ends reach them through
   Application's `RightsTexts`); the discovery log names the temp folder it looked in. Tests: `LinuxCaptureRulesTests`.
6. [x] Turn capture on for Linux: `CapturePlatform` supports Windows and Linux (macOS refuses until 4.2, its message
   updated); `PlatformHints.CanCapture` follows it, so the Capture and Live pages and the tutorial's lessons open there.
   Tests: the platform rule per system; the view models' empty texts per system.
   Done: `CapturePlatform.IsSupported` (Windows and Linux); the capture tests run on Linux too (`HeapScopeTestsRunOn`
   dropped; a test of one system skips elsewhere); tests that named the Windows texts expect each system's. Also needed:
   the DAC copied beside a dump has its Linux name (`libmscordaccore.so`), and the runtime version on Linux is the file
   version embedded in `libcoreclr.so` (as ClrMD reads it from a dump), so `dac/<version>` matches. Wording drafted
   (info-text.md, Sprint 4.1 additions).
7. [x] End to end on Linux in CI: an integration test starts the sample, finds it in the list, captures a full dump and a
   gcdump through the application layer and opens both; CI's fixture dump is taken with HeapScope's own CLI instead of
   `dotnet-dump`, and the app's `--smoke-live` runs headless on Linux. Tests: those runs.
   Done (decided autonomously: a script, not an integration test, since the Integration project targets Windows):
   `build/linux-capture-check.sh` drives the CLI (the application layer a user's command runs) through `sample start`,
   `list`, a full dump and a gcdump, and `summary` of both; it passes on Windows here, and CI's Linux job runs it with
   the fixture capture by the CLI and the live smoke run. The Linux package job captures with its own CLI too. Proven on
   Linux by the PR's first CI run.
8. [x] The tutorial on Linux: the sample starts from the Learn page and every lesson that captures runs there; this
   sprint's lesson is the Linux rules (who can capture whom, ptrace) as a short lesson or a lesson note, drafted for the
   owner. Tests: the lesson catalog on Linux; the sample launch path per system.
   Done: the lessons open on Linux with capture (task 6); a note on the Learn page, on Linux only, says who can capture
   whom (decided autonomously: a note rather than a lesson; drafted for the owner). Tests: `ShellViewModelTests` (the
   note per system); the lesson catalog and the sample launch path run on Linux in CI (`LessonCatalogTests`,
   `SampleServiceTests`), and `build/linux-capture-check.sh` starts the sample there.
9. [ ] Docs: ADR-025 amended (Linux capture), ARCHITECTURE and its capture pages, DEVELOPER.md (Linux capture, the CI
   steps), README's platform table, info text for the changed screens (owner approves), Backlog (the macOS work and what
   stays out).

**Decisions**
- Order, size and the macOS plan: the owner, 2026-11-18 (Phase 4 above).
- HeapScope never changes system settings such as `ptrace_scope` or capabilities; it says what to change. Decided
  autonomously.
- Discovery looks only where the runtime publishes (this user's `TMPDIR`); a target with another `TMPDIR` is a refusal
  with a next step, not a search. Decided autonomously.
- No new sample scenario: the existing ones run on Linux, and the lesson covers the Linux rules. Decided autonomously.
- Local Linux runs are not available on the development machine; Linux is proven in CI's Linux job. Decided
  autonomously. Once the owner's planned native Ubuntu laptop (claude-dev-3) exists, Linux checks may also run there, as a
  convenience only (owner, 2026-11-19: work must not depend on the extra laptops).

**Review summary:** to be filled by /wrap-sprint.
