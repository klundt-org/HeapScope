# HeapScope sprints: Phase 4 (what Phase 3 leaves out)

Closed sprints of Phase 4 moved here from [SPRINTS.md](../SPRINTS.md) when they closed; each keeps its goal, tasks,
decisions and review summary as they stood then. The phase's plan stays in SPRINTS.md. Reviews are in
[docs/reviews/](../reviews/).

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
   The shared framework folder's name (`10.0.1`) is only the fallback since task 6, which reads the file version embedded
   in `libcoreclr.so` (`10.0.125.57005`), as Windows gives coreclr.dll's; a self-contained app's comes from its library too.
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
   Done (wording approved by the owner 2026-11-20: info-text.md, Sprint 4.1 additions): `LinuxCaptureRules` (the write
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
   Linux by a CI run of the branch on 2026-11-20 (the first failed three tests that expected Windows wording, now fixed);
   since the review it also monitors the sample and records and reads an allocations trace (M7).
8. [x] The tutorial on Linux: the sample starts from the Learn page and every lesson that captures runs there; this
   sprint's lesson is the Linux rules (who can capture whom, ptrace) as a short lesson or a lesson note, drafted for the
   owner. Tests: the lesson catalog on Linux; the sample launch path per system.
   Done: the lessons open on Linux with capture (task 6); a note on the Learn page, on Linux only, says who can capture
   whom (decided autonomously: a note rather than a lesson; drafted for the owner). Tests: `ShellViewModelTests` (the
   note per system); the lesson catalog and the sample launch path run on Linux in CI (`LessonCatalogTests`,
   `SampleServiceTests`), and `build/linux-capture-check.sh` starts the sample there.
9. [x] Docs: ADR-025 amended (Linux capture), ARCHITECTURE and its capture pages, DEVELOPER.md (Linux capture, the CI
   steps), README's platform table, info text for the changed screens (owner approves), Backlog (the macOS work and what
   stays out).
   Done: ADR-025 amended; ARCHITECTURE.md, the code map and logging.md; DEVELOPER.md (the CI steps, the platforms);
   README's "What to expect", INSTALL.md, the CLI's README and USER-GUIDE.md (user-facing: with the owner, as the info
   texts); the Backlog's Linux item (what 4.1 left out).

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

**Review summary:** 6 High, 13 Moderate, 34 Low ([docs/reviews/sprint-4.1.md](../reviews/sprint-4.1.md)); every High fixed
and confirmed, every Moderate decided autonomously and its fix confirmed. Fixed: as root, another user's full dump is
refused for HeapScope's folders before anything is written (the runtime writes it as that user; `--out` names a folder
that user can write); a write failure is named from what createdump reported before any ptrace or container setting;
another user is told by effective user ids, a process in another mount namespace probably has its own `/tmp`, and a
single-file app is no longer called not .NET; every `dac/` folder beside a dump is searched and `libcoreclr.so` is copied
with the DAC, proven by a test that opens the fixture dump through the capture's own `dac/` folder with no runtime
installed; files a target names are read only when regular and of a sane size; each Linux decision logs what it rests on
(user ids, ptrace_scope, container, `TMPDIR`s, mount namespace, runtime), in a capture, Live and a recording; Live and
recording give the capture's Linux reasons; CI's Linux check also monitors the sample and reads an allocations trace,
with a scratch data folder, and warns when a dump is larger than its estimate; the capture tests skip where capture is not
built. Deferred: the per-system split made whole for macOS (M6, to start sprint 4.2), a full dump as root of another
user's process (staging), and the Lows. Measured: on CI's Linux runner the dump estimate was 94 and 95% written (Windows,
90%); the first Linux CI run failed 3 tests that expected Windows wording, the second passed the Linux job end to end.
The review's new wording (the own-`/tmp` and root refusal texts, the Record allocations tooltip) was approved by the
owner on 2026-11-20.

## Sprint 4.1.1: Hide processes HeapScope cannot capture

**Goal** (owner, 2026-11-20): the Processes list shows by default only the processes HeapScope can capture. Processes
that need more rights than HeapScope has are hidden, with a check box to show them; when HeapScope runs elevated (as
root on Linux) it can capture them all, so all are shown. Elevated processes stay marked as such.

**Tasks**
1. [x] The texts, per system, in Core's `ElevationTexts` (passed on by Application's `RightsTexts`): the check box's label
   and tooltip, and the note that says how many processes are hidden and why; drafts for the owner in info-text.md.
   Tests: each text on each system (one and several processes), and the info text tests.
2. [x] One rule for both lists (`ProcessListFilter` in the view models): a row that needs more rights than HeapScope has
   (`NeedsAdministrator`, false for every row when HeapScope runs elevated or as root) is hidden unless shown; it counts
   what it hides. Tests: hidden by default, shown with the box, nothing hidden when HeapScope is elevated, the count.
3. [x] The Processes page: the check box, shown only when a process needs more rights; while they are hidden the note
   replaces the banner, and the banner comes back once they are shown; a selected row that becomes hidden is unselected;
   "No running .NET processes" only when there are none at all; the log names the pids it hid. Tests: ProcessesViewModelTests.
4. [x] The Live page's process chooser: the same rule and check box. Tests: LiveViewModelTests.
5. [x] Docs: ADR-014 amended (hidden by default, marked when shown), the user guide, the code map. Done (no Backlog item was needed).

**Decisions** (each decided autonomously, with the defaults told to the owner on 2026-11-20)
- Both lists that pick a process to capture or watch hide them: the Processes page and the Live page. `heapscope-cli
  list` is unchanged (it lists everything with its mark), so scripts keep working.
- Hidden only while HeapScope lacks the rights: run as administrator (or root), it can capture them all, so all are shown
  and the check box is not.
- The check box is off at each start and kept while the app runs (not a setting), so hidden is always the default.
- A note says how many are hidden and why, in place of the banner; the Elevation column stays and marks what is shown
  ("Needs administrator" or "Needs root or its user", "Elevated" or "Another user's").
- The label names what it shows on each system: "Show elevated processes" (Windows), "Show other users' processes"
  (Linux, where those are the ones it hides).

**Review summary:** 1 High, 5 Moderate, 25 Low ([docs/reviews/sprint-4.1.1.md](../reviews/sprint-4.1.1.md)); every High and
Moderate fixed and confirmed (decided autonomously). Fixed: both pages log what they hide, through one method on the
shared rule, after a listing when it changed and at every toggle, and the Windows list line says whether HeapScope runs
elevated; a process whose rights are unknown is never hidden (tested through the real mapping); the sample's mark survives
a toggle; the Windows note names the set the banner names. Deferred: the review's Lows (among them the Live wording
"cannot capture", the info icons, the screenshot run's made-up rows).

## Sprint 4.2: Capture on macOS

**Goal:** what HeapScope captures on Windows and Linux (the process list, full dumps, gcdumps, live counters, allocation
recording, the tutorial's sample) is built for macOS, with refusals that say what to change. It is built and checked as
far as Windows and Linux allow, and marked untested: nobody has run it on a Mac.

**Starting point.** The diagnostics channel is the runtime's own on macOS too. What is missing: the facts about a process
(macOS has no `/proc`; `Process.Modules` gives the main executable only), the size estimate, another user's mark, the
rules that explain a failure, and the texts. The per-system split of 4.1 is partial (4.1 review M6, 4.1.1 review L18).

**Tasks**
1. [x] The split made whole (M6, L18): `HostSystem` in Core in place of `bool linux` in `ElevationTexts` and
   `PlatformHints`; the effective user id and the readable bytes behind `IProcessFacts`; the rules that explain a failure
   behind one interface per system (`ICaptureRules`); a system without a reader gets none instead of Windows' facts;
   `PlatformHints.CanCapture` taken from Application. No behavior changes on Windows or Linux. Tests: the facts and rules
   chosen per system, every text per system, `CanCapture` equal to the capture layer's answer.
   Done: `HostSystem` and `HostSystems` in Core, `ICaptureRules` (Windows, Linux), `RuntimeLibrary` and `TargetEnvironment`
   shared; a system without a reader has `NoProcessFacts`, and capture refuses there before any is asked for; `CanCapture`
   through `RightsTexts`. Tests: `CapturePlatformTests`, `CaptureExplanationTests`, the text tests.
2. [x] macOS facts (`MacProcessFacts`): the image path, effective user id and mapped regions through libproc, the command
   line and environment through `sysctl` (`KERN_PROCARGS2`); the runtime's folder from the mapped `libcoreclr.dylib`; the
   readable regions for the size estimate. The buffers are parsed by pure functions (`MacProcData`) over bytes. Tests: the
   parsers on built buffers (short, cut, padded, hostile counts) on every system; on macOS, the test process itself.
   Done: `MacProcessFacts`, `MacProcData`, `MacRegion`; off macOS every read is empty. Tests: `MacFactsTests`. The estimate
   takes the larger of the walk and the fallback, plus the shared cache (`DumpSizeEstimator.Choose`).
3. [x] Elevation on macOS: the rule on effective user ids of 4.1, with the ids from task 2. Tests: the rule and the probe's
   source per system.
   Done: `UnixElevationProbe` over `IProcessFacts`. Tests: `MacFactsTests`, `CapturePlatformTests`.
4. [x] Refusals on macOS (`MacCaptureRules`): createdump's `task_for_pid` failure (signing and entitlements), a folder the
   target's account cannot write, a full disk, diagnostics turned off, another `TMPDIR` (each macOS user has their own),
   another user's process; each with its evidence for the log. Wording drafted for the owner. Tests: each rule on text.
   Done: `MacCaptureRules` (drafts in info-text.md, Sprint 4.2 additions). Tests: `MacFactsTests`.
5. [x] The DAC beside a dump on macOS: `libcoreclr.dylib` copied with `libmscordaccore.dylib`, sizes allowed for. Tests: the
   copied names per system.
   Done: `CaptureService.DacFileName` and `RuntimeFileName`. Tests: `CaptureExplanationTests`.
6. [x] Capture turned on for macOS: `IsSupported`, the empty pages, tips and the Learn page, marked untested where a user
   reads it before relying on it. Drafts for the owner. Tests: the texts per system.
   Done: the texts are drafts. Tests: `MacFactsTests`, `InfoIconTests`.
7. [x] A check a Mac owner can run: `build/linux-capture-check.sh` becomes `build/capture-check.sh` (its name and messages say "capture
   check"); INSTALL.md and the issue link say how to report it. CI's Linux job keeps running it.
   Done: `build/capture-check.sh`; run on Windows here. INSTALL.md says how to run it on a Mac and report.
8. [x] Docs: ADR-025 amended, ARCHITECTURE and its pages, logging, security-privacy, DEVELOPER, README, INSTALL, USER-GUIDE,
   the CLI's README, info-text.md (drafts), Backlog (what stays unconfirmed, and the AllocationTraceTests CI failure of
   2026-11-21).
   Done: as listed; the user-facing passages are drafts for the owner.

**Decisions**
- No Mac and no macOS runners: built and checked on Linux, marked untested (owner, 2026-11-18 and 2026-10-31).
- The structs and constants come from Apple's published headers (XNU `proc_info.h`), read 2026-11-23. Every read whose size the
  system returns is checked and gives nothing on a mismatch, so a wrong layout loses a detail and never a listing. Decided
  autonomously.
- Where a source could not be read (how a region walk ends), the code assumes the least: a walk stops at the first failed
  or non-advancing read, and nothing is concluded from what it did not hold. The dump writer's messages come from the
  macOS createdump in the runtime pack (the review read them); an unknown one gets the generic text. Decided autonomously.
- Another user's process follows 4.1's rule on user ids, but the texts on macOS say to run as that user only: each user's
  temp folder differs there and sudo does not keep it (the review). No search of other users' folders. Decided
  autonomously.
- HeapScope never signs or re-signs a target or changes its entitlements; a refusal says what the runtime reported. Decided
  autonomously.
- No new sample scenario or lesson: 4.1's lesson note gains macOS wording (a draft for the owner). Decided autonomously.

**Review summary:** 8 High, 15 Moderate, 47 Low ([docs/reviews/sprint-4.2.md](../reviews/sprint-4.2.md)); every High fixed and
confirmed, every Moderate decided autonomously and its fix confirmed. Fixed: the size estimate counts the shared cache and has
a real fallback on macOS; every macOS read keeps its outcome and one self-check line shows them; a failed read is never
called an exited process; no rule calls a process not .NET; the write rules use createdump's own texts (Linux too); a huge
estimate no longer passes the free-space check; no environment value reaches the process list; macOS texts never offer sudo;
the check script runs every step, shows no other process and keeps its log; the tests ask each system's own answer; every
system logs what a dump's estimate rests on. Deferred: the Lows, and everything only a Mac can confirm (Backlog, "Testing on
macOS").

The owner approved the texts on 2026-11-23, with their own wording for the Processes and Live page texts.

## Sprint 4.2.1: Damaged gcdumps and traces

**Goal** (owner, 2026-11-24): the nightly fuzz test's failures of 2026-11-20, 22 and 23 (seeds 20, 22 and 23) are fixed, and a
damaged trace is refused as a damaged gcdump is: with a reason, within bounds, and never as a HeapScope error.

**Tasks**
1. [x] The gcdump's root index is checked before anything is loaded: in range, described and a pseudo node (PR 82, made
   stricter by the review). Tests: `GcDumpTests`, failing before the fix; 48,000 damaged files over 8 seeds.
2. [x] `TraceFileCheck` walks a trace's blocks before TraceEvent reads them and refuses sizes, field counts, nesting and types
   the format cannot hold, and any version but 4. Tests: `TraceFileCheckTests`; recorded traces walked to their end.
3. [x] An error TraceEvent throws on the file's events is a refusal (`TraceReads`); the disk, access and memory are not the
   file's; running out of memory is reported; amounts no process allocates are refused. Tests: `TraceReadsTests`,
   `AllocationTraceTests`.
4. [x] Damaged traces in the heavy tests and the nightly fuzz job. Tests: `CraftedTraceTests`; 15,000 over 6 seeds.
5. [x] The log gives every refusal's byte, rule, versions and origin. Tests: in 2 and 3.
6. [x] TraceEvent 4.0.2 (owner, 2026-11-24), compared with 4.0.0 in the review file.

**Decisions**
- A trace of another format version is refused, since nothing checks it (owner, 2026-11-24); version 6 is in the Backlog.
- The owner approved the refusal texts on 2026-11-24.
- The check's shape for a file that grows waits for 4.3 (ADR-028). Decided autonomously.

**Review summary:** 3 High, 14 Moderate, 22 Low ([docs/reviews/sprint-4.2.1.md](../reviews/sprint-4.2.1.md)); every High and
Moderate fixed or decided, and confirmed. Deferred: the Lows; version 6 and one file handle (Backlog).
