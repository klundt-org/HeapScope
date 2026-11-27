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
3. [x] An error TraceEvent throws on the file's events is the file's (`TraceReads`): a refusal, or while converting the trace's
   end, with a note; the disk, access and memory are not the
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

**Review summary:** 6 High and 20 Moderate, and in the confirmations 3 High and 5 Moderate
([docs/reviews/sprint-4.3.md](../reviews/sprint-4.3.md)); every one fixed and confirmed, but for the names of a process that exits
without a rundown (M18, Backlog). Deferred: the Lows listed there, and a stall of the live smoke run's window (Backlog).

## Sprint 4.4: Allocated between two snapshots

**Goal:** comparing two snapshots of one monitoring run that recorded its allocations, Compare says what was allocated between them, by
type, and one step leads to where.

**Starting point.** A comparison says what is there now and was not (New, matched by address, so a moved object counts too); it says
nothing of what was allocated and is gone again, or where. A run that records allocations has both snapshots and the trace in its
session (`SessionRun`: `SnapshotIds`, `Trace`), each with a time; ADR-028 left out joining them, since it needs one clock and one
name for a type.

**Tasks**
1. [x] Measured before building: how a capture's recorded time lies to the trace's (the target is paused while a dump is written), and
   how much of the sampled bytes join a diff row by name, for full dumps and gcdumps (generics, arrays, nested types, names in two
   modules). If names do not join for most of the bytes, stop and report. Proof: the numbers in Findings and ADR-028's amendment.
2. [x] Which trace lies between two snapshots: the run of one session that holds both and a trace, the interval, and a reason when
   there is none (another process, another run, no trace, a trace that ended before). Tests: each reason; a received session's
   paths.
3. [x] One name for a type: the trace's names brought to the snapshots' form in one place, tested against real dumps. Tests: table and
   integration.
4. [x] The worker: a diff with a trace and an interval gets Allocated columns (estimates), with the sampled bytes that no row took;
   one protocol bump. Tests: contract, worker, a real run.
5. [x] Compare: a check box like New and dead objects, the note that says what the figures are, and a step from a row to the
   Allocations page with the interval and type chosen. Wording drafted for the owner. Tests: view models, views.
6. [x] The command line: `diff --allocated`, with the trace found in the session (decision below). Tests: CLI.
7. [x] The sample's scenario and lesson (ADR-042), or why not; drafts for the owner.
8. [x] Docs: ADR-028 amended, ARCHITECTURE pages, DEVELOPER, USER-GUIDE, info-text.md, the CLI's README, Backlog.

**Decisions**
- Only a trace of the same monitoring run is linked. A trace recorded on the Processes page between two captures is not looked for: its
  session does not say what it belongs to. Decided autonomously.
- Figures are estimates and marked so, as everywhere; rows without a sample say nothing rather than zero bytes. Decided autonomously.
- Task 1 showed that the tick's type id is the type's method table, so full dumps are joined by it, not by name. Only a gcdump is joined by
  name; a name that types of several modules share goes to no row, and the note says how many bytes that is. Decided autonomously.
- The run is found by its process and time, not by its list of captures, so a capture from the Processes page during the run counts too.
  Decided autonomously.
- When a capture is asked for what was allocated since, its heap's time counts, not its file's: `SessionSnapshot.HeapUtc`. Decided
  autonomously.
- Types without an object in either snapshot have no row; the note says their bytes, and the Allocations page has them. Rows for them are in
  the Backlog. Decided autonomously.
- `diff --allocated` takes the session's trace only; a trace named by the user has no heap times to go with it. Decided autonomously.
- Task 7: a scenario of its own, Churn, since the bursts keep nothing a comparison could have a row for. Decided autonomously.

**Findings** (task 1, .NET 10.0, Windows, LeakyApi under LoadGen and DenseHeap compiling with Roslyn, 2026-11-25)
- Time. The recorded time of a full dump lay 3 to 160 ms after the target's pause (up to 220 MB at the rate DenseHeap allocates), that
  of a gcdump 3 to 6 s after (170 MB of LeakyApi's, up to 1,860 MB of DenseHeap's). A dump's own time has whole seconds only. Now: a full
  dump's last write and a gcdump's first heap node, within the pause or at most 3 ms after it (0 to 1.9 MB).
- Names. Of the sampled bytes between two full dumps, 61 to 83% join by name and 74 to 100% by the tick's type id; the rest are types
  without an object in either dump (74%: both dumps just after a collection, of a compiler's temporaries). The runtime's events name a
  nested type by its innermost name, as a gcdump does: between gcdumps 80 to 95% join by name, and the rest are types that left nothing
  behind (Activity, log scopes, compiler temporaries). Names several modules share: 0 to 0.4%; names the trace has for several types,
  which the review left without a row: 1.1%.
- Every gcdump but the first that was captured while a trace recorded had types without a name (it said that it was incomplete). The
  runtime describes a type once while a session has its type keyword, and forgets only when the keyword is turned off, which the flush
  before a gcdump did not do beside another session. So since 3.4, when runs could record allocations, the second gcdump of such a run
  was of no use. Fixed in the vendored flush, for HeapScope's three kinds of recording; another tool's session that has the type
  keyword still does it (Backlog). Test: `Each_gcdump_beside_a_recording_names_its_types`, failing without the fix for all three.

**Review summary:** 5 High and 14 Moderate, and in the confirmations 7 Moderate ([docs/reviews/sprint-4.4.md](../reviews/sprint-4.4.md));
every one fixed and confirmed. Deferred (Backlog): rows for types without an object in either snapshot, the Allocations page filtered by
method table, the flush before a gcdump checked on .NET 8 and 9.

## Sprint 4.5: Peer review

**Goal:** what must change, be refactored or be proven before the first full release and the UI-heavy sprints, from reviewers who read
the whole code base as outsiders, ranked, each finding checked in the code.

**Tasks**
1. [x] Five reviewers read the code base at sprint 4.4 (not a diff), each its area: solution architect, senior developer, CLR and GC
   specialist, support engineer, and DevSecOps (build, packages, CI, supply chain, threat surface). Each also says what stands between
   this and a first full release.
2. [x] The findings merged, the Critical and High ones checked in the code by the lead engineer ("verified"), ranked, in
   `docs/reviews/peer-review-4.5.md`.
3. [x] Anything Critical (wrong results, data loss, security) fixed at once as a patch, with the owner told.
4. [x] A proposed split into sprints, for the owner to decide (agreed, 2026-11-25); the Backlog updated.
5. [x] The owner's 4.4 wording and release notes answers folded in, if they came.

**Decisions**
- Like 3.5's: the review changes no code but for Critical findings. Decided autonomously.
- The split into sprints is the owner's decision, so the sprint ends by asking. Decided autonomously.
- The fact is positive (`HandlesChecked`) and the older cache's literal version is gone: decided before loader version 5 shipped, since after it the
  change would have cost a version 6. Decided autonomously.
- Compare's retained columns with the fact on one side only go to 4.6, since the result needs a new field. Decided autonomously.

**Findings** (.NET 10.0, 24 processors, 2026-11-25): strong handles the runtime's own walk has of 2,400: 103 at 1 heap, 192 to 206 at 2,
785 at 8, 1,200 at 12 (DATAS); HeapScope's second walk has all, in 6 to 14 walks and 0.1 s, and 1.9 million handles in 7.1 s.

**Review summary:** 1 Critical and 1 High fixed (GC handles of Server GC processes; a program by its bare name), 20 High and 16 Moderate ranked
([docs/reviews/peer-review-4.5.md](../reviews/peer-review-4.5.md)) and split into sprints 4.6 to 4.12. The fixes were reviewed by four: every High
and Moderate fixed and confirmed, one Moderate deferred to 4.6.

## Sprint 4.6: Right answers

**Goal:** every answer HeapScope shows is about the snapshot it names, says when it did not check, and nothing hangs or crashes on the
way there: the High and Moderate findings of [peer review 4.5](../reviews/peer-review-4.5.md) planned here, with what its fixes left.

**Tasks**
1. [x] H4: a grid whose subject changes (another snapshot, another pair, a snapshot closed) shows nothing of the old one: rows, totals,
   heap bar and notes cleared first, also when the new query fails; Trend falls back to its default sort for a column gone; Compare and
   Trend show the busy bar. Tests: a query of B that fails shows nothing of A, in Types, Compare and Trend (notes included); Trend
   unticked to fewer snapshots sorts by the change.
2. [x] H9: stopping an EventPipe session waits a bounded time, then lets it go, in one helper (`SessionStop`): 5 s for the forced
   collection and the counters, 30 s for a recording, whose stop writes its rundown; a recording's and the counters' start wait 30 s;
   closing the app waits 15 s for its host, logs what it waited for and always shuts down. Tests: the helper with a stop that never
   answers; a recording of a suspended process ends. Deferred (Backlog): a forced collection of a suspended process.
3. [x] H10: the file and process work at a job's start runs on the thread pool, never on the caller's (UI) thread: the trusted DACs an open reads, the session a recording takes, starting a worker (all jobs on the pool broke the synchronous start the views rely on). Deferred (Backlog): a test. Was: work that
   records its thread is never run on the caller's.
4. [x] H11: a run's allocation trace checks the free space of its own folder's disk (DumpCapturer's probe) on every system, goes on
   after a failed check, and stops recording with a message after several in a row. Tests: the probe on a folder, failures counted.
5. [x] H2: a detector that could not read objects says so: the result carries its counts (scanned, unread), the front ends show "not
   checked" for what was unread, and a detector that could read none of what it needs is a refusal. Tests per detector with fields
   missing; the CLI's and the app's text.
6. [x] H3: the static storage bucket keeps its "static storage" detail always; statics of closed generic types and boxed struct statics
   are named where ClrMD can read them; the growing static collections detector notes storage slots no static field names. Tests on a
   fixture with `Cache<T>` statics and a struct static.
7. [x] H16: the CLI has a boundary around its start: a mistake in `appsettings.json` is said with the file, the line and how to write a
   backslash in JSON, and logged; unhandled exceptions are logged. INSTALL gets a Windows example. Tests: a damaged and a mistyped file.
8. [x] H19: a failed nightly fuzz run opens an issue, or comments on the open one, naming its seed. Test: the workflow step, by a dry run.
9. [x] M6: one private JSON file helper (a lock, shared reads with retries, a flushed temp file, a replace with retries, unreadable is not
   empty) used by lesson progress, bookmarks and settings. Tests: a held file loses no progress; two writers lose no update.
10. [x] M7: Inspections has Try again (for a failure, not a refusal; the chosen baseline kept); the Retainers tree and an object's tabs
    that failed run again when shown again; Compare and Trend query only while shown and catch up when shown. Tests: Inspections, hidden
    Compare. Deferred (Backlog): Retainers and Object retry tests, a hidden Trend test, a Try again in their status.
11. [x] M8: a worker crash racing a restart is still a crash (WorkerCrashedException); SampleService raises its events safely and a kill its
    timeout fires is logged, not thrown; a recording from the Processes page marks its session in use. Test: an architecture rule against raw
    event raises in Application. Deferred (Backlog): an open trace keeping its session, and tests of the crash race, a throwing listener and a
    recording's session in use.
12. [x] M9: "no root found" in place of "unreachable" stated as fact where stack roots of a running process can be incomplete, and the
    unreachable set's generation split. Wording drafts for the owner. Tests: the texts, the split.
13. [x] What 4.5's fixes left: retained sizes (Compare's two sides, the Types grid, the CLI) say when a snapshot's GC handles were not all
    checked. Test: `DominatorIndexBuilderTests`. A cache loaded again by itself once the second walk is on was built and taken out after the
    review: the dump that needed the walk off would end the worker on every open. `heapscope-cli open --rebuild` is the step, as the note says.
14. [x] The tests that failed in CI now and then: the run's trace that had no bytes for 60 s (a start that hangs, near task 2), the
    bookmarks status of a damaged file on Linux, the live allocations follower (fixed 2026-11-25, watched). Each fixed or explained.

**Decisions**
- All of the findings the owner put in 4.6 (2026-11-25), in this order: what shows wrong answers first, then what hangs, then the rest.
  Decided autonomously.
- New user-facing texts are drafts in `docs/design/info-text.md` ("Sprint 4.6 additions") until the owner approves them; the sprint is built
  with them. Decided autonomously.
- No protocol change after all: task 5's counts and task 13's caution reach clients in the notes that exist (process-model.md). Decided
  autonomously.
- Settings for the DAC on the Settings page (H15) stay in 4.8 with the rest of "Ready to release"; task 7 only says the file and line.
  Decided autonomously.

**Findings** (2026-11-25)
- Statics: on .NET 10 a struct static with references is boxed in the runtime's storage; HeapScope now roots the box by its field. Statics
  of closed generic types stay in that storage with no field to name them (ClrMD lists none), so the growing static collections detector
  says how many of their collections it could not check.
- The known answers' own walk (ClrMD's) lacked the GC handles 4.5's second walk finds in LeakyApi, a Server GC process: the unreachable set
  disagreed by up to 100 objects, wrongly on the test's side. Its walk now adds them.
- A recording's stop writes its rundown, which took more than 5 s in the gate; recordings wait 30 s.
- A recording's start took more than 60 s once under the gate's load (CI saw it four times): not explained; bounded at 30 s with a message,
  and the tests say what they saw.

**Review summary:** 6 High and 19 Moderate, and in the confirmations 4 Moderate and some Low
([docs/reviews/sprint-4.6.md](../reviews/sprint-4.6.md)); every High fixed and confirmed, 14 Moderate fixed and 5 deferred (Backlog), the
confirmations' fixed. Gate: pass, 0 warnings, 2,207 tests.

---

## Sprint 4.7: Runtimes, Linux and large heaps

**Goal:** what README promises is what was tested: dumps of .NET 8, 9 and 10 give the known answers, the integration tier runs on
Linux and the app's window is shown there, and a large heap opens within the memory HeapScope says it has, on every system: the
findings H1, H5 to H8, H12 and M11 of [peer review 4.5](../reviews/peer-review-4.5.md), with what 4.6 left for them.

**Tasks**
1. [x] Tests leave nothing in the temp folder: one helper for a test's folder (`TestFolders`: deleted with retries, one that stays is
   said on the error stream, never swallowed), used by the integration tier's dump folders; every test assembly's start sweeps the dump
   folders and the tests' `heapscope-*` folders a run left more than a day ago (the small shared data and sessions folders stay).
   Tests: `TestFoldersTests` (a held file said, a stale folder swept, a fresh one or one named by hand kept).
2. [x] H1: the fixtures (Leaks, LeakyConsole, LeakyApi) build for net8.0, net9.0 and net10.0; the tests pick a fixture's runtime by
   `HEAPSCOPE_FIXTURE_RUNTIME` (net10.0 when unset); the known answers (`KnownAnswerTests`, `LoaderKnownAnswerTests`,
   `DetectorTests`, `check-known-answers.sh`) pass on each, with any answer that differs by runtime stated in its file. CI runs them
   for .NET 8 and 9 on Windows and Linux (the classes on Linux since the review). Tests: the known answer classes per runtime; a runtime's own answer where it differs.
   Found: .NET 8 reports its static storage only as strong handles, so a static ClrMD cannot name (a generic type's) blamed a
   handle; the loader now names a handle's array that holds a static's slot as the storage (loader version 7). And a heap dump of
   .NET 8 leaves out the memory its statics are in: the loader says so (`StaticsChecked` missing, when none of CoreLib's statics read; loader version 8),
   with a note, the unreachable set refused and the detectors noted as for unchecked handles. Tests: `HeapDumpStaticsTests`, the
   statics case in `UncheckedHandlesTests`; the tests take full dumps of .NET 8. No answer differs by runtime.
3. [x] H1: opening a dump of a runtime HeapScope was not tested on (before .NET 8) says so beside the result, in the app and the CLI, and
   the log names it; README, ADR-002 and INSTALL say which runtimes are tested (8, 9 and 10) and which open untested (5 to 7). Tests: the
   note for a .NET 6 version, one before .NET 5, none for 8 to 10 (`TestedRuntimesTests`); the CLI prints it as every note.
4. [x] M11: thread statics have a fixture (`[ThreadStatic]` and `ThreadLocal<T>` holding leak objects, on a parked thread) and a known
   answer; the loader names a thread static by its field, "(thread static)", once per thread that set it (loader version 7): .NET 8
   reports no thread static root (its arrays were blamed on a strong handle), .NET 9 and 10 only the runtime's arrays. A ThreadLocal's
   values are reached through its own static field. Tests: `A_thread_static_is_named_by_its_field_and_a_thread_local_is_reached_through_its_static`
   on each runtime.
5. [x] H12: the integration tier runs on Linux: the project builds for net10.0, the paths of the fixtures, the CLI and the worker are
   the system's own, and the Windows-only tests (minidumps, elevation, `cmd.exe`) skip elsewhere and say why; CI runs the light
   integration tier on Linux. Tests: the tier itself, green in CI on Linux.
6. [x] H12: the app's window is shown on Linux in CI under a virtual display (`xvfb-run`): it opens the fixture dump and drills down as the
   headless smoke run does. A real desktop (claude-dev-3) stays in the Backlog. Tests: the CI step.
7. [x] H7: the memory budget reads the system's own numbers off Windows: `/proc/meminfo` (MemAvailable) and a cgroup's limit and use on
   Linux, `sysctl` and the VM statistics on macOS (untested); the worker's own memory on macOS is its physical footprint. One reader for the
   worker and the front ends. Tests: the parsers on sample texts; a cgroup limit below the machine's; on Linux CI, nonzero numbers before
   any collection.
8. [x] H6: an inspection runs inside a memory reservation sized by its own estimate (`IInspection.WorkingBytes`: a byte per object by
   default, Duplicate strings 49 bytes per string and its rows), refused with the reservations' next step when it cannot fit; Duplicate
   strings lists the 10,000 values that waste the most, its totals count every duplicate and the note says how many more there were
   (version 3). Tests: the bound, its note and its estimate (`Duplicate_strings_list_the_values_that_waste_the_most_and_count_the_rest`);
   a refusal is the reservations' own (their tests).
9. [x] H8: a recording's default limit is one whose read fits beside two snapshots on the 16 GB baseline (512 MB), and a recording's
   limit is checked against the machine at its start, not only at its read: lowered and logged (said in the stop's text: Backlog). The
   one streaming reader stays in the Backlog. Tests: the default; a limit over the machine lowered (`TraceReadCostTests`).
10. [x] H5: the worker checks a first load of a full dump: it reserves the load's estimate, less what it holds already, while it loads,
    and logs the estimate against what was used and how the load ended. One that does not fit is logged, not refused: the worker is not
    told whether the user accepted the front ends' warning (heapscope-cli open --no-overcommit refuses before asking); a refusal in the
    worker needs a protocol change (Backlog). An 8 GB DenseHeap dump is measured (first load, peak, reopen in both modes, the indices;
    and with the working set capped): 5,279 MB against 4 GB, so the references go to a mapped temporary file as they are read
    (not into the cache: their count is not known until the pass ends; ADR-015 amended), falling back to memory, logged, when
    space is short or the file fails, and freezing collects its chunks. Tests: `A_first_load_is_reserved_and_logged_against_its_estimate`,
    `EdgeStoreTests` (the same graph; the file released on every path; each fallback),
    `A_load_that_writes_its_references_to_a_file_builds_the_same_graph`; the measurements in budgets.md (2 GB: 1,920 to 1,161 MB;
    8 GB: 5,279 to 3,569 MB, under 4 GB, the same graph). `FirstLoadEstimate` refitted to 0.45 bytes per dump byte plus 704 MB
    (`Estimate_covers_the_measured_first_load_peaks`).
11. [x] M11: the reverse index built without a cache takes a reservation; a capture's disk check counts the index caches the dump will
    need (by the graph's bytes per object); counters read the interval the target reports and log one that differs from the one asked.
    Tests: each.
12. [x] The trace start that takes over 60 s under load (CI since 2026-11-20, the 4.6 gate). Found: not a slow start, nor the names
    session (CI's run of this sprint saw it on a LeakyApi with no session before). `TraceRecorder.RecordAsync` wrote through a 64 KB
    buffer, so an idle target's trace stayed 0 bytes on disk, and the tests wait for bytes before they start their load. Fixed: written
    unbuffered, as a live recording is. Test: `An_idle_targets_recording_has_bytes_on_disk_within_seconds` (1.8 s with the fix; none in
    30 s without). A live recording, already unbuffered, was seen late once (`AllocatedBetweenTests`, 4.6): not explained by this.

**Decisions**
- Owner, 2026-11-25: .NET 8 and 9 runtimes may be downloaded (this machine has them already; CI gets them with setup-dotnet).
- Owner, 2026-11-25: about 20 GB in `artifacts/` for the 8 GB dump is fine, and the old fixture dumps there may be deleted (D: has
  1 TB free, so they are kept unless the space is needed).
- Owner, 2026-11-25: Linux runs the integration tier headless in CI now, and the app's window under a virtual display; a real desktop
  later (claude-dev-3).
- Decided autonomously: .NET 5 to 7 are opened, marked untested, not refused; ADR-002's decision stands, amended with what is tested.
- Decided autonomously: the worker protocol and the loader version are bumped only if a task needs it (task 4 does: loader 7).
- Decided autonomously: the 1 GB trace limit becomes 512 MB; the streaming reader for a finished trace is Backlog (L).
- Decided autonomously: the test temp cleanup (task 1) joins the sprint: a killed run left 17 GB in the temp folder (owner, 2026-11-25).
- Decided autonomously: the edges written straight into the cache only if the 8 GB measurement misses the 4 GB first load budget.
  It missed (5,279 MB); owner, 2026-11-26: fix it in 4.7. Built as their own mapped temporary file instead of the cache
  (ADR-015 amended); the 8 GB dump then peaked at 3,569 MB, and the first load estimate was refitted to it.

**Review summary:** 7 High and about 14 Moderate, and in the confirmations 4 Moderate; then the task 10 follow-up (the references'
temporary file), 2 High and 9 Moderate, and in its confirmations 3 Moderate ([docs/reviews/sprint-4.7.md](../reviews/sprint-4.7.md)).
Every High fixed and confirmed; the Moderates fixed, or deferred to the Backlog where said. Measured: an 8 GB DenseHeap dump's first
load peak 5,279 MB down to 3,569 MB (under the 4 GB budget, the same graph), a 2 GB one's 1,920 MB down to 1,161 MB;
`FirstLoadEstimate` refitted to 0.45 bytes per dump byte plus 704 MB. Gate: pass, 0 warnings, 2,267 tests.

## Sprint 4.8: Ready to release

**Goal:** someone outside can install HeapScope, get help, recover from the common dead ends (a missing DAC, sudo on Linux, a stuck
worker), trust what a release contains and script its CLI output, so that a trial by people outside can start; the first full release
follows it when the owner says. Peer review 4.5: H14, H15, H17, M1 to M5, M13, M14; from the Backlog: the memory the drop before the
mapped switch does not return, a note when a load's references fall back to memory, `ColumnBytes` split.

**Tasks**
1. [x] H14: one place to get help. GitHub Issues on the repository, with issue templates (a bug, a wrong result, a question) and a
   `SUPPORT.md`; a "Getting help" section in README and INSTALL; every message that says to contact support names that page and the
   error's id, and says what the log holds and how to check it before attaching it (CONTRIBUTING and INSTALL agree). Wording drafts for
   the owner. Tests: an architecture test that no user-facing text says "support" without the page; the templates' fields.
2. [x] H15: a missing DAC is not a dead end. The app's Settings page gets the DAC folder (picked or typed) and the opt-in Microsoft
   symbol server, saved where the worker reads them; the missing-DAC message names that page in the app and the options in the CLI;
   INSTALL says where the DAC is found and how to copy one. Tests: the settings round trip, the worker reading them, the message per front end.
3. [x] H17: sudo on Linux works as written. The advice names the full path (`secure_path` drops the user's `PATH`), and HeapScope run
   under sudo keeps the invoking user's data folder and gives the files it writes (dumps, sessions) to that user (`SUDO_UID`), so a
   dump of a service is readable afterwards. Tests: the texts; the owner change on Linux CI (as root in the container).
4. [x] M1: a release is built and run before it is public. The workflow creates a draft, builds every package, runs each archive it
   can run (Windows and Linux: `--version`, open a fixture dump), refuses to replace an existing tag's assets, attests only the
   assets, then publishes. Tag protection and immutable releases are repository settings (owner, listed). Tests: the workflow's dry run
   on a test tag in a fork-free way (workflow_dispatch with publish off).
5. [x] M2: THIRD-PARTY-NOTICES lists the versions shipped, and a test compares it with the lock files. Test: `Notices_list_the_shipped_versions`.
6. [x] M3: opening an untrusted dump runs the dump's runtime's DAC, native code, with the user's rights: security-privacy.md and INSTALL
   say so (draft), and opening a dump while elevated adds a note. Sandboxing the reader stays in the Backlog (L). Tests: the note.
7. [x] M4: CLI output is a versioned contract: every `--json` output carries `"format": 1`, docs/cli-output.md says what may change
   within a format and what bumps it. Tests: each command's JSON has the field; a contract test per command's top-level shape.
8. [x] M5: the worker. A crash banner stays until the worker is back or dismissed; "Restart worker" for a stuck one (also the CLI's
   timeout message names it). One process for live and snapshots stays (Backlog, with its reason). Tests: the banner's life; restart.
9. [x] M13: the support paths, each small: the app's crash path writes a report it offers; Copy details carries version, system, id;
   a log that cannot be written is said once; every failure shown has its log line; the worker's first start says what it is doing
   and is measured; Purge does what its text promises; README's path to a first answer; INSTALL's Linux steps checked on a clean
   container. Tests: per item.
10. [x] M14: CI and the project's single points: the required check confirmed, the known flaky tests (CounterMonitor, the gcdump cancel,
    the load cancellation, the churn recording start) made deterministic or bounded, a .NET servicing policy in DEVELOPER.md, and the
    token and org's single points written down with what the owner holds. Tests: the flaky tests 20 times in a loop without a failure.
11. [x] From the Backlog: the memory the drop before the mapped switch does not return (find what holds it; `load-dump` back near
    3.12's 34 MB), a snapshot note when a load's references fell back to memory (draft wording), `ColumnBytes` split into private and
    file-backed for the memory mode's log. Tests: `load-dump`'s private bytes after the drop; the note; the split.
12. [x] The trial: docs/trial.md, what to try, how to report, and what is known not to work; the release checklist for the first full
    release in DEVELOPER.md 7. The trial itself and the release are the owner's.

**Decisions**
- Owner, 2026-11-26 (asked: it changes ADR-017's 1.2 amendment): the symbol server opt-in is per user, on the Settings page and
  `heapscope-cli settings --symbol-server on --yes`, off by default and on only after the user confirms; ADR-017 amended. The DAC
  folder is per user too (`--saved-dac-folder` on the command line, since the run's `--dac-folder` is taken); the command line and
  appsettings win over both.
- Decided autonomously (owner to confirm): the help channel is GitHub Issues on klundt-org/HeapScope, with templates, not
  Discussions or email; a log may be attached after the user checks it (it holds paths and type names, filter text only when the
  user turned it on).
- Decided autonomously (owner to confirm): no macOS runners stays (owner's rule) although the reviewer notes they are free for public
  repositories; the macOS packages stay untested.
- Decided autonomously: repository settings (tag protection, immutable releases, required checks) are the owner's; the sprint
  drafts them in DEVELOPER.md 7 and the owner applies them.
- Decided autonomously: the JSON output's version is a top-level `"format": 1`, additive only within a format. Built as `"Format": 1`,
  the first member, in PascalCase like every other member; a list (`inspect` without a name) became the object's `Inspections`.
- Decided autonomously: under sudo, HeapScope gives what it writes to `SUDO_UID` and keeps that user's data folder.
- Decided autonomously: one worker process for live and snapshots stays; a second process is Backlog.
- Decided autonomously in the review (owner to confirm): a whole log, when the lines in an issue are not enough, is shared by email to the
  owner's address, never posted (SUPPORT.md).
- Decided autonomously: user-facing texts are built as drafts (docs/design/info-text.md "Sprint 4.8 additions") for the owner.

**Review summary:** 6 High and 18 Moderate ([reviews/sprint-4.8.md](../reviews/sprint-4.8.md)). The Highs, all fixed: under sudo the root
worker no longer takes what decides which DAC it loads from the user's folder (the saved DAC folder and opt-in are ignored, the trust
list and DAC copies are root's, and nothing in root's home is given away, the reviewers' re-check); a DAC folder the search leaves out is
refused or named; Restart worker is logged and what it ends says so; the help pages say issues are public; HelpLinks moved to
Contracts with a source check; a release runs on its tag and builds one resolved commit. Moderates: 12 fixed, 6 fixed in part or
deferred to the Backlog (the error id as protocol data, the JSON rows' keys, PrivateFiles.Copy, a failed Give logged, a DAC-only
symbol locator, the capture refusal's sudo advice). Measured: the worker after the mapped switch on a 2 GB dump 50 MB private and
8 MB of GC heap (743 and 698 MB before); the flaky loop's 20 runs (BACKLOG.md); the release workflow's dry run and CI on the branch.
 Gate at close: pass, 0 warnings, 2,327 tests
(the known answers run in CI).

---

## Sprint 4.9: Workspaces

**Goal:** each open snapshot gets a workspace of its own (its types grid, analysis path, inspections and retainers), so switching
snapshots keeps every view as it was and links land in their snapshot's workspace, with the worker's result sets kept for the
workspace shown; and the code is ready for analysis tabs (4.10): classes kept small, comments that explain without sprint ids, and
view model tests on a real UI thread. Peer review 4.5: H13 (analysis tabs do not fit the page model: one set of views re-pointed,
paths per snapshot, links without a workspace, 32 result sets) and M15 (classes grow back, sprint and review ids in about 1,600
comments, tests without a UI thread).

**Tasks**
1. [x] A workspace per open snapshot: `SnapshotWorkspaceViewModel` owns its types grid, analysis path (navigator), inspections and
   retainers; the Snapshots page keeps one per open snapshot and shows the current one; closing a snapshot disposes its workspace.
   Views bind to the current workspace. Compare stays on the page. Tests: switching between two snapshots keeps each one's filter, sort,
   path, inspection results and retainers as they were, without a new query; closing one drops its workspace.
2. [x] Links land in their workspace: a `NavigationTarget` names its snapshot, and following one shows that snapshot's workspace
   first (Compare, bookmarks, Retainers links, the Live page's files). Tests: a link into a snapshot not shown shows its workspace and
   step; a link of a closed snapshot is refused with why.
3. [x] The worker's result sets follow the workspace shown: the client tells the worker which snapshot is shown, and the result set
   store evicts the sets of snapshots not shown first; a workspace shown again re-runs only what was evicted. Protocol version bump.
   Tests: three snapshots open, the shown one's sets survive the others' queries; the eviction order; the re-run.
4. [x] M15, classes: the view models over 600 lines are split along their parts (CompareViewModel, LiveAllocationsViewModel,
   InspectionsViewModel, SnapshotsViewModel once the workspace is out), and an architecture test caps a view model file at 600 lines,
   with a named, shrinking list of exceptions. Tests: the cap; the split classes' existing tests unchanged.
5. [x] M15, comments: source comments say why without sprint or review ids (a decision keeps its ADR number); an architecture test
   refuses new ones in src. Tests: the rule, with its patterns.
6. [x] M15, the UI thread: a test dispatcher that runs on a thread of its own, and checks that a view model's state changes there;
   the workspace and page tests use it. Tests: a property set off that thread fails the test; the workspace tests pass on it.
7. [x] Docs: an ADR for workspaces (amending ADR-029's shared types grid), ARCHITECTURE.md's page model, process-model.md's result set
   store, code-map.md, DEVELOPER.md's view model and comment conventions.

**Decisions**
- Decided autonomously: a workspace per open snapshot now; several workspaces of one snapshot (analysis tabs) are 4.10's, on this model.
- Decided autonomously: Compare stays on the Snapshots page, across workspaces; it takes its two snapshots as before.
- Decided autonomously: the worker is told which snapshot is shown (a protocol call) and evicts other snapshots' result sets first,
  rather than the client releasing them eagerly: a workspace shown again keeps what still fits.
- Decided autonomously: comment ids go from src only; tests and docs keep theirs (their records); an ADR number stays in a comment.
- Decided autonomously: 600 lines per view model file, with a list of named exceptions that only shrinks.
- Decided autonomously: page and workspace tests move to the single-thread dispatcher; other view model tests keep the immediate one.

**Review summary:** 1 High and 15 Moderate ([reviews/sprint-4.9.md](../reviews/sprint-4.9.md)). The High, fixed: logging.md now
describes the sprint's lines. Moderates: 12 fixed (a hidden workspace defers its growth reruns, a switch stops only a rerun in
flight even while it ends, a chosen None baseline survives, the worker keeps 128 sets and drops a closed snapshot's, the eviction and tell lines at
Information, the workspace lines say new or kept and what may still run, more workspace tests, a robust test UI thread watching the page's subjects, the
comment rule over views, project files and wrapped ids, and the sweep's damaged comments repaired); 3 deferred, all to 4.10 or
before it (measuring three Medium snapshots open, sets tagged by workspace, links routed by workspace). 18 Lows listed. Measured:
about 1,950 comment lines in src and about 80 XML comments lost their ids; the four view models are 496 to 549 lines. Gate at
close: pass, 0 warnings, 2,371 tests (the known answers run in CI).

---

## Sprint 4.10: Analysis tabs

**Goal:** several analyses of one snapshot side by side, each in a tab with its own types grid, path and retainers, so a person can
follow two leads in one dump without losing either; links land in the tab they came from, and the worker keeps the shown tab's
results first. Inputs: the 4.9 review's deferred M4, M6 and M7 (ADR-044), and its Lows L2 and L6, which touch the same code.

1. [x] Measure first (4.9 review M6): a smoke run that opens three dumps (`--smoke-open` and `--smoke-more a;b`), walks a path and runs
   the inspections on each, and logs the UI process's working set, private bytes and managed heap after each; run on the three
   Medium dumps (or what of them opens here) and recorded in budgets.md. Tests: the options take several dumps; the smoke run's
   measure line.
2. [x] Tabs of a snapshot: a snapshot's analysis holds its tabs (each a `SnapshotWorkspaceViewModel`: types grid, path, retainers)
   and one inspections view shared by them (one run per snapshot); the page shows the selected snapshot's current tab. New tab
   starts at the types grid; closing a tab disposes it and its sets; the last tab of a snapshot cannot be closed; at most 8 tabs
   per snapshot, a ninth refused with why. Tests: a second tab keeps its own filter and path; the inspections run once for two
   tabs; closing a tab releases its sets; the cap.
3. [x] Links land in their tab (4.9 review M7, L2, L6): a tab is the navigator of its own views, so a link of its snapshot lands in
   the tab it came from, and one into another snapshot goes to the page, which shows that snapshot's current tab; the navigator
   keeps one path (its snapshot's); a Retainers link into a tab not yet shown navigates before the tab queries. Tests: a link in
   the second tab stays there; a cross-snapshot link; the Retainers order.
4. [x] The worker keeps the shown tab's sets first (4.9 review M4): the client says which snapshot and which tab are shown
   (`ShowSnapshotAsync(snapshot, scope)`, protocol 26); new sets of the shown snapshot carry the shown tab; eviction goes other
   snapshots first, then the shown snapshot's other tabs, then the rest. Tests: the eviction order with two tabs; the scope over the
   pipe; the page tells the scope on a tab switch.
5. [x] The tab strip: on the Snapshots page above the tabs, the selected snapshot's tabs with their titles (the current step's),
   New tab and Close tab, and Ctrl+T and Ctrl+W (Cmd on macOS) in the shortcut table; texts as drafts. Tests: the view binds the
   tabs and commands (UI tests), the shortcuts list, the page and workspace tests on the UI thread.
6. [x] Docs: ADR-045 for tabs (amending ADR-044), ARCHITECTURE.md 3.2, process-model.md (protocol 26, the store), code-map.md,
   logging.md (the tab lines), the user guide (a draft), info-text.md (drafts).

**Decisions**
- Decided autonomously: a tab holds its own types grid, path and retainers; the inspections are per snapshot, shared by its tabs, so
  a second tab does not run them again.
- Decided autonomously: a new tab starts at the types grid; "open in a new tab" from a row is left for later (Backlog).
- Decided autonomously: at most 8 tabs per snapshot; the last tab of a snapshot cannot be closed (closing the snapshot closes them).
- Decided autonomously: the worker learns the shown tab with the shown snapshot (one call, protocol 26) and tags new sets of that
  snapshot with it, since only the shown tab queries (ADR-044); sets of no snapshot and Compare's stay untagged.
- Decided autonomously: Ctrl+T and Ctrl+W (Cmd+T and Cmd+W on macOS) for New tab and Close tab; tabs are not kept across restarts.
- Decided autonomously: the 4.9 Lows L2 and L6 come in with task 3; the other Lows stay in the review file.

**Review summary:** 2 High and 9 Moderate ([reviews/sprint-4.10.md](../reviews/sprint-4.10.md)), all fixed and confirmed. The Highs: the
shared inspections' result sets were tagged with a tab and evicted as another tab's (they now carry none, and the tab tier takes only
another tab's), and no log line said which tab a step or a link landed in (every path line, the workspace and tab lines and the
evictions name it). Moderates: the measurement redone with 8 tabs paged deep and closed, the smoke phases counted right, the tab
strip only on the Snapshot and Retainers tabs, a store test that proves the tab tier, the path list's selection kept across tabs
(fixed in the re-check), tab tests for every move, the memory line tested, late links through the page, logging.md. 18 Lows
listed. Measured: two Medium snapshots and 8 tabs on one hold 69 MB of managed heap in the UI process (about 1 to 3.5 MB a tab),
back to 35 MB when all close; a third in-memory Medium's inspections were refused by the worker budget on 32 GB. Gate at close: pass
(after a saturated machine and a try on claude-dev-2, review file), 0 warnings, 2,388 tests (the known answers run in CI).

---

## Sprint 4.11: Sunburst

**Goal:** a sunburst beside the icicle chart, for the dominator tree on the Retainers tab and for the allocation call tree and back
traces, so a person sees at a glance which part of a heap or of an interval's allocations is biggest, and from a chart reaches its row.
Input: peer review 4.5 M16 (the sunburst had no layout for call trees: the allocation trees had no chart at all).

1. [x] Sunburst geometry, toolkit free (UI.ViewModels): the focus as the center disc, each level a ring, each segment an arc whose angle
   is its share of the top; neighbors with one parent too narrow to point at are one "+N smaller" arc, as in the icicle; the arc under
   a point; the path to a selected arc. The texts (a segment's description and label) shared with the icicle. Tests: children lie
   inside their parent's angle; the grouping; the hit test of the center, a ring, a gap and outside; an empty chart.
2. [x] The sunburst control (UI): the same inputs as the icicle (segments, total, selected segment, focus command); hover says what an
   arc is, a click selects it and the path to it turns amber, a double-click runs the focus command; the center names the focus and
   its size. Tests: UI tests of the arcs drawn, a click and a double-click.
3. [x] The Retainers tab: a choice of Icicle or Sunburst above the chart, kept by the tab; the icicle parts of RetainersViewModel move to
   a part file of their own (the 600-line rule). Tests: the choice shows one chart; selection and focus work from the sunburst;
   view tests.
4. [x] The allocation chart (worker, protocol 27): a layout of the call tree (from the outermost frames) and of the back traces (from
   the allocating frames) of an interval and type, as chart segments keyed like the tree's rows (AllocationTreeKeys), six levels,
   segments under 0.1 percent left out; `QueryAllocationChartAsync` beside the tree query, logged with its counts. Tests: children fit
   inside their parent, the keys are the tree's, the totals are the tree's; the query over the pipe; the session method.
5. [x] The chart on the Allocations page: above the back traces or call tree, Icicle or Sunburst, queried with the tree for the same
   interval, type and kind; a click on a segment opens its ancestors in the tree and selects its row, and a selected row selects its
   segment. Tests: a segment reveals and selects its row; a row selects its segment; the chart follows the interval, type and kind.
6. [x] Docs: ADR-046 (the charts: the sunburst drawn from the icicle's segments, the allocation chart's layout in the worker), ARCHITECTURE.md,
   process-model.md (protocol 27), code-map.md, logging.md, the user guide (a draft), info-text.md (drafts).

**Decisions**
- Decided autonomously: the sunburst draws the icicle's segments (no new dominator query); the choice of chart is per tab and per
  Allocations view, starts at the icicle, and is not kept across restarts.
- Decided autonomously: the sunburst's rings carry no words (they curve and are short); the center names the focus, and hover names
  every arc.
- Decided autonomously: the allocation chart has no focus of its own: a double-click, like a click, shows the row in the tree, whose
  rows already narrow by type and interval. Both allocation trees get the chart.
- Decided autonomously: no chart in the CLI (its trees are text); no new sample scenario or lesson (the charts show what the
  existing lessons find); the screenshots stay as they are.

**Review summary:** 1 High and 14 Moderate ([reviews/sprint-4.11.md](../reviews/sprint-4.11.md)), all fixed and confirmed but the
measurement of M6, deferred to the Backlog. The High: a contested allocation chart could not be backed up from the log (now each
level's count, bytes and widest segments, what was left out and the cut, every click with its bytes and share, and the tree's revealed
row). Moderates: arcs drawn with no parent under them, a stale chart clickable while the next one loads, Close not stopping the chart,
the sunburst squeezing the tree, missing tests, the back traces' grouping allocations, a redraw rebuilding every arc, what the chart
left out and the cap, failures and empty charts that vanished, the wrong note in a cut tree, the drafts' missing limits, and a tally
that grew with the whole trace. 24 Lows listed. Measured: none beyond the tests (the large-trace measurement is in the Backlog). Gate at close: pass, 0 warnings, 2,417 tests
(the known answers run in CI).
