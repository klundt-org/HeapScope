# Sprint 4.10 review: Analysis tabs

Reviewed 2026-11-27 against sprint-4.9 by all four reviewers, once the 6 tasks were in. Findings that overlap are listed once, under the
highest severity. Moderates decided autonomously: fixed, or deferred where said.

## High

- H1 (support-engineer; gc-specialist, solution-architect, senior-dev, Moderate) The store tagged every new set of the shown snapshot
  with the tab shown, the inspections' too, though a snapshot's tabs share them. After a switch to another tab the whole inspections
  run counted as "another tab's" and was evicted before the shown tab's own sets, while the Inspections view was on screen; the
  eviction was logged at Debug. Fixed: the inspections' sets carry no tab (`Add(..., perTab: false)`), and the tab tier takes only a
  set with a tab that is not the one shown. The client tells the worker the tab before the tab's views query, one tell at a time and
  the latest shown. Tests: `What_the_tabs_share_stays_with_the_snapshot_across_a_tab_switch`, and the tab tier proven against least
  recently used order (M4). ADR-045's premise is corrected. Status: Fixed.
- H2 (support-engineer; senior-dev, solution-architect, Low) No line at the default level said which tab a step or a link landed in:
  "Workspace shown" logged the page tab, the path's lines named no tab, and the store's eviction line named neither tab. Fixed: every
  "Analysis path:" line names its tab ("in snapshot {id}, tab {tab}"), "Workspace shown" names the analysis tab with its place and the
  page tab, "Tab shown" names the page tab, a closed tab its place, and the eviction line the set's tab, the rule that took it and the
  tab shown. Status: Fixed.

## Moderate

- M1 (gc-specialist) The measurement could not show what tabs cost: it ran before tabs, touched one page per grid, and checked no
  release. Fixed: the smoke run opens the most tabs on a snapshot, pages each tab's largest grid of objects to the 50 pages a grid
  keeps, then closes the tabs and the snapshots, logging memory at each count; budgets.md records it (about 1.1 MB a tab, memory back
  on close) with a ceiling. Status: Fixed (decided autonomously).
- M2 (gc-specialist) `--smoke-more` counted slow operations two and three times, ran the memory's full collection inside a checked
  phase, and never checked the extra dumps' drills. Fixed: each pass begins its phases before opening, ends its drill and its
  inspections, logs memory outside them, and the ceiling counts the first dump's run only (the rest is a measurement, said in the
  log). Status: Fixed (decided autonomously).
- M3 (solution-architect) The tab strip showed on Compare and Inspections, where switching tabs changes nothing, and Ctrl+W closed a tab
  there unseen. Fixed: `ShowsTabStrip` (Snapshot and Retainers only) shows the strip and is New tab's and Close tab's CanExecute, so
  the shortcuts do nothing elsewhere. Test: `The_tab_strip_is_for_the_views_of_one_tab`. Status: Fixed (decided autonomously).
- M4 (senior-dev) The store test did not prove the tab tier: the other tab's set was the oldest anyway. Fixed: the other tab's set is
  newer than the shown tab's, and it goes first. Status: Fixed (decided autonomously).
- M5 (senior-dev) The restore of a cleared `CurrentStep` ran inside the generated setter, re-entrantly, not deferred as its comment
  said. Fixed: `CurrentStep` is a written property that ignores a null; the UI test checks the step stays selected. Status: Fixed
  (decided autonomously).
- M6 (senior-dev) Tab paths were untested: which sets a close releases, a tab switch mid-run, closing a tab not shown or the first one,
  closing a snapshot with tabs, an inspection row's link, Back in a hidden tab, the path's foreign guard. Fixed: a test for each in
  `AnalysisTabsTests` (the fake records the types grid's sets). Status: Fixed (decided autonomously).
- M7 (senior-dev) Task 1's memory line had no test. Fixed: `SmokeRunner.MemoryLine`, a constant the log uses, with a test of its
  wording. Status: Fixed (decided autonomously).
- M8 (senior-dev; support-engineer, solution-architect, Low) A late link of a snapshot closed meanwhile fell through to the shown tab,
  where `Retainers.Navigate` and `Record` took another snapshot. Fixed: `NavigateIn` hands a link of a snapshot not shown, or
  closed, to the page, which refuses it with why; `Record` refuses another snapshot's target. Test:
  `A_tabs_path_takes_no_step_of_another_snapshot`. Status: Fixed (decided autonomously).
- M9 (support-engineer) logging.md no longer matched the eviction levels and did not tell the page tab from the analysis tab. Fixed
  with H2. Status: Fixed (decided autonomously).

## Low

Listed only, not fixed.

- L1 (support-engineer) The cross-snapshot warning of a path sets no status, so a link that is refused there does nothing visible.
- L2 (support-engineer, senior-dev) Ctrl+W on the last tab says nothing (it is not executable now, M3).
- L3 (gc-specialist) budgets.md's worker memory with several snapshots open is not compared with a worker budget; the measurement
  found a third in-memory Medium refused by it on a 32 GB machine.
- L4 (solution-architect, senior-dev) `NavigatorViewModel` still keeps a path per snapshot and `Show(snapshot)`; its summary speaks of
  snapshots, not tabs.
- L5 (solution-architect) The scope over the pipe is tested only as accepted, not by its effect in the worker.
- L6 (solution-architect) BACKLOG.md's "the navigator's history at the Snapshots page level" item predates tabs.
- L7 (senior-dev) The tab's close button is a plain "x", not the `dismiss` style with an icon and an automation name.
- L8 (senior-dev) `SingleThreadUiDispatcher` watches the first empty analysis only after a switch, and re-subscribes a navigator per
  switch (the duplicate check keeps it harmless).

## Measurements and runs

- Before the review: Debug build 0 warnings; 2,380 tests, 2,353 passed, 27 skipped.
- After the fixes: Debug build 0 warnings; 2,388 tests, 2,355 passed, 27 skipped, 6 failed under the full parallel run and passed alone (the
  load cancel, the sample's CLI start, a lesson run, a live trace, the counters of LeakyApi, `StartFailureTests`; the load flakes in
  BACKLOG.md).
- The tab measurement (M1), budgets.md: two Medium snapshots, then 8 tabs on one, managed heap 61, 69, 45 and 35 MB as tabs and
  snapshots open and close.

## Confirmations

To be filled as each fix goes back to the reviewer that raised it.

## Markdown sweep

To be filled after the fixes are confirmed.
