# Sprint 4.1.1 review: Hide processes HeapScope cannot capture

Reviewers: solution-architect, senior-dev, gc-specialist, support-engineer (2026-11-20), on `sprint/4.1.1` against the tag
`sprint-4.1`. Each reviewer read the working tree (none had a shell or ran anything). Findings that overlap are merged and
keep the highest severity. Counts: 1 High, 5 Moderate, 25 Low (3 from the confirmations).

Before the review: Debug build with 0 warnings; the fast tier passed (1,561 tests, 1,545 passed, 16 skipped). The owner
decided the Moderates' handling in advance (autonomy for Phase 4): each was applied as recommended, marked "decided
autonomously". The sprint's texts are drafts for the owner (docs/design/info-text.md, "Sprint 4.1.1 additions").

## High

### H1. The Live page hid processes and logged nothing
- Reviewers: support-engineer (High), senior-dev, solution-architect, gc-specialist (Low).
- Where: `LiveViewModel.RefreshAsync`; the Processes page's own line in `ProcessesViewModel`.
- What: Live's chooser applies the rule but no line said which pids it hid; on Windows the "Listed" line carries no
  elevation data at all, so "my process is not in Live's list" could not be backed up.
- Why it matters: a contestable result the log cannot back up.
- Fix: one method on the shared rule, `ProcessListFilter.Log`, used by both pages: "The {page} page lists {shown} of {count}
  processes; '{box}' is checked|unchecked; hidden as needing more rights than HeapScope has: {pids}" (200 at most, then a
  count), after a listing when that changed since its last line (a page is listed each time it is shown, so repeats are not
  logged) and on every toggle. Tests: `ProcessListFilterTests.The_log_says_what_a_page_hides_when_that_changes_and_at_every_toggle`,
  `A_list_where_nothing_needs_more_rights_logs_nothing_until_a_toggle`,
  `ProcessesViewModelTests.The_page_logs_the_pids_it_hides_and_each_toggle`.
- Status: Fixed and confirmed (Confirmations below).

## Moderate

### M1. A toggle was not logged, and Windows' list line had no elevation
- Reviewers: support-engineer. Fix: every toggle logs its line ("(just changed)"); the "Listed" line says on Windows
  whether HeapScope runs elevated (Linux already gives the effective user ids). Fixed (decided autonomously).

### M2. logging.md did not describe the new lines
- Reviewers: support-engineer (Moderate), solution-architect (Low). Fix: a "Hidden processes" entry in section 11. Fixed
  (decided autonomously).

### M3. No test pinned that a process whose rights are unknown is never hidden
- Reviewers: gc-specialist. Fix: `ProcessListFilterTests.A_process_whose_rights_are_unknown_is_never_hidden` and, through the
  real mapping, `CaptureExplanationTests.A_process_whose_rights_are_unknown_never_needs_more_so_no_list_hides_it` (HeapScope
  elevated or not). Fixed (decided autonomously).

### M4. The sample's mark could come back after a toggle
- Reviewers: senior-dev. `MarkSamples` updated the visible rows and the rule's copy separately, untested. Fix: it updates
  every listed row in the rule and rebuilds the shown ones, so hidden and shown rows agree; the sample test toggles the box
  after the exit. Fixed (decided autonomously).

### M5. No test checked the hide line
- Reviewers: senior-dev. Fix: with H1's tests. Fixed (decided autonomously).

## Low (listed, not fixed unless noted)

- L1 (all four): the Windows note said "elevated processes" where the banner and tooltip say "elevated or as another user"
  (the probe marks both). Changed in the draft for the owner: "{N} processes that run elevated or as another user are
  hidden ..."; the label stays "Show elevated processes" as the owner named it, and the note now quotes the label from
  `ShowHiddenOn` so they cannot drift.
- L2 (support-engineer, senior-dev): on the Live page the note says HeapScope "cannot capture" them; Live watches.
- L3 (support-engineer): the Processes and Live info icons do not say that some processes are hidden by default.
- L4 (support-engineer): "Sample {pid} started but is not among the {count} processes listed" counts the shown rows.
- L5 (support-engineer, senior-dev, solution-architect): the tooltips and the Linux singular note are not checked word for
  word, though info-text.md says they are.
- L6 (senior-dev): toggling the box during a Live run can clear the chooser while the run goes on.
- L7 (senior-dev): `StartSampleAsync` says "Press Refresh" for a sample that would be hidden (cannot happen today: the
  sample runs as the user).
- L8 (senior-dev, solution-architect): the screenshot run writes made-up rows straight into the list, past the rule, with
  the old marks, so the picture can show a note beside a row it calls hidden.
- L9 (senior-dev): the check box appears and disappears between Refresh and the page's main button, so controls can shift.
- L10 (senior-dev, gc-specialist, solution-architect): `Visible` returns the rule's own list when shown, `Hidden` is built
  on each read, and both pages copy their `ShowRows` (the rule's presentation could move into one place).
- L11 (senior-dev): comments carry sprint ids beyond the guards DEVELOPER.md 4 allows; full `RightsTexts` names where a
  using exists; a test message on the wrong assertion.
- L12 (gc-specialist): ADR-014 and the Linux column tip say root can capture them all; a full dump of another user's process
  into HeapScope's folders is refused (4.1 review H1), a gcdump and Live work.
- L13 (gc-specialist): on Windows an elevated HeapScope may still not reach another standard user's process (the runtime's
  pipe security); unverified, to check once with a second local user.
- L14 (gc-specialist): on Linux, "has the rights" is root only (not `CAP_DAC_OVERRIDE`), and a process that drops its user
  after starting keeps its first user's socket.
- L15 (gc-specialist, solution-architect): allocations per refresh (records built twice per row, item-by-item adds).
- L16 (gc-specialist): Live's refresh does not catch `IOException` as the Processes page does.
- L17 (solution-architect): ADR-014's title and index did not say "amended" (fixed with the sweep).
- L18 (solution-architect): the new texts use `bool linux` too, so macOS would get the Windows words (with 4.1's M6, sprint 4.2).
- L19 (solution-architect): `ElevationSuffix` shows ", Unknown" after a name in the Live chooser.
- L20 (solution-architect): the two pages' boxes are independent (consistent with "kept while the app runs").
- L21 (solution-architect): the grid's CSV export holds the shown rows only.
- L22 (support-engineer): the user guide does not say the box is off at each start.

From the confirmations (listed, not fixed):

- L23 (support-engineer): no test checks the "HeapScope elevated: yes|no" part of the Windows "Listed" line.
- L24 (support-engineer): a page's line after a listing is skipped when nothing changed, so a new log part (after Purge logs
  or turning dump values off) can hold "Listed" lines without a page line after them.
- L25 (senior-dev): no test lists more than 200 hidden pids, so the "and N more" tail is untested.

## Confirmations

Each fix went back to the reviewer that raised it (resumed with the finding and the changed files); all read the code and
ran nothing.
- support-engineer: H1, M1, M2 and L1 Confirmed (L22 as listed), with L23 and L24.
- gc-specialist: M3 Confirmed (both tests would catch an unknown process being hidden).
- senior-dev: M4 and M5 Confirmed, with L25.
- solution-architect: the review changes Confirmed for layering and shape (the shared log method, the Windows list line,
  the composed note, ADR-014's title), nothing new.

## Markdown sweep

Every tracked Markdown file that names the process list, the Processes or Live page, elevation or the Elevation column was
read against the code. Changed: `docs/USER-GUIDE.md` ("Processes you cannot capture"), ADR-014 (amended for 4.1 and 4.1.1,
its title and index line), `docs/architecture/code-map.md` (`ProcessListFilter`), `docs/architecture/logging.md` (the
hidden processes' lines), `docs/design/info-text.md` (the drafts, and what the tests check), `docs/SPRINTS.md`. INSTALL.md
("lists the .NET processes you can capture") holds as it stands. No em or en dashes in the changed files.
