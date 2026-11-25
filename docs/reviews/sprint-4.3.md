# Sprint 4.3 review: Live session

Reviewed 2026-11-25 against sprint-4.2.1 by all four reviewers. Gate before the fixes: pass, 0 warnings, 2,054 tests. Findings that
overlap are listed once, under the highest severity. Moderates decided autonomously.

## High

- H1 (all four) A chosen interval was snapped once; when stretches doubled (30 minutes, or 400,000 entries) it counted nothing, or
  another interval than it said. Fixed: the worker counts whole stretches of the view it answers from and says which interval and
  stretch that was; the page shows it. Tests: `The_interval_shown_is_the_one_the_worker_counted_when_stretches_grew`,
  `An_interval_of_a_live_trace_is_counted_in_whole_stretches`, and the query in `What_is_not_live_...`. Status: Fixed.
- H2 (solution-architect, senior-dev) A finished or failed run's live trace was never closed in the worker; a failed one kept its
  thread and reservation. Fixed: however following ends, the worker is told. Tests: in `A_run_is_followed_...` and
  `A_worker_that_stops_is_asked_again_...`. Status: Fixed.
- H3 (solution-architect, senior-dev) After a run ended, a worker restart left partial figures for good. Fixed: following again reads
  what the file holds before anything is answered, checks it is the same recording, and carries that it ended. Test:
  `Choosing_an_interval_after_the_end_still_answers`, `A_recording_is_followed_...`. Status: Fixed.
- H4 (senior-dev) A choice around the end could be lost or answered out of order. Fixed: one loop until the run is replaced, choices
  counted, answers to an older one dropped. Tests: `A_choice_made_while_the_last_figures_arrive_...`,
  `A_choice_is_asked_for_again_when_the_worker_stopped_at_it`. Status: Fixed.
- H5 (support-engineer) Nothing at Information said as of when a shown figure was, nor the delay. Fixed: the interval line has
  samples, seconds, stretch and top types; the stacks line as of when; the worker's minute line is at Information with the longest
  delay; a delay or note is logged when it appears or goes. Status: Fixed.
- H6 (support-engineer) An error while recording deleted the live file without a word. Fixed: what was recorded is kept, and said.
  Test: `A_recording_that_ends_by_an_error_is_told_to_whoever_follows_it`. Status: Fixed.

## Moderate

- M1 (gc-specialist, from knowledge of the CLR; confirmed here by a failing test) The recording's counter each second overrode a longer
  interval of the run. Fixed: the run's own interval. Test: `A_recording_leaves_the_run_s_interval_as_it_is`.
- M2 (support-engineer, senior-dev) A worker that stops at each question was restarted without end; one that stopped while the trace
  opened was not asked again. Fixed: three times without reading more, the open included, then a message.
- M3 (three) Lost events were 0 while recording. Fixed: read with each event.
- M4 (solution-architect, senior-dev, gc-specialist) The limits did not bound memory: frames and addresses past the node limit, type
  text, names. Fixed, measured at the limits (75 MB, 98 MB with two views), 192 MB reserved. Test:
  `What_is_kept_is_bounded_however_many_addresses_and_types_there_are`.
- M5 (gc-specialist, senior-dev) A view per question, a walk of everything at each poll, stacks folded again for each method compiled.
  Fixed: a view answers for a second, the unnamed count is kept until something changes, folding at most every 2 s.
- M6 (gc-specialist) A code range reused by another method could keep the old name. Fixed: a method over others takes their place, an
  unloaded one is gone, and what is left out is logged.
- M7 (gc-specialist) The names session was not gated on captures, and a rundown larger than its buffer was partial without a word.
  Fixed: not while a capture runs; the buffer of the recording; lost events are said.
- M8 (gc-specialist) Unnamed frames were never said. Fixed, the text a draft for the owner.
- M9 (solution-architect) End could be lost in a canceled reopen, and the identity was checked only by one question. Fixed with H3.
- M10 (solution-architect) Opening read the names before it answered, one sorted insert at a time. Fixed: on the reader's thread,
  sorted once.
- M11 (solution-architect) A live call tree's keys would not hold. Fixed: refused, and the ADR says so.
- M12 (solution-architect) Threading, memory and budgets pages lacked live traces. Fixed.
- M13 (senior-dev) A drag on a growing timeline started elsewhere than pressed. Fixed: the press is kept in seconds.
- M14 (senior-dev) "Waiting for the first allocation sample" stayed on a run without samples. Fixed, the text a draft for the owner.
- M15 (senior-dev) The bursts test and lesson depended on where a burst fell. Fixed: two and a half seconds long, and more of both.
- M16 (senior-dev) Missing tests: added with the above; an event of a replaced run after its successor started is the existing
  `ReferenceEquals` check, as for samples.
- M17 (support-engineer) End, stop and open lines lacked how, names, path and numbers. Fixed.
- M18 (support-engineer) The recording's end wrote no line. Fixed. A process that exits leaves no rundown, so the finished trace may lack
  names the live view had: logging.md says so; keeping the names for it is in the Backlog.
- M19 (support-engineer) No activity joined the lines. Fixed: one per run followed, one per stacks question.
- M20 (support-engineer) Notes, refusals and the end's numbers were not logged as shown. Fixed.

## Low

Fixed in passing: the stacks' token source (senior-dev L1), `_updating` in a finally (L2), the close's other errors (L3), the
comments on a failed recording (L4, L5), a past run's refusal over a run that started (L6), indentation and usings (L8); the names
session's unobserved copy, closing off the RPC thread, methods left out in the log (solution-architect); the process's name per
sample (gc-specialist L2), sorting (L4), names let go at the end (L6, in part), a floor on stretches (L7); the new id of a reopened
trace and both starts (support-engineer). Not fixed: the delay across a clock step or suspend (gc-specialist L1); a 32-bit target's
stacks (L3); the counters' share of the trace limit in the guide (L5); the command line named in a refusal the Live page can show
(senior-dev L7); "Close this trace and open it again" on the Live page, and no next step for a file that cannot be opened
(support-engineer).

## The confirmations

All four checked c2458a7. H1 to H6, M1 to M3, M5 to M14 and M16 to M20 confirmed (M17's end wording and M4, M15 not), and these found:

- N1 High (all four) A view answers for a second, but the page took the trace's version for what it showed, so final types could miss a
  second of samples. Fixed: the answers say the version, seconds and stretch they are of. Test:
  `Figures_a_little_older_than_the_trace_are_asked_for_again`.
- N2 High (senior-dev; solution-architect, Moderate) After following stopped, a drag labeled the old figures with a new interval. Fixed:
  the interval the figures are of stays, and the stacks say why there are none. Test: in `A_worker_that_stops_each_time_...`.
- N3 High for a crafted trace (solution-architect; gc-specialist, senior-dev, Moderate) Past the entry limit with many types, stacks were
  given up again at every sample, and entries grew. Fixed: once, and then the time is given up too. Test:
  `Many_types_over_many_stretches_give_up_the_stacks_once_and_then_the_time`.
- N4 Moderate (gc-specialist, senior-dev) After too little memory, the last renaming could fail again and end the worker. Fixed: only after
  a clean end, guarded.
- N5 Moderate (solution-architect) A worker that stopped while it caught up ended the following. Fixed: it counts as a stop.
- N6 Moderate (support-engineer) The note's line was written at each change of a number. Fixed: when which notes are shown changes.
- N7 Moderate (support-engineer, M17) The end line called a whole recording stopped early. Fixed: by what was read.
- N8 Moderate (senior-dev, M15) The bursts were counted in ticks, so a loaded machine could turn the lesson's answer. Fixed: by time.
- Low, fixed: the longest delay per line, stacks and names said when known, the give-up text, the unnamed note at the end, lost events
  at the tail, methods left out of a names file, frame text bounded (solution-architect N5), the stacks' answer no longer moves the
  interval (gc-specialist N7), the interval test, a recording once started is never deleted. Not fixed: overlaps in a names file
  resolved by address; names one by one at the end when none were recorded; a capture that starts during the names session.

The second round (cafa77a) was checked by senior-dev and solution-architect: N1, N3 to N5 and N8 confirmed. N2 was not whole: a choice pending
when following stopped kept its label and its stacks, and another type's stacks stayed listed. Fixed; tests:
`A_choice_that_is_never_answered_does_not_name_the_figures_shown`. Names given to unnamed frames count toward the text limit now
(solution-architect, Low).

## Markdown sweep

Every tracked Markdown file but the records (2026-11-25). Changed by the sweep: ARCHITECTURE.md, architecture/code-map.md, USER-GUIDE.md,
RELEASE-NOTES.md (with the CLI package's notes, drafts for the owner), SPRINTS.md and sprints/phase-4.md. Changed with the sprint: ADR-037,
architecture/{budgets,logging,process-model,security-privacy,threading,worker-memory}.md, DEVELOPER.md, BACKLOG.md,
design/info-text.md. No package or license changed. No em or en dashes in changed files.
