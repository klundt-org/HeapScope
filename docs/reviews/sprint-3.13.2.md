# Sprint 3.13.2 review: Dump values in logs, by consent

Reviewers: solution-architect, senior-dev, gc-specialist, support-engineer (2026-11-19), on `sprint/3.13.2` against `develop`
(3.13.1). Each reviewer read the working tree (none had a shell or ran anything). Findings that overlap are merged and keep the
highest severity. Counts: 2 High, 13 Moderate (2 from the confirmations), 31 Low (16 from the confirmations).

Before the review: Debug build with 0 warnings; 1,825 tests, 20 failed in a run while the owner tried the app on the same
checkout and the solution was rebuilt mid-run. Of those, 2 were fixed (the one public type per file rule, the info text
count); rerun alone, all but 2 integration tests passed, and those 2 time the machine (a fixture build of 103 s, a page of 966
ms against 200 ms) on a loaded machine; CI's runner decides them.

The owner decided the Moderates' handling in advance (autonomy for Phase 4): each was applied as recommended, marked "decided
autonomously".

## High

### H1. Purge logs keeps the log that holds the values
- Reviewers: solution-architect, support-engineer.
- Where: `SettingsService.LogFiles`, `FileLoggerProvider`, the approved question and off status, ADR-043.
- What: the worker's lines go into the client's own log file, which Purge logs always keeps (the file being written). A user
  who follows the approved advice (turn it off, Purge logs, share today's log) shares the file that holds the values.
- Why it matters: privacy; the consent texts promise a cleanup that does not happen for the file most likely shared.
- Fix: turning dump values off starts a new part of the process's log (`FileLoggerProvider.StartNewPart`), and Purge logs
  deletes the process's own earlier parts and days, keeping only the part being written; other running HeapScope processes
  keep theirs until they close (ADR-043). The approved texts now hold for the process that changed the setting, so they stay.
  Test: `SettingsViewModelTests.After_dump_values_are_turned_off_purge_logs_deletes_the_part_that_held_them`.
- Status: Fixed; confirmation below.

### H2. Saving one setting over an unreadable file resets the other
- Reviewers: support-engineer (High), solution-architect, senior-dev (Moderate).
- Where: `SettingsService.SetIncludeFilterTextInLogs`, `SetIncludeDumpValuesInLogs`.
- What: both read with `HeapScopeSettings.Read`, which returns the defaults for a file that is there but cannot be read, then
  saved them: turning dump values on could silently turn filter text back on and drop a newer build's members.
- Fix: both read with `TryRead` and refuse to save over an unreadable file (an `IOException` the page and the CLI show).
  Test: `SettingsViewModelTests.A_settings_file_that_cannot_be_read_is_not_overwritten`.
- Status: Fixed; confirmation below.

## Moderate

### M1. An unreadable file kept dump values on
- Reviewers: gc-specialist, solution-architect, support-engineer. `LogPrivacy` kept the last values read once the file had
  been read. Fix: an unreadable file turns dump values off whatever was read before (filter text keeps the last value read),
  and raises `Changed`, which the worker logs. Test: `HeapScopeSettingsTests.Dump_values_go_off_when_the_file_cannot_be_read_and_filter_text_keeps_what_was_read`.
  Fixed (decided autonomously).

### M2. Rolled and next-day log files did not say what they hold
- Reviewers: solution-architect, support-engineer. Fix: every part's header says "logs hold filter text X, dump values Y".
  Fixed (decided autonomously).

### M3. The Settings page could show a stale state
- Reviewer: solution-architect. The command line can change `settings.json` while the app runs. Fix: the page reloads both
  settings each time it is shown (`SettingsViewModel.Reload`). Test: `The_page_shows_the_settings_saved_since_it_was_built`.
  Fixed (decided autonomously).

### M4. Tests named by tasks 3 and 10 were missing
- Reviewers: solution-architect, support-engineer, senior-dev. Fix: the worker's change line moved to `PrivacyLog` and is
  tested (`PrivacyLogTests`); `IsQuerying` is tested (`TypeSummaryViewModelTests`, three cases). Fixed (decided autonomously).

### M5. Values nested in struct fields and array elements, and the on path's edge cases
- Reviewers: gc-specialist, senior-dev. With values on, `ObjectFieldDto.Children` printed only type names; no tests for
  control characters, a cut inside a surrogate pair, boundaries, or a page of rows. Fix: records go four levels deep with
  values on, a line stops at 16 KB, a cut never splits a character; tests for each. Fixed (decided autonomously).

### M6. The command line's refusal applied the other option and was not logged
- Reviewers: support-engineer, solution-architect, senior-dev. Fix: `on` without `--yes` refuses the whole command before
  applying anything and logs it; `on` and `off` are logged as given. Test: `Dump_values_on_without_yes_changes_no_other_setting_either`.
  Fixed (decided autonomously).

### M7. logging.md described the old lines
- Reviewers: support-engineer, solution-architect. Fix: the Settings bullet names the real lines (saves, the question and
  answer, the refusal, the new part, the start lines, headers and the change line). Fixed (decided autonomously).

### M8. The consent question could be raced or fail unobserved
- Reviewer: senior-dev. Fix: a toggle while the question is open is reverted; a question that fails leaves the setting off
  and is logged. Test: `A_second_click_while_the_question_is_open_is_ignored_and_a_question_that_fails_leaves_it_off`.
  Fixed (decided autonomously).

### M9. The main Retainers grid had no busy bar
- Reviewer: senior-dev. Its first query computes the dominators, the longest wait. Fix: the bar is above the dominator tree
  too. Fixed (decided autonomously).

### M10. A test could see another test process's settings
- Reviewer: senior-dev. The CLI settings tests write the shared test data folder, and a Provenance test read it. Fix: the
  Provenance tests that assert on values pass the settings explicitly. The CLI tests keep the shared folder, as since 3.12;
  no test asserts on it implicitly now. Fixed in part (decided autonomously).

### M11. The new approved texts were not checked word for word
- Reviewer: senior-dev. Fix: `InfoIconTests.The_sprint_3_13_2_texts_the_owner_approved_are_the_ones_shown` and
  `SettingsCommandTests.The_refusal_is_the_one_the_owner_approved`. Fixed (decided autonomously).

## Low (listed, not raised)

- L1. `Provenance.Describe` read the two flags in two locks (applied anyway: `LogPrivacy.Read`).
- L2. A failure while describing could fail an answer (applied anyway: `Describe` names the type).
- L3. Change detection by write time alone can miss two saves in one tick (the CLI now saves at most once per option).
- L4. The busy bar moves the grid by 8 px and may flash on quick queries; an indeterminate bar while hidden.
- L5. Each quoted value allocates a substring and a serializer buffer.
- L6. The start lines and change line at Information are dropped from a log kept at Warning (the change line to "on" is a
  warning now).
- L7. A user-typed text under another property name than `Filter` would be written with values on (Phase 4 DTOs).
- L8. `security-privacy.md`'s duplicate strings line (updated).
- L9. RELEASE-NOTES had no tutorial bullet (added).
- L10. "From now on" holds within a second, not at once.
- L11. `SmokeRunner` logs the opened status where it logged "ok".
- L12. A failing turn-off save was not tested (added).
- L13. History in comments instead of the current reason; `OpenedStatus` placed among the dump values texts.
- L14. `LogPrivacy.Set`'s optional parameter (made required).
- L15. The first-read comment in `LogPrivacy` (rewritten).

## Confirmations

Each fix went back to the reviewer that raised it (2026-11-19):

- solution-architect: H1 fixed for its main case; M1, M2, M3 and H2 fixed; M4 partly (start lines untested, now Low). New:
  - Moderate (M12): a worker writes values for up to a second after dump values are turned off, and a change made by another
    process never rolls this log. Fix: every purge starts a new part first, after waiting until two seconds have passed since
    dump values were turned off; the purge tests check that the part being written goes too (`Purge_logs_...`,
    `Purge_all_...`). Fixed (decided autonomously).
  - Lows: the forgotten write time after an unreadable check (fixed: `SetUnreadable` forgets it), the header reading
    privacy under the logger lock, `Reload` with an unreadable file, a stale comment (fixed).
- support-engineer: H1 and H2 confirmed; all its Moderates confirmed. New: the same Moderate as the architect's (M12,
  fixed above). Lows: an unreadable file's change line looks like a choice; the off save line when no part is started; the
  lock order; the path named twice in one status. One approved text raised for the owner: the command line's off message
  ("heapscope-cli purge logs deletes them") is not true while the app is open; a new wording went to the owner.
- senior-dev: six Moderates confirmed, the tests one partly (the newer query test releases both at once; the start lines
  untested; both Low). New Lows: the answer line logged when the question failed, with no status; the Provenance summary
  (fixed); the check box label not compared word for word.
- gc-specialist: its three Moderates confirmed and its Lows applied. New Lows: the forgotten write time (fixed), a test
  that could not tell "kept" from "forced off" (fixed: filter text on in the test), stale comments (fixed).

- solution-architect, on the M12 fix: confirmed. New Moderate (M13): Purge all rolled after logging its start line, the
  question and answer and one line per session, so it deleted its own record. Fix: Purge all starts the new part before its
  first line, which says it was confirmed; the wait line is logged in the new part. Lows: the question's count leaves out
  the part closed first; the wait counts only this process's own turn-off.

- solution-architect, on the M13 fix: confirmed. New Low: "confirmed by the user" is not passed in by the caller, and the
  wait ignores cancellation.

No High or Moderate is open.

## Markdown sweep

After the fixes, every tracked Markdown file was checked against the code for what this sprint changed: the settings and
their command line, the log lines and headers, Purge logs and Purge all, the lesson steps, the busy bar and the open status,
and versions. Changed by the sweep: `docs/architecture/code-map.md` (`LogPrivacyChange`), `docs/DEVELOPER.md` (the settings
command and Purge logs), `docs/USER-GUIDE.md` (what Purge logs keeps), `docs/architecture/logging.md` and ADR-043 (the
purge-time part). No em or en dashes in any changed file.

## After the fixes

Debug build with 0 warnings. The fast tier passed. The integration tier, run as the gate runs it while the owner used the
machine, failed 15 tests: rerun alone, all passed but the page cost test (`PageCostTests`, 236 to 295 ms against 200), which
fails the same way on `develop` on this machine at the same time (245 and 320 ms): the machine, not the sprint. CI's runner
decides it.

## Open for the owner

- The command line's off message (an approved text) was not true while the app is open. The owner approved a new wording
  (2026-11-19): "Logs written while it was on still hold values read from dumps. Purge logs on the Settings page deletes them, or heapscope-cli purge logs once every HeapScope window is closed."
