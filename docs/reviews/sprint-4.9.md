# Sprint 4.9 review: Workspaces

Reviewed 2026-11-27 against sprint-4.8 by all four reviewers, once the 7 tasks were in. Findings that overlap are listed once, under the
highest severity. Moderates decided autonomously: fixed, or deferred to the Backlog (or to sprint 4.10) where said.

## High

- H1 (support-engineer) `docs/architecture/logging.md` described none of the sprint's new or changed lines (the workspace shown, the
  refused link, the suspended run, which snapshot the worker was told, the eviction that now names it), against DEVELOPER.md 9.
  Fixed: a "Workspaces" entry lists each line, its level, how to read a re-run and that hidden evictions need
  `Logging:File:MinimumLevel=Debug`. Status: Fixed.

## Moderate

- M1 (solution-architect, support-engineer; senior-dev, Low) A hidden workspace still reran its growth detectors when another snapshot
  opened or closed, against ADR-044's "a hidden workspace queries nothing". Fixed: a baseline change while the Inspections tab is not
  shown leaves the growth tiles stale; they run when it is shown, logged as such. Test:
  `WorkspaceTests.A_hidden_workspace_reruns_its_growth_detectors_only_when_shown_again`. Status: Fixed (decided autonomously).
- M2 (senior-dev; gc-specialist, Low) `Suspend` restarted the whole run when only a rerun of the growth tiles was in flight, dropping
  every finished result in the main growth workflow. Fixed: a rerun stops alone, and its stale tiles run when shown; only an unfinished
  first run starts again, and its log line says how many results it drops. Test: `A_growth_rerun_stopped_by_a_switch_keeps_the_other_results`.
  Keeping the finished tiles of an unfinished first run is not done (the GC Low): listed below. Status: Fixed (decided autonomously).
- M3 (senior-dev) A chosen "None" baseline was lost on a suspended run or Try again (`kept is not null`), so the growth detectors ran
  against a baseline the user had turned off. Fixed. Test: `A_chosen_None_baseline_stays_when_a_hidden_run_starts_again`.
  Status: Fixed (decided autonomously).
- M4 (solution-architect, gc-specialist) The result set store kept its 32-set cap, which one workspace's inspections nearly fill, and
  its preference is by snapshot, which tabs of one snapshot will share. Fixed: the cap is 128 sets, the 256 MB byte budget stays the
  memory guard (test: two workspaces of inspection sets fit); ADR-044 records that 4.10 tags sets by workspace instead (Backlog, for
  4.10). Status: Fixed; the per-workspace tag deferred to 4.10 (decided autonomously).
- M5 (gc-specialist) Closing a snapshot in the worker kept its result sets, so one unreleased set kept a closed graph alive through its
  lease. Fixed: `ResultSetStore.ReleaseSnapshot` drops them on close, logged with the count. Test:
  `ResultSetStoreTests.Closing_a_snapshot_drops_its_sets_and_returns_their_leases`. Status: Fixed (decided autonomously).
- M6 (gc-specialist) Front end memory with three Medium snapshots open, each with its workspace, has never been measured. Deferred:
  Backlog, to be measured before sprint 4.10 starts (it needs the Medium dumps and a quiet machine). Status: Deferred (decided
  autonomously).
- M7 (solution-architect) Links are routed by snapshot, not by the workspace they came from, so a link in one tab of a snapshot would
  land in "that snapshot's workspace". Deferred to 4.10 (ADR-044, Backlog): the workspace becomes the navigator of its own views.
  Status: Deferred (decided autonomously).
- M8 (solution-architect) Nothing tested that a new worker is told the shown snapshot after its reopen. Fixed:
  `WorkspaceTests.A_new_worker_is_told_the_shown_snapshot_once_it_is_reopened_there`. Status: Fixed (decided autonomously).
- M9 (support-engineer) At the default level the log could not back up the promise that the shown snapshot keeps its sets: evictions,
  the tell and its failure were Debug, and a tell skipped for a stopped or restarted worker left no line. Fixed: an eviction of the
  shown snapshot's set (or with none shown) is Information with the store's state and limits; a failed or skipped tell is Information
  with why. Status: Fixed (decided autonomously).
- M10 (support-engineer) "Workspace shown" did not say new or kept, the path or the tab, and closing a workspace left no line. Fixed:
  both lines as in logging.md; the reopen line names the workspace. Status: Fixed (decided autonomously).
- M11 (senior-dev) The tests did not prove all the tasks claimed: the sort, path and retainers kept on a switch, a Retainers link into
  another workspace, and a re-run of only what the worker evicted. Fixed: `Switching_keeps_the_sort_the_path_and_the_retainers_shown`,
  `A_retainers_link_into_another_snapshot_lands_in_its_workspace`, `A_workspace_shown_again_runs_only_what_the_worker_evicted` (which
  uses the fake's `Evict`). Status: Fixed (decided autonomously).
- M12 (senior-dev) `SingleThreadUiDispatcher` let a posted action's exception end the test process, and `Invoke` on its own thread
  deadlocked. Fixed: the pump records the exception and goes on, and a checked run fails on it (`Faults`); `Invoke` runs inline on the
  UI thread; `Dispose` joins the thread. Status: Fixed (decided autonomously).
- M13 (senior-dev) The watch missed Compare, the bookmarks, the path's subjects and Retainers' set, and work that finished after a test
  body; the trust tests built pages on the immediate dispatcher. Fixed: `WatchPage` watches them, every subject the navigator shows
  included; the page and workspace test classes check again at their end; those tests use the UI thread. The off-thread test now proves
  `RunCheckedAsync` fails. Status: Fixed (decided autonomously).
- M14 (senior-dev, solution-architect, gc-specialist) Task 5 was incomplete and partly damaged: about 80 ids remained in `.axaml` and
  project files, bare history ("since 3.4", "before 3.1") and ids split over two lines passed the test, a few sentences lost their full
  stops, the loader version history was garbled, a measured 1.2 GB read as the load's peak, and DacLocator lost its two version
  examples. Fixed: the XML comments of views and project files and the bare history reworded; the broken sentences, the loader history,
  the 1.2 GB (chunks, with the peak's 3.2 to 1.7 GB) and DacLocator's versions repaired; `SourceCommentTests` reads a run of comment
  lines as one text, `/* */` and the XML comments, and refuses "since/before X.Y" (and X.Y.Z), with tests. DEVELOPER.md 4 says what
  is enforced. Status: Fixed (decided autonomously).
- M15 (support-engineer, in the re-check) "Workspace shown ... kept as it was, nothing queried" was logged after the tab was set, which
  may start a stopped run or the stale growth tiles, so it claimed more than was true and followed the queries it caused. Fixed: logged
  before the tab, and a kept workspace says what may still run. Status: Fixed (decided autonomously).

## Low

Listed only, not fixed (from the review and its re-checks).

- L1 (solution-architect, senior-dev) `ShowInBackground` sends unordered calls; two quick switches (or a close that sends none, then the
  next) may be applied out of order until the next switch. `InvokeAsync` can start a worker that died between the state check and the
  call. Line 242's `&&`/`||` precedence wants parentheses (rewritten since as two checks).
- L2 (solution-architect, senior-dev) `NavigatorViewModel` still keeps a path per snapshot and `Forget(snapshot)` though a workspace has one
  snapshot; it does not refuse a target of another snapshot. `Hide`/`SetActiveTab` are not guarded after `Dispose`; `Inspections` and
  `Retainers` have no summaries on the workspace.
- L3 (solution-architect) ADR-029 still lists releasing hidden snapshots' sets in the Backlog, and BACKLOG still lists the workspace per
  snapshot (P21); the "when to bump" rule leaves out an added method; ADR-044 called tabs a Phase 4 candidate (fixed with M4's ADR text).
- L4 (gc-specialist) One evicted owner whose `Dispose` throws leaves the other evicted owners undisposed.
- L5 (gc-specialist) An unfinished first run that a switch stops drops the inspections it had finished; they run again when shown.
- L6 (senior-dev) A Retainers link into a workspace not yet shown, while the Retainers tab is active, queries the biggest retainers before
  the link's target.
- L7 (senior-dev) Closing the selected snapshot shows the empty workspace, then the next: two tells and two "Workspace shown" lines.
- L8 (senior-dev) `!` suppressions without a comment (`out workspace!`, `state!`).
- L9 (senior-dev) The workspace tests swallow a temp folder that stays instead of using `TestFolders.Delete`; the store test does not
  assert the second tier after `TryGetPage` reorders recency, nor the byte budget with a snapshot shown.
- L10 (support-engineer) The refused link's line names the old snapshot id, not the path, the link's source or which check refused.
- L11 (support-engineer) The re-query line has no previous row count or snapshot, so a re-run that gives another count is not flagged.
- L12 (support-engineer) The closed-snapshot link text names only the file and does not say to follow the link again from the reopened
  snapshot; it is a draft for the owner.
- L13 (support-engineer, senior-dev, pre-existing) CLI option help still says "(sprint 3.3)", "(sprint 2.6)" and "(sprint 3.5)":
  user-facing text, left for a text pass with the owner's approval.
- L14 (gc-specialist, solution-architect) A query that holds its lease when its snapshot closes can still add its set after
  `ReleaseSnapshot`; that set keeps the closed graph until released or evicted (process-model.md says so). A throwing owner `Dispose`
  in `ReleaseSnapshot` skips the rest and fails the close (L4's path).
- L15 (senior-dev) A cancelled rerun's tile status can overwrite "Running..." on a tile the next rerun has started.
- L16 (senior-dev) The switching test does not assert that no new retainers query ran; the Retainers link test starts from the Snapshot
  tab, not with Retainers active (L6).
- L17 (senior-dev) The test dispatcher's `BlockingCollection` is never disposed; one page in `SnapshotsViewModelTests` is not disposed.
- L18 (senior-dev) `SourceCommentTests` skips a comment after code on a line with a quote before it (`Foo("x"); // ...`), and a bare
  finding id ("as B5 shows") passes it; the one found was removed.

## Measurements and runs

- Before the review: Debug build 0 warnings; `dotnet test --solution HeapScope.sln` 2,354 tests, 2,327 passed, 27 skipped, 0 failed.
- Page and workspace tests on the UI thread: no change was raised off it, five runs.
- After the fixes: the view model tests 357, the architecture tests 83, each run green; the race test fails with its fix removed.
- Final run: Debug build 0 warnings; 2,371 tests, 2,341 passed, 27 skipped, 3 failed under load and passed alone (the load cancel's
  2 s, the sample's CLI start, `StartFailureTests`' log folder; Backlog).

## Confirmations

- First re-check (commit 94eef0a): support-engineer confirmed H1, M9, M10 and M1's deferral, and raised M15; gc-specialist confirmed M4
  and M5, accepted M6's deferral, and found the 1.2 GB comment (M14) still inaccurate (fixed with its wording); senior-dev confirmed M3,
  M11, M12 and M13, and M2 and M14 with a Low each (a stopped rerun still ending when shown again; two theme comments); solution-architect
  confirmed M8, accepted M4's and M7's deferrals, and found M1 not fully fixed for the same race, as a Moderate. Fixed: the stopped
  rerun's tiles run once it ends (`RerunWhenEndedAsync`), with a test that fails without the fix; the theme comments reworded and the
  rule widened to X.Y.Z after since and before.
- Second re-check: all four confirmed the rest. solution-architect: M1 confirmed (the race, its test, the closed snapshot's sets in
  process-model.md). support-engineer: M15 and the stopped-rerun Low confirmed; logging.md's order note made exact. gc-specialist: the
  collection comment confirmed accurate. senior-dev: M2 and M14 confirmed. Nothing High or Moderate remains open.

## Markdown sweep

After the fixes were confirmed, the tracked Markdown files were checked against what this sprint changed (the workspace model, the
result set store and protocol 25, the split view models, the comment rule and the test UI thread). Changed: ADR-029 (the store's cap
and the hidden sets, pointing to ADR-044), ADR-044 (the review's decisions), BACKLOG.md (P21 done; the 4.10 items),
design/refactor-analysis.md (the workspace candidate done), architecture/process-model.md (the bump rule names an added method; the cap;
closing drops sets), architecture/threading.md and code-map.md (the test UI thread), architecture/logging.md (the Workspaces lines),
DEVELOPER.md (the comment rule as enforced), USER-GUIDE.md (switching snapshots, a draft), design/info-text.md (the drafts), and the
senior-dev brief (the size and comment rules). CLAUDE.md (git-ignored) has both rules. Past review files and RELEASE-NOTES' past
sections are records and were left as written. No em or en dashes in the changed files.
