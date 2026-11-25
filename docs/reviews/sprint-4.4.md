``# Sprint 4.4 review: Allocated between two snapshots
``
``Reviewed 2026-11-25 against sprint-4.3 by all four reviewers. Gate before the fixes: pass, 0 warnings, 2,119 tests. Findings that overlap
``are listed once, under the highest severity. Moderates decided autonomously: all fixed.
``
``## High
``
``- H1 (solution-architect, senior-dev, support-engineer) A run that still records was taken for one that ended, since its record has its
``  trace's name from the start: the growing file was read, and its tab reused after the run. Fixed: only a run that ended; a tab whose
``  file changed since is read again. Tests: \`A_run_that_still_records_is_not_taken_for_one_that_ended\` (a real run),
``  \`Each_reason_is_said\`, \`A_trace_that_changed_since_it_was_read_is_read_again\`. Status: Fixed.
``- H2 (senior-dev; solution-architect, Moderate) A question asked again while the trace is read started another read, whose trace nobody
``  could close. Fixed: reads in progress are shared by path. Test: \`A_trace_is_read_once_however_often_the_question_changes_meanwhile\`.
``  Status: Fixed.
``- H3 (support-engineer) A refusal did not say which check refused, nor the log. Fixed: \`AllocatedBetween.Evidence\`, logged by both front
``  ends. Tests: \`Each_reason_is_said\` and its neighbors. Status: Fixed.
``- H4 (support-engineer) The log did not say where the heaps' times came from. Fixed: at capture (\`HeapTime.Source\`) and in the evidence;
``  a full dump without its heap's time is said too. Tests: \`The_heap_s_time_is_the_dump_s_last_write\`,
``  \`A_heap_whose_time_was_not_recorded_is_marked\`. Status: Fixed.
``- H5 (gc-specialist) A gcdump's row took the bytes of another type of its name, when a collection left objects of one only. Fixed: a name
``  the whole trace has for several method tables goes to no row, and the note says when rows are matched by name. Tests:
``  \`A_name_the_trace_has_for_two_method_tables_goes_to_no_gcdump_row\`, \`Names_of_several_method_tables_are_found_in_the_whole_trace\`.
``  Status: Fixed.
``
``## Moderate
``
``- M1 (gc-specialist) A reused method table was joined to the wrong type although the names told. Fixed: by the name without generic
``  arguments, on both sides. Test: \`A_method_table_of_another_type_is_not_joined_to_it\`.
``- M2 (gc-specialist; the clocks from knowledge) A machine that slept, or a drifting clock, shifted the interval without a word. Fixed: a
``  run records the shift, and the note says it from a second on.
``- M3 (three) A closed trace, or one that could not be read again, broke the comparison's rows. Fixed: one kind of failure, the
``  comparison without allocations, and closing the tab unticks the box with the reason. Tests: in \`A_row_opens_the_allocations_page_...\`,
``  \`A_resource_that_cannot_be_reopened_is_told_apart\`.
``- M4 (three) \`diff --allocated\` read the trace without progress or cancellation. Fixed.
``- M5 (solution-architect) A run whose end was never recorded matched every later pair. Fixed: the latest that ended. Test:
``  \`A_later_run_that_ended_is_taken_before_one_whose_end_was_not_recorded\`.
``- M6 (senior-dev, support-engineer) A session that cannot be read was said as snapshots being elsewhere. Fixed. Test:
``  \`A_session_that_cannot_be_read_says_why\`.
``- M7 (senior-dev) A test read the worker's log line without waiting for it. Fixed.
``- M8 (senior-dev) The command line's refusal was not what its test ran into. Fixed.
``- M9 (senior-dev) The worker's rules about trace and interval had no test. Fixed: \`DiffAllocationsTests\`.
``- M10 (senior-dev) No view test for the box, columns and menu. Fixed.
``- M11 (support-engineer) Shared names and differing method tables were logged as sums. Fixed: the largest five.
``- M12 (support-engineer) A gcdump whose types have no name was not told apart; the flush was logged at Debug. Fixed: counted, said in the
``  note and logged; the flush at Information.
``- M13 (support-engineer) The step to the Allocations page did not say when it shows every type, nor log the row. Fixed.
``- M14 (support-engineer) Refusals in the worker had no figures or next step, and the sort's fallback no line. Fixed.
``
``## Low
``
``Fixed in passing: an unrecorded heap time's text; a canceled or unread trace logged as said, with words; the runnable \`Where:\` line;
``the logging page; where the texts say "on the Live page"; the damage note; task 6's text; the ADR index; process-model.md's hidden column;
``budgets.md's bytes per sample; the note when still reading; the notes that stayed after a snapshot closed; token checks; the last event
``of a recording; "may be low"; the first heap node of the dump's own collection; the dictionary lookups; the edges in the user guide.
``Not fixed: \`DiffAllocations\` carries a service (solution-architect); the flush checked on .NET 10 only (Backlog).
``
``## The confirmations
`
`All four checked bc0d11d. H1, H3 to H5, M1, M2, M4 to M12 confirmed; H2, M3, M13 and M14 in part. Found then, and fixed:
`
`- N1 Moderate (solution-architect, senior-dev; the rest of H2) A read nobody waited for any more ended as an open trace without a tab.
`  Fixed: the tab comes with the trace. Test: `A_read_that_is_not_waited_for_gets_its_tab`.
`- N2 Moderate (senior-dev; solution-architect, support-engineer, Low; the rest of M3) A trace closed after the worker answered, or that a
`  page could not read again, left rows tied to it. Fixed: the box is unticked with the reason.
`- N3 Moderate (support-engineer; gc-specialist, senior-dev, Low; the rest of M14) Two snapshots of one moment were refused with a negative
`  time. Fixed, with words of its own. Test: in `An_interval_outside_the_recording_is_refused`.
`- N4 Moderate (support-engineer; the rest of M13) What the Allocations page shows more than the row was said on the page one left. Fixed:
`  on the Allocations page, and for nested types. Test: `A_nested_type_s_row_says_that_the_page_shows_every_type_of_its_name`.
`- N5 Moderate (support-engineer) The evidence described the latest runs, not those that matter. Fixed: fitting ones and the process's
`  nearest first, and the run taken.
`- Low, fixed: an exact name before one of other arguments (gc-specialist); the clock shift's test (gc-specialist); shared names found once,
`  for a gcdump only (gc-specialist); a second look under the lock and the unobserved failure of a read (solution-architect, senior-dev);
`  the note that stayed for another pair (senior-dev, support-engineer); set-aside entries counted, a failed record's heap time logged, the
`  unreadable session's words, "not found" beside unnamed types, a received note kept to one short line (support-engineer);
`  process-model.md and budgets.md (solution-architect). Not fixed: the session keeps no source of a heap's time; a file looked at on
`  the UI thread per question.
`
`## Markdown sweep
``
``(after the confirmations)
