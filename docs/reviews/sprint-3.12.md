# Sprint 3.12 review: First-show performance and refactor analysis

Reviewers: solution-architect, senior-dev, gc-specialist, support-engineer (2026-11-14), on `sprint/3.12` against `sprint-3.11`.
Each reviewer read the working tree (none had a shell for `git diff`). Findings that overlap are merged and keep the highest
severity. Task 9 (the older cache fallback, ADR-041) had its own feature review and re-review earlier in the sprint, recorded
in its Done note in SPRINTS.md; this review looked at it only for what those missed. Counts: 5 High, 19 Moderate, 16 Low.

Before the review: Debug build with 0 warnings; 1,730 tests, 1,716 passed, 4 failed. One was real: the architecture test found
two public types in `Inspection.cs` (B7's `InspectionCounts`), now in its own file. Three integration tests failed under the
full run's load (the four reviewers and a build ran beside it) and passed alone: `LiveWorkflowTests` (already in the Backlog's
flaky list), `UiResponsivenessTests.Ui_stays_responsive_during_a_5M_synthetic_job` and
`PageCostTests.A_full_page_costs_little_to_receive_in_the_client_process` (added to it).

## High

### H1. The architecture docs did not describe what was built
- Reviewers: solution-architect; support-engineer (Moderate: logging.md lacked the prewarm lines and B4's set-aside entries,
  which task 14's Done note claimed).
- Where: `docs/architecture/graph-format.md` 6.3, `process-model.md` 4.1, `ARCHITECTURE.md` section 3, `docs/adr/README.md`
  and the titles of ADR-006, 032, 035, 036 and 041, `logging.md`.
- What: the object pass was still described as ClrMD's `EnumerateObjects` with a per-object finalizable lookup (R4 replaced
  both); taming the worker's GC threads was still "a backlog item" (task 6 settled it); the new test folders were in no layout;
  ADR-041's amendments were not marked on the ADRs it amends; logging.md did not list the new log lines.
- Why it matters: the Definition of Done requires the docs to describe what was built.
- Fix: graph-format's object pass rewritten with R4's layout rules and its proof; the GC threads sentence settled; section 3
  lists `tests/SharedSession`, `tests/SharedCapture`, `tests/HeapScope.Benchmarks.Tests` and the benchmarks' trace readers;
  "amended by ADR-041" on the four ADRs and their index lines, "amends" on ADR-041; logging.md describes the start and
  prewarm lines, the settings folder rule and B4's entries.
- Status: Fixed. Confirmed by solution-architect (2026-11-14); its nit (no code-map entry for the test folders) is left: the
  code map lists source projects only.

### H2. The UI's 64 MB gen 0 budget was shipped through a setting the runtime ignores
- Reviewer: gc-specialist.
- Where: `src/HeapScope.UI/HeapScope.UI.csproj` (`System.GC.Gen0Size`); budgets.md, process-model.md, SPRINTS task 6.
- What: task 6's A/B runs set the budget with the `DOTNET_GCgen0size` environment variable; the app shipped it as
  `System.GC.Gen0Size` in its runtime configuration, which the GC does not read. Confirmed by a test program (net10.0, 20
  million small allocations): 73 gen 0 collections with and without the setting (also as `GCgen0size`, decimal or hex), 14
  with the variable.
- Why it matters: task 6's Done note and the docs described a fix that did not run.
- Fix: the setting removed; budgets.md, process-model.md, task 6's Done note and the Backlog say the budget is not shipped and
  why. Task 4's A/B runs used the published build without the variable, so their numbers stand. Shipping the budget another
  way is M2.
- Status: Fixed. Confirmed by gc-specialist (2026-11-14).

### H3. Objects of collectible types had no edge to their LoaderAllocator
- Reviewer: gc-specialist.
- Where: `src/HeapScope.Loaders.ClrMd/ClrMdDumpLoader.cs`, the reference pass.
- What: the review read R4's skip of types without pointers as dropping the LoaderAllocator edge ClrMD yields for a
  collectible type. M1's fixture showed otherwise: ClrMD 4.2 marks the type collectible and has its LoaderAllocator handle,
  but yields no reference from the object, carefully or not, so the edge was never in the graph, before R4 or after. The GC
  does mark the LoaderAllocator through such objects.
- Why it matters: a LoaderAllocator held only by such objects looked unreachable: paths, dominators and retained sizes wrong
  for "why won't my context unload". No fixture has collectible types, so the graph hash could not see it.
- Fix: the skip applies only to types that are neither pointer-carrying nor collectible, and the loader adds the edge itself,
  reading each collectible type's LoaderAllocator once through its handle (graph-format.md, ADR-010). Test (with M1): the
  RuntimeKinds fixture now holds objects of a pointer-free type from a RunAndCollect assembly;
  `LoaderKnownAnswerTests.An_object_of_a_collectible_type_without_pointers_references_its_loader_allocator` fails without the
  added edge, and `Object_and_reference_counts_equal_clrmd` counts it.
- Status: Fixed. The first fix was confirmed by gc-specialist (2026-11-14) by reading the code; the edge added after the
  fixture's finding goes back to gc-specialist with M1.

### H4. A worker given another data folder read another settings file
- Reviewer: support-engineer.
- Where: `src/HeapScope.Application/Workers/WorkerHost.cs`, `src/HeapScope.Worker/Program.cs`, `WorkerOptions.cs`.
- What: the Settings page saves `settings.json` in the client's data folder; a worker started with `Worker:DataFolder` read
  that folder's file instead, usually missing, so filter text was logged although the user turned it off.
- Why it matters: the user's privacy setting did not reach the worker's log (a privacy failure, High by the rubric).
- Fix: the client always passes `--settings-folder` (its own data folder); the worker follows it, or the data folder for an
  older client. Test: `WorkerOptionsTests.The_settings_folder_is_the_clients_even_when_the_data_folder_is_elsewhere`. The
  worker's start line names the settings file it follows (from the confirmation).
- Status: Fixed. Confirmed by support-engineer (2026-11-14).

### H5. The logs' privacy setting fell back to "on" whenever the file could not be read
- Reviewers: senior-dev (Moderate, suggesting High under the privacy clause), support-engineer (Moderate); raised to High.
- Where: `src/HeapScope.Core/LogPrivacy.cs`, `HeapScopeSettings.cs`.
- What: an unreadable or damaged `settings.json` read as the defaults (filter text on), and its write time was recorded, so
  one failed read kept "on" for the rest of a worker's life while the page showed "off"; the save moved the file into place
  without flushing it, so a power loss could leave an empty file, which read as "on".
- Fix: `HeapScopeSettings.TryRead` tells a missing file (the defaults) from one that cannot be read now; `LogPrivacy` keeps the
  last value read, reads again at the next check, and keeps filter text out if the file was never read; `Save` flushes to disk
  before the move. Tests: `HeapScopeSettingsTests.An_unreadable_file_keeps_the_last_setting_read_and_is_read_again_next_time`
  and `A_file_that_cannot_be_read_from_the_start_keeps_filter_text_out`. From the confirmations (senior-dev and
  support-engineer both): a failure to read the file's write time before the first read now keeps filter text out too. A
  file unreadable at first and then deleted keeps filter text out until it is written again (fails closed; accepted).
- Status: Fixed. Confirmed by senior-dev and support-engineer (2026-11-14).

## Moderate

The owner decided on 2026-11-14: fix all nineteen, the parts recommended for the Backlog included (M1's collectible
fixture, M2's relaunch, M4's CLI commands, M7's hard faults, M8).

- M1. R4's object walk encodes CoreCLR's object layout beside ClrMD, with no ADR and no CI proof (solution-architect,
  gc-specialist). The header mask, the component count, the minimum object, the alignment and the allocation context skip are
  recorded nowhere as decisions; the proof is a benchmark's hash on two local .NET 10 Windows dumps, leaving out free bytes,
  roots and counts, never running the per-segment fallback (where a range walk starting early would hit the duplicate
  address check and fail the load), segment GC (.NET 6), Linux core files or collectible types. Suggested: amend ADR-010; a CI
  integration test comparing the walk object for object (address, type, size, kind, flags, free bytes) with ClrMD's on every CI
  system; a seam forcing the fallback mid-segment; free bytes and roots in the hash; the collectible fixture (Backlog) for H3.
- M2. Shipping the gen 0 budget (H2): only the environment variable sets it, so the app would relaunch itself with it, at
  the cost of a second process start on every launch, or drop it (gc-specialist, H2).
- M3. One path identity rule is not at every path comparison (solution-architect; senior-dev Low; solution-architect Low):
  Core-side projects cannot reach `PathIdentity` (Contracts), so `DacLocator` keeps its own copy; `ProcessDiscovery` compares
  ignoring case on every system; `SettingsService` and `PageViewModels` build their own "is under"; macOS, case-insensitive by
  default, is treated as case-sensitive, so `App.dmp` and `app.dmp` open as two snapshots of one file there. Task 21's Done
  note overstates it.
- M4. ADR-008 says every feature has a UI and a CLI command; the filter text setting and the two purges are on the Settings
  page only, so a CLI user (CI) changes the setting by editing `settings.json` (solution-architect).
- M5. Purge logs and Purge all delete the log another HeapScope process is writing (a second window, a CLI run), since logs are
  opened with delete sharing; its later lines, a B5 id among them, are lost (senior-dev; solution-architect Low).
- M6. The finalizable "merge" is not one: its index restarts at every segment and a binary search runs for most objects
  (gc-specialist; senior-dev Low). Results are right; the hot loop does needless work and the docs describe a merge.
- M7. The load meter's numbers are labeled loosely: "page faults" are all faults on Windows and major faults on Linux; CPU and
  GC pause are the whole process's, so queries during a load count in it; the fault counter can wrap (gc-specialist;
  solution-architect, support-engineer Low).
- M8. An array or string whose corrupted length runs past its segment's end is added at that size, and the segment still
  counts as complete (gc-specialist; not new in this sprint).
- M9. Purge all lists and measures the whole library on the UI thread before its first await, without cancellation
  (senior-dev).
- M10. Purge failures other than a refusal (an I/O error listing a folder, a worker failure while closing snapshots) reach the
  last-chance dialog with no next step, and a failed close leaves no outcome line and a half-done purge (senior-dev;
  support-engineer).
- M11. `SettingsViewModelTests` do not prove the purge's safety rule: no session records a dump outside its folder or a cache in
  the fallback root, no snapshot is open when Purge all closes them, and the in-use refusal is untested (senior-dev).
- M12. The prewarm's paths beyond the happy one are untested (a cancel mid-run, a failing step, a page shown before its step),
  and a window closed mid-run does not stop it until the host stops, after the shell is disposed (senior-dev; support-engineer
  Low on its done line).
- M13. A Purge all refused at its plan step (a job running, a session in use) is shown but not logged (support-engineer).
- M14. Purge all logs totals only: not which session folders, not the dumps kept outside them, not files a session does not
  list but the recursive delete removes; Purge logs does not name the files it could not delete (support-engineer).
- M15. B7's counts say "left out 0" where objects were left out (an unreadable disposed flag, types under the minimum, a
  finalizer queue under its threshold); seven inspections log "basis: not recorded"; the 50-row cap is printed where no rows are
  listed; two lists stop without "and N more" (support-engineer).
- M16. The CLI writes the worker's error line to stderr, so an unexpected failure shows the exception's text, its stack and the
  request (filter text included) before the fixed message; its message does not name the log folder (support-engineer).
- M17. Several UI catch sites show a worker message (now possibly a B5 id) without a UI log line of their own, and four RPC
  methods (`CloseSnapshotAsync`, `ReleaseResultSetAsync`, `GetInfoAsync`, `ShutdownAsync`) skip `GuardAsync`, so their failures
  carry the exception's text and no id (support-engineer).
- M18. Session entries set aside for an enum value this build does not know (B4) are not logged: "my snapshot vanished" has no
  answer (support-engineer).
- M19. No process logs the privacy state it applies, at start or when it changes (support-engineer, the rest of H5's finding).

## Low

Listed only.

- L1. `TraceStore.cs:95`: an empty trace path is a plain `ArgumentException`, shown as an unexpected failure (senior-dev).
- L2. `SetDtos.cs:217`: an unknown set kind throws from `CacheKey` before the resolver's own refusal (senior-dev).
- L3. `RequestRefusedException(message, paramName)` at about 20 sites: users see " (Parameter 'request')" after the text
  (senior-dev).
- L4. `InstancesSource.cs:193, 329`: two internal invariants are refusals, logged without a stack (senior-dev).
- L5. `InspectionsViewModel.cs:472`: a broad `InvalidOperationException` catch labels any such failure "This result could not
  be shown" (senior-dev); that text is not in info-text.md (support-engineer).
- L6. The purge's busy check is not held through the deletion; a session started or a snapshot opened meanwhile is not
  covered (senior-dev).
- L7. Purge all leaves `bookmarks.json.damaged-*` files and deletes `bookmarks.json` without the bookmark lock; "kept until
  HeapScope closes" may read as "deleted at close" (senior-dev; the wording is the owner's).
- L8. The protocol history's version 22 entry leaves out `RetainedIs` and B5's narrower `InvalidArgument`
  (solution-architect).
- L9. A `settings.json` this build cannot parse is overwritten by the next save, dropping a newer build's members
  (solution-architect).
- L10. ARCHITECTURE.md calls Core "pure domain types"; it now also holds the settings file and the process's log privacy
  (solution-architect).
- L11. No test proves the prewarm's dropped views can be collected (gc-specialist).
- L12. A zero component size returns the base size without the minimum object size (gc-specialist).
- L13. The finalizable list is deduplicated through a hash set after sorting; `_ = heap;` is a dead parameter (gc-specialist,
  senior-dev).
- L14. "Smoke slow operations" does not say whether the ceiling applied or what it decided (support-engineer).
- L15. `DacRefusals.cs:45`: an untrusted DAC without a hash falls to the default text, which carries the exception's message
  (support-engineer).
- L16. The prewarm's done line does not count failed or skipped steps, and its skip line does not say which reason applied
  (support-engineer).

## Markdown sweep

Pending (after the Moderate decisions).
