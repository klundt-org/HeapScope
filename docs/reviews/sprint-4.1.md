# Sprint 4.1 review: Capture on Linux

Reviewers: solution-architect, senior-dev, gc-specialist, support-engineer (2026-11-20), on `sprint/4.1` against the tag
`sprint-3.13.2`. Each reviewer read the working tree (none had a shell or ran anything). Findings that overlap are merged
and keep the highest severity. Counts: 6 High, 13 Moderate (2 from the confirmations), 34 Low (15 from the confirmations).

Before the review: Debug build with 0 warnings; the fast tier passed (1,534 tests, 1,522 passed, 12 skipped). The first CI
run on Linux failed 3 tests that expected the Windows wording on every system (fixed before the review: each expects its
system's words); the second passed the Linux job end to end: the sample listed, captured as a full dump (94 and 95% of the
estimate written) and a gcdump, and both counted its 2,000 entries. Its Windows job failed
`LiveWorkflowTests.A_run_records_an_allocation_trace_for_its_whole_length`, a known flake that passed in the first run.

The owner decided the Moderates' handling in advance (autonomy for Phase 4): each was applied as recommended, marked
"decided autonomously". New user-facing texts from the fixes are drafts in `docs/design/info-text.md` for the owner.

## High

### H1. As root, another user's full dump into HeapScope's folders always fails, and the error names the wrong cause
- Reviewers: support-engineer, solution-architect, gc-specialist (High), senior-dev (Moderate).
- Where: `ElevationTexts` (the "run as root" next step), `CaptureService.StartCapture`, `PrivateFiles` (0700 folders),
  `DumpCapturer.Translate`.
- What: the runtime's createdump writes the dump as the target's user, who cannot write to root's 0700 session folders. The
  failure then read "the runtime refused the request", or blamed ptrace or the container.
- Why it matters: the next step HeapScope gave for another user's process could not work for its main feature.
- Fix: a full dump into HeapScope's folders is refused before anything is written when HeapScope runs as root on Linux and
  the process as another user (`DumpCapturer.LibraryRefusal`, draft text "runs as another user, and the runtime writes its
  full dump as that user ... capture a gcdump; on the command line, --out can name a folder that user can write"). A
  folder the user names (`--out`) is tried, and its failure is named by H2. README and INSTALL say root works for all but a
  full dump into HeapScope's own folders. Making it work (a staging folder owned by the target's user) is in the backlog.
  Tests: `CaptureExplanationTests.As_root_on_linux_another_users_full_dump_is_refused_for_the_library_before_anything_is_written`
  (Linux), `LinuxCaptureRulesTests.As_root_another_users_full_dump_is_refused_for_the_library_only_on_linux`.
- Status: Fixed and confirmed (Confirmations below).

### H2. A Linux write failure was blamed on ptrace or the container whatever its cause
- Reviewers: support-engineer (High), solution-architect (Moderate), gc-specialist (Low).
- Where: `DumpCapturer.Translate`, `LinuxCaptureRules`.
- What: with `ptrace_scope` 2 or 3, or in a container, every createdump failure got the ptrace or `SYS_PTRACE` reason; the
  Windows HRESULTs it checked do not occur on Linux, so a full disk or an unwritable folder was misnamed.
- Why it matters: the user is told to lower a kernel security setting for a failure it would not fix.
- Fix: `LinuxCaptureRules.WriteFailure` reads what createdump reported first: "No space left" is the full disk, "Permission
  denied" or "Could not open" without ptrace is the folder the target's account cannot write, and the ptrace rule or the
  container explain only a reported ptrace failure ("ptrace", "Operation not permitted") or scope 3, where nothing can
  attach. The rule that decided is logged (H6). Tests: `LinuxCaptureRulesTests.A_write_failure_createdump_explains_is_named_before_any_system_setting`,
  `The_ptrace_rule_and_the_container_explain_only_a_ptrace_failure_or_scope_3`.
- Status: Fixed and confirmed (Confirmations below).

### H3. A single-file app was called "not a .NET process", and a process with its own /tmp was not explained
- Reviewers: support-engineer (High), gc-specialist, senior-dev (Moderate).
- Where: `LinuxCaptureRules.WhyNoDiagnostics`.
- What: a single-file app links the runtime into its executable, so its maps name no `libcoreclr.so`; it was declared not
  .NET. A service with `PrivateTmp`, or a container, names the same `/tmp` and sees another one, so its socket is never
  found and nothing said why.
- Why it matters: a stated cause that was not measured sends the user and support the wrong way, for the most likely Linux
  targets (services).
- Fix: "not .NET" only when the executable holds no runtime version string either (read in 64 KB pieces); a process in
  another mount namespace gets a draft text ("probably has its own /tmp (a service with PrivateTmp, or a container) ... Run
  HeapScope where the process runs"). Tests: `Only_a_process_without_a_runtime_anywhere_is_called_not_dotnet`,
  `A_process_with_its_own_tmp_says_so_before_comparing_temp_folders`, `A_marker_is_found_across_the_pieces_a_large_executable_is_read_in`.
- Status: Fixed and confirmed (Confirmations below).

### H4. The DAC copied beside a Linux dump would not be found
- Reviewers: solution-architect (High), gc-specialist (Moderate), senior-dev (Low).
- Where: `CaptureService.CopyDac`, `DacLocator.BesideDumpDirectories`.
- What: the capture names `dac/<version>` by the runtime library's file version, the loader looked only in the folder named
  by ClrMD's version, which on a Linux core file may be 0.0 (then the runtime folder's name, or 0.0 for a self-contained
  app). And a DAC found by its runtime library's build id needs `libcoreclr.so` beside it, which was not copied.
- Why it matters: a dump of a self-contained app, or one opened after the runtime was updated, silently loses its DAC. CI
  could not see it: the installed runtime is searched first.
- Fix: the loader searches the named folder, then every other `dac/` folder (the identity checks pick the file); on Linux the
  capture copies `libcoreclr.so` beside the DAC (read for its identity, never loaded or trusted). Test:
  `DacLocatorTests.Every_dac_folder_beside_the_dump_is_searched_after_the_one_named_by_the_version`. A CI open with the
  installed runtimes switched off is in the backlog with the Low findings.
- Status: Fixed and confirmed (Confirmations below).

### H5. As root, HeapScope read and copied files at paths another user's process named
- Reviewers: solution-architect.
- Where: `LinuxProcessFacts.FileVersion`, `CaptureService.CopyDac`.
- What: the runtime folder comes from the target's `/proc/<pid>/maps`; HeapScope read `libcoreclr.so` whole and copied the
  DAC from it, following links, with no check. A pipe there hangs the process list; a link to a device never ends.
- Why it matters: a less privileged user could stall or exhaust a root HeapScope.
- Fix: `TargetFiles.IsReadable`: a file named by a target is read or copied only when it is a regular file (links followed)
  of 64 bytes to 64 MB. Test: `LinuxCaptureRulesTests.Only_a_regular_file_of_a_sane_size_from_a_target_is_read` (on Linux
  also `/dev/zero` and a link to it). A file swapped between the check and the read is not covered (Low).
- Status: Fixed and confirmed (Confirmations below).

### H6. The Linux refusals logged their conclusion, never what it rested on
- Reviewers: support-engineer.
- Where: `LinuxCaptureRules`, `CaptureService`, `DumpCapturer`.
- What: no user ids, `ptrace_scope`, container marker, `TMPDIR`s or runtime finding reached the log.
- Why it matters: support could not back up a contested refusal.
- Fix: `IProcessDiscovery.WhyNotFound` returns the reason with its evidence (`NotFoundExplanation`); the capture logs "Pid
  {pid} was not found: Linux check of pid ...: running, effective user and HeapScope's, environ readable, diagnostics off
  (the setting's name only), TMPDIR and HeapScope's, mount namespace, runtime". A write failure logs "judged by: ptrace_scope
  {n}; container yes/no; rule: ...". No value from the environment but `TMPDIR` is logged. Tests:
  `The_evidence_names_ids_folders_and_the_runtime_but_no_values`,
  `CaptureExplanationTests.A_capture_of_a_pid_that_is_not_found_says_why_and_logs_what_that_rests_on`. Described in
  `docs/architecture/logging.md`.
- Status: Fixed and confirmed (Confirmations below).

## Moderate

### M1. "Another user" was inferred from an unreadable environment
- Reviewers: support-engineer, senior-dev. An exited process or a same-user process that is not dumpable was told to run as
  another user. Fix: the effective user ids decide (from `status`, which anyone reads); an unreadable `status` means exited;
  as root the other checks speak. Tests: `Another_users_process_is_told_by_its_user_id_not_by_an_unreadable_environment`,
  `As_root_another_users_process_is_judged_by_the_other_checks`, on Linux `On_linux_pid_1_is_judged_without_reading_what_another_user_keeps`
  and `On_linux_an_exited_process_has_no_reason`. Fixed (decided autonomously).

### M2. Live and recording did not give the Linux reasons
- Reviewers: support-engineer. `monitor` and `trace` said "did not answer ... Is it a .NET 6 or later process?". Fix:
  `CounterMonitor` and `TraceRecorder` give the same Linux reason as a capture when there is one. Fixed (decided
  autonomously).

### M3. The Record allocations tooltip still said "on Windows"
- Reviewers: support-engineer, senior-dev. Fix: "on Windows or Linux" (draft for the owner) and the stale page comment.
  Fixed (decided autonomously).

### M4. logging.md did not describe the Linux lines
- Reviewers: support-engineer. Fix: a "Capture on Linux" entry in section 11 with every line from H6 and the estimate line.
  Fixed (decided autonomously).

### M5. The README did not say macOS does not capture
- Reviewers: support-engineer. Fix: "macOS: analysis only, untested", with `dotnet-dump`; Linux keeps "everything", which
  CI's Linux check now exercises (M7). Fixed (decided autonomously).

### M6. The per-system split is partial, so macOS would touch about nine places
- Reviewers: solution-architect (senior-dev's Lows on macOS getting the Windows facts and on `CanCapture`). Deferred to the
  start of sprint 4.2, where macOS needs it (BACKLOG, "Capture on Linux, what sprint 4.1 left out") (decided autonomously).

### M7. Live counters and allocation recording never ran on Linux
- Reviewers: solution-architect. Fix: `build/linux-capture-check.sh` also runs the SteadyAllocation scenario, monitors the
  sample (at least two samples in three seconds) and records an allocations trace that `allocations` must name
  `HeapScope.Sample.PricedOrder` from. Checked on Windows here; CI's Linux job runs it. Fixed (decided autonomously).

### M8. Tests missing for the explanations and the /proc edges
- Reviewers: senior-dev. Fix: `CaptureExplanationTests` (the reason and its evidence in a capture and in Force GC, the
  generic text without one, the list's temp folder line), the write failure rules as a function of what was read, and on
  Linux pid 1 and an exited process. Fixed (decided autonomously).

### M9. The Capture tests failed on macOS
- Reviewers: senior-dev. Fix: the capture tests skip where capture is not built, and the estimate's memory walk returns
  nothing off Windows and Linux. Fixed (decided autonomously).

### M10. The Linux check wrote to the user's own data folder
- Reviewers: senior-dev. Fix: the script sets a scratch `HEAPSCOPE_DATA_FOLDER` and removes the work folder it made. Its
  earlier local run (task 7) had added two sessions to the developer's own library, which the owner was told about. Fixed
  (decided autonomously).

### M11. The Linux estimate's margin was printed, not checked
- Reviewers: senior-dev. Fix: CI warns when a Linux dump is larger than its estimate; the first runs wrote 94 and 95%.
  Fixed (decided autonomously).

### M12. A refusal of another user's process logged no user ids (from the confirmations)
- Reviewers: support-engineer (H6's second round). The CLI's `capture --pid` and `monitor --pid` run in their own process,
  so no process list backs their refusal up. Fix: on Linux a refusal of another user's process logs "Pid {pid} was refused
  as another user's: effective user {n}, HeapScope's {n}" in a capture, Live and a recording; the list line adds HeapScope's
  id and each marked process's (and "and N more" past 200); Live and recording log their not-found evidence too. Confirmed
  by the support-engineer (decided autonomously).

### M13. Nothing opened a Linux capture with only the DAC copied beside it (from the confirmations)
- Reviewers: solution-architect (H4's second round). Fix: `PlantedDacTests.A_DAC_kept_in_a_dac_folder_beside_a_real_dump_is_found_without_installed_runtimes`
  opens the fixture dump with no installed runtime, through the `dac/` folder the capture left beside it (CI's CLI
  capture), or one it plants under a name no version matches; refused until trusted, then loaded from the private copy.
  CI's Linux job runs it with `HEAPSCOPE_REQUIRE_FIXTURE=1`; run here both ways. Confirmed by the solution-architect
  (decided autonomously).

## Low (listed, not fixed)

- L1 (support-engineer): the "Captured" line and a disk refusal do not say whether the estimate came from `maps` or the
  private-bytes fallback.
- L2 (support-engineer): the write failure's next step names `--out`, which the app has no field for.
- L3 (support-engineer): other users' command lines are readable on Linux and shown in the list (an export leaves them out
  unless the user opts in, ADR-026).
- L4 (solution-architect): `ElevationTexts` puts front-end words in Core; ADR-014 is not amended for Linux's rule;
  `NeedsAdministrator` now also means "needs root or its user"; the Learn note is a third place for these texts.
- L5 (solution-architect): stale lines in ARCHITECTURE.md, ADR-025, the code map and ProcText's comment (corrected by the
  Markdown sweep below).
- L6 (gc-specialist): the diagnostics settings are not resolved as the runtime does (DOTNET_ before COMPlus_, values in hex).
- L7 (gc-specialist): the scope 2 text is wrong for a root target; the container check sees only HeapScope's own markers.
- L8 (gc-specialist, senior-dev): a runtime mapping marked "(deleted)" is read at its path, now another file.
- L9 (gc-specialist): `maps` is read whole and split per refresh; `libcoreclr.so` is read whole once per file.
- L10 (gc-specialist): the estimate's "errs high" comment; CI measured 94 to 95%.
- L11 (senior-dev): `ExecutablePath(path)!` without a comment.
- L12 (senior-dev): macOS gets `WindowsProcessFacts` (with M6).
- L13 (senior-dev): the screenshot run forces the chart tooltip open, does not observe the command on its timeout path, and
  a comment says "widest bar" where it orders by Y first.
- L14 (senior-dev): `PurgeAllQuestionAsync` takes no cancellation token.
- L15 (senior-dev): the estimate is logged as preformatted text, not numbers.
- L16 (senior-dev): the CLI says "elevated" on Linux where the app says "Needs root or its user"; the sample stop failure
  names Task Manager on Linux.
- L17 (senior-dev): a failed `list` in the check script exited without its message (fixed with M7's edit).
- L18 (senior-dev): `ProcFactsTests` compares the image path with itself and the runtime folder without resolving links.
- L19 (senior-dev): `PlatformHints.CanCapture` repeats `CapturePlatform.IsSupported` untested (with M6).

From the confirmations (listed, not fixed):

- L20 (gc-specialist): two self-contained runtimes whose version cannot be read share `dac/unknown`; the first copy wins
  (the identity checks reject the other, so it is safe).
- L21 (gc-specialist): a socket removed by a temp cleaner gets the generic text; a hint could name it.
- L22 (gc-specialist): an open() failing with "Operation not permitted" would be classed as a ptrace failure.
- L23 (solution-architect): a monitor run as root on another user's process is refused at each rule firing, not at Start;
  `profile.CopiesDac` stands for "a full dump" (now commented).
- L24 (solution-architect): the createdump messages the rules match are assumed, not recorded from a real Linux failure;
  replace the test strings when one is seen (the generic text stays the fallback).
- L25 (solution-architect): a file swapped between `TargetFiles`' check and the read is not covered; a bounded read would.
- L26 (solution-architect): the new DAC test compares a path ignoring case.
- L27 (support-engineer): the approved column tooltip says root "can" capture another user's process; a full dump into
  HeapScope's folders is refused.
- L28 (support-engineer): Live and recording reuse texts that end "then capture it again".
- L29 (support-engineer): another user's process that is not .NET is still told to run as that user (its maps cannot be
  read).
- L30 (support-engineer): "probably has its own /tmp" rests on another mount namespace, not on comparing the folders.
- L31 (support-engineer): a monitoring run's own allocation recorder (`new TraceService()`) has no logger; Force GC does not
  log a `GcTrigger` failure's evidence.
- L32 (support-engineer): when a user id cannot be read, Live and recording log "(null)" where a capture logs "unknown".
- L33 (senior-dev): no test checks the "judged by" log line of a write failure.
- L34 (senior-dev): the ci.yml header did not mention monitor and trace (fixed with the sweep).

## Confirmations

Each fix went back to the reviewer that raised it (resumed with the finding and the changed files); all four read the code
and ran nothing.
- gc-specialist: H1, H3, H4 Confirmed (first round), with L20 to L22.
- solution-architect: H1, H2, H5, M6 (deferral), M7 Confirmed (first round); H4 Confirmed in code, with a Moderate (M13) and
  the docs (L5) pending; second round: docs Confirmed, M13's first test would fail in CI (the capture's own `dac/` folder
  made it fail its guard); third round: M13 and the security page Confirmed.
- support-engineer: H1, H2, H3, M1 to M5 Confirmed (first round); H6 not confirmed (Live and recording, the list's ids, the
  root refusal's ids); second round: those Confirmed, H6 no longer High, a Moderate left (M12); third round: M12 Confirmed,
  with L32.
- senior-dev: H1, M1, H3, M8, M3, M7, M10, M11 Confirmed (first round); M9 not confirmed (the gcdump tests) and a doc comment
  out of place; second round: both Confirmed.

## Markdown sweep

Every tracked Markdown file was searched for what this sprint changed (capture "on Windows" or "a backlog item" off
Windows, the renamed or new types, `mscordaccore.dll` alone, the Linux job's steps, the data folder rule) and the hits
read against the code. Changed: `docs/ARCHITECTURE.md` (Linux capture since 4.1, the DAC pair and the `dac` search),
ADR-025 (the backlog line; what `IProcessFacts` carries and what stays Linux calls until 4.2; root, `TargetFiles`, the
`dac` search), `docs/architecture/code-map.md` (the review's types), `docs/architecture/logging.md` (the Linux lines),
`docs/architecture/security-privacy.md` (capture on Linux), `docs/DEVELOPER.md` (the Linux job's monitor and trace steps,
the scripts' scratch data folder), `docs/SPRINTS.md` (task notes: the version, the approval, the CI proof),
`docs/BACKLOG.md` (what 4.1 left out, M6 for 4.2), `docs/design/info-text.md` (the review's drafts), `README.md` and
`INSTALL.md` (root, macOS), and the CI header. `RELEASE-NOTES.md`'s 3.13 limit and `docs/sprints/` are records and stay.
ADR-014 is not amended for Linux (L4). No em or en dashes in the 73 changed files.
