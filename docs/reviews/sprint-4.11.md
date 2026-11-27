# Sprint 4.11 review: Sunburst

Reviewed 2026-11-27 against sprint-4.10 by all four reviewers, once the 6 tasks were in. Findings that overlap are listed once, under the
highest severity. Moderates decided autonomously: fixed, or deferred where said.

## High

- H1 (support-engineer; solution-architect, Low) A contested allocation chart could not be backed up from the log: the worker logged the
  10 widest segments of the whole chart, which in a call tree are the trunk, so most segments' bytes were in no line; a click on a
  segment whose row was loaded logged nothing, and the line of one that was not had no bytes; the tree query a click started logged
  the first rows, not the row revealed. Fixed: the worker's chart line gives each level's segments and bytes and its five widest by
  frame, key and bytes, with how many more; every segment clicked logs its key, frame, bytes and share of the chart; the tree's line
  names the revealed row itself (name, total, own). logging.md and ADR-046 say so. Status: Fixed.

## Moderate

- M1 (gc-specialist, senior-dev) A segment too short to draw, alone among its neighbors, was dropped without hiding what lay beyond it.
  In a sunburst a byte is longer further out, so its children could be drawn over an empty ring with no parent under them; in either
  shape tiny children of such a parent could become a "+N smaller" group under nothing. Fixed: `ChartSegments` hides what lies beyond
  a segment too short to draw, as it does for a group. Test: `What_lies_beyond_a_segment_too_short_to_draw_is_hidden_with_it` (both
  shapes). Status: Fixed (decided autonomously).
- M2 (senior-dev) While a new allocation chart loaded, the old one stayed drawn and clickable: after a kind switch a click put call tree
  keys into the back traces' open rows and set a false note; a row selected meanwhile kept a stale segment, and the new chart did not
  select the row's segment. Fixed: the chart is emptied when a new one is asked for, and on arrival the selected row selects its
  segment. Test: `While_a_chart_is_drawn_the_old_one_is_gone_and_a_row_selected_meanwhile_selects_its_segment` (a gated fake chart).
  Status: Fixed (decided autonomously).
- M3 (senior-dev) Closing a trace did not cancel its chart query, which went on in the worker and wrote into the closed view model, and
  a pending reveal key survived a clear. Fixed: the tree's clear stops and empties the chart (`ClearChart`). Test:
  `Closing_a_trace_stops_its_chart`. Status: Fixed (decided autonomously).
- M4 (senior-dev) The sunburst docked above the allocation tree at 240 pixels could leave the tree little or no height at the default
  window size. Fixed: the sunburst takes at most half the height it is given (both pages), so the tree keeps the rest. Status: Fixed
  (decided autonomously).
- M5 (senior-dev, gc-specialist) Tests missed the chart's edge cases: a chart arriving late, Close, recursion, merged nodes, the segment
  cap, the top total against the tree's under a filter, the sub-pixel parent. Fixed: the fake's chart query can be gated and counts
  its token; tests for each of those (`AllocationsViewModelTests`, `AllocationChartLayoutTests`, `SunburstGeometryTests`). Status: Fixed
  (decided autonomously).
- M6 (gc-specialist) The back-trace grouping now runs for the tree and the chart, and allocated a dictionary of every position at the
  first level (where none merges), a LINQ copy per caller list, and a 24-byte position. Fixed in part: the position is 16 bytes, the
  first level skips the merge lookup, callers are gathered in one presized pass, and the loops check the token every 64K. Deferred to
  the Backlog: measuring the tree and the chart together on the 600 s trace and a large synthetic one. Status: Fixed in part, rest
  deferred (decided autonomously).
- M7 (gc-specialist) Every draw re-ran the grouping and built a new geometry for every arc, on each selection, hover and theme change.
  Fixed: both charts keep their bars or arcs (and the sunburst its sector shapes) until the segments, the total or the size change; a
  selection only draws them again with other fills. Status: Fixed (decided autonomously).
- M8 (support-engineer; solution-architect and senior-dev, Low) The chart did not say what it left out, nor whether the 20,000-segment
  cap cut it. Fixed: `AllocationChart` and `AllocationChartResult` count the rows the drawn levels left out under the share with their
  bytes, and say `Truncated`; the worker's line logs both. Test: `What_the_share_leaves_out_is_counted_with_its_bytes`,
  `A_chart_past_the_segment_cap_is_cut_and_says_so`. Status: Fixed (decided autonomously).
- M9 (support-engineer; senior-dev, Low) Every worker failure of the chart was handled as a refusal: the chart vanished with no message
  and the log said "refused" at Information, though only the chart may have failed. Fixed: a refusal of the request itself
  (`InvalidArgument`, a trace being recorded) leaves the message to the tree; any other worker failure shows the worker's message with
  its next step and logs it as shown at Warning. Test: `A_chart_that_fails_alone_says_why_and_a_refused_request_leaves_that_to_the_tree`.
  Status: Fixed (decided autonomously).
- M10 (support-engineer) When the tree was cut at its row limit, a segment's reveal replaced the tree's note ("collapse rows") with
  "expand the list's more row", the wrong step. Fixed: a cut tree keeps its own note, and the log line says which case it was. Status:
  Fixed (decided autonomously).
- M11 (support-engineer) The drafts and the user guide did not say the allocation chart shows six levels, leaves out frames under 0.1
  percent with what lies below them, and has none for a trace being recorded. Fixed in the drafts (`InfoTexts.AllocationsChart`,
  `AllocationsSunburst`, info-text.md) and the guide's Charts bullet. Status: Fixed (decided autonomously).
- M12 (support-engineer) A chart with no frame reaching the share disappeared with its caption and choice, saying nothing. Fixed: it
  says "No frame reaches 0.1 percent of the sampled bytes; the tree below lists them all." and logs it as shown. Test:
  `A_chart_with_no_frame_wide_enough_says_so`. Status: Fixed (decided autonomously).
- M13 (support-engineer, from the re-check) The M10 fix had no test, and it rests on the tree's apply step running before its rows are
  applied. Fixed: `A_segment_missing_from_a_cut_tree_keeps_the_trees_own_note` (the fake tree can answer as cut). Status: Fixed
  (decided autonomously).
- M14 (gc-specialist, from the re-check) The M8 tally kept a dictionary entry for the parent of every narrow stack node the interval
  reached, at any depth: tens of MB on a large trace, against M6's trimming. Fixed: the call tree keeps a set of the parents it laid
  out (at most the segments drawn) and counts their narrow children in one pass after; the cap breaks out of the loops instead of
  returning, so the tally still runs. Status: Fixed (decided autonomously).

## Low

Listed only (the review process fixes no Low).

- L1 (solution-architect) A segment whose row the tree lists behind a "more" row is not opened; the chart's order is the tree's, so the
  batches could be computed and opened too. ADR-046 now states the limit.
- L2 (solution-architect) `IcicleSegmentDto` is the shared chart contract but lives in DominatorDtos.cs with the dominator meaning of its
  fields; a `ChartSegmentDto` of its own file, documented per subject.
- L3 (solution-architect) The chart limits (levels, share, segments) are literals in two handlers and Analysis, not a `ChartLimits` in
  Contracts; no test of the two refusals over the pipe.
- L4 (solution-architect) The answer is sent whole; at the defaults it holds at most about 6,000 segments, at the widest limits more.
- L5 (solution-architect) The two chart hosts repeat the shape, caption, sync and load parts; extract a chart part when a third arrives
  (the 2.5 review's M5 backlog item); bind the shape as an enum.
- L6 (solution-architect) `IAllocationTraceService`'s summary and `OpenLiveTraceAsync`'s refusal doc do not name the chart.
- L7 (senior-dev) The internal `ChartSegments` class shares its name with `AllocationTreeViewModel.ChartSegments`.
- L8 (senior-dev) IcicleGeometry.cs, a listed two-type file, was changed without moving `IcicleBar` out.
- L9 (senior-dev) The selection guard is reset without try/finally; a click on the allocation chart's top keeps the old row selected;
  `FindLoadedRow` and the sync repeat the Retainers tab's.
- L10 (gc-specialist, senior-dev) The call tree's subtree sums are pooled now, but its children are kept for every node over the
  share at any depth, not only the levels drawn; the cap cuts in breadth-first parent order.
- L11 (gc-specialist) `AllocationChartSegment` pads to 40 bytes; a row selection scans the chart's segments linearly.
- L12 (gc-specialist) The tree and the chart each run the interval's samples (`AllocationProfile.Own`); a cache of the last filter's
  would share it.
- L13 (support-engineer) The worker's level and share refusals give no next step, and a back traces chart is refused as having "no call
  tree".
- L14 (support-engineer) A double-click on the sunburst logs "focus on the icicle's"; the shape line names no snapshot or tab.
- L15 (support-engineer) The allocation sunburst's center shows an exact-looking size without "About", and the top's hover says
  "allocated through it".
- L16 (support-engineer) The docs promised the widest segments by level before the H1 fix (now true).
- From the re-checks: L17 (senior-dev) emptying the chart while a new one is drawn hides its header and moves the tree up for the
  query's length; L18 (senior-dev, support-engineer) an OperationCanceledException not of the chart's own is shown as its bare message;
  L19 (senior-dev) a TraceNotLoaded failure is said on both the chart and the tree; L20 (senior-dev) no test of a click on the
  chart's top, or of a "more" row clearing the segment's selection; L21 (support-engineer) info-text.md's row for a chart failure
  does not split the worker's message (with its id) from the app's own; L22 (support-engineer) the allocation info texts word the back
  traces' first row as "allocated", the captions as "the methods that allocated"; L23 (support-engineer) LeftOut counts what the
  share left out within the drawn levels, not what lies below the last; L24 (gc-specialist) a back traces chart cut at the cap still
  groups the rest of its level; each resize still runs the grouping's GroupBy and OrderBy; the positions' lists still grow by
  doubling; no test of the back traces' cap.

## Measurements and runs

- Builds: Debug and Release, 0 warnings. Tests after the fixes: Analysis 200, UI.ViewModels 386, UI 116, Worker 329, Architecture 83,
  the allocation trace integration tests 22 (the chart checked segment by segment against the real tree's rows).
- Not measured: the allocation tree and its chart together on a large trace (M6, Backlog).

## Confirmations

Each fix went back to the reviewer that raised it, who re-read the code and tests:
- support-engineer: H1, M8, M9, M10, M11, M12 Confirmed (privacy unchanged); raised M13 and four Lows.
- gc-specialist: M1, M5, M6, M7 and the layout rewrite Confirmed; raised M14 and two Lows.
- senior-dev: M1, M2, M3, M4, M5, M7 Confirmed; raised three Lows.
- solution-architect: no High or Moderate.
- Second round: M13 Confirmed by support-engineer (with a Low: the test could also check the reveal key and the log line), M14
  Confirmed by gc-specialist (the back traces' cap still groups the rest of its level, Low L24).

## Markdown sweep

Every tracked Markdown file was searched for what this sprint changed (the charts, the icicle, the call tree, protocol 26 and 27,
test and text counts) and read where it matched. Changed for the sprint: ARCHITECTURE.md (scope), DEVELOPER.md (the chart tests),
USER-GUIDE.md (Charts, a draft), SPRINTS.md, BACKLOG.md (the measurement and the CLI's allocation trees), adr/README.md and ADR-046,
architecture/code-map.md, logging.md and process-model.md (version 27), design/info-text.md (Sprint 4.11 additions). The sweep found
nothing else stale: README's line on the icicle chart still holds (the Retainers tab opens on it), the reviewer briefs and skills name
no chart, and past review files and RELEASE-NOTES' past sections were left as written. No em or en dashes in the changed files.

## Gate at close

Pass on 2026-11-27: Debug and Release with 0 warnings, 2,417 tests, the fixture capture (the known answers run in CI).
