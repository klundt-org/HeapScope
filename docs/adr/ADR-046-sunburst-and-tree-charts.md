# ADR-046 The sunburst and the tree charts (added in sprint 4.11)

- Context: the Retainers tab drew the dominator tree as an icicle (ADR-027; the research chose it over dotMemory's sunburst as
  simpler to draw and label, with a sunburst "as an optional toggle later"). The allocation trees, the call tree and back traces,
  had no chart at all, so the peer review of 4.5 (M16) found a sunburst planned "for dominators and call trees" with no layout for
  call trees. A sunburst shows the same tree as rings: its shares read as angles, and a deep, narrow branch stays visible further
  out, where an icicle's bar of the same bytes is as thin at every level.
- Decision:
  - **One set of segments, two shapes**: a chart is a list of segments (`IcicleSegmentDto`: level, offset, width, key, name,
    address), the top as level 0 and each level's children side by side inside their parent, as wide as their bytes. The icicle
    draws a segment as a bar; the sunburst (`SunburstGeometry`, `SunburstChart`) draws the top as the center disc and each level as a
    ring of equal width, a segment as an arc whose angle is its share of the top. The shape is the client's: switching it asks the
    worker nothing. Which segments are drawn alone and which are one "+N smaller" group is decided by the client for both shapes
    (`ChartSegments`), from each segment's length on screen: a bar's width, an arc's length along the middle of its ring, so a
    sunburst groups sooner near the center than further out. A segment too short to draw hides what lies beyond it, as a group
    does, so no arc is drawn with no parent under it.
  - **Both trees chart**: the Retainers tab draws the dominator tree's icicle segments either way (no new dominator query). The
    worker lays out an allocation tree for a chart (`AllocationChartLayout`, `QueryAllocationChartAsync`, protocol 27): all the
    interval's sampled bytes as the top, the call tree from the outermost frames or the back traces from the allocating frames, by
    default six levels and segments under 0.1 percent of the top left out with what lies below them (the worker accepts 1 to 20
    levels and 0.0001 to 0.5, as for the icicle), in the tree's own order. The answer counts the rows its drawn levels left out, with
    their bytes, and says when the 20,000-segment cap cut it. Its keys are the
    tree's row keys (`AllocationTreeKeys`), so a segment finds its row: a click opens the segment's parents in the tree and selects
    its row, and a selected row selects its segment. A row the tree lists behind a "more" row (past a list's first 100) is not
    opened: a note says to expand that row. The allocation chart has no focus of its own: the tree already narrows by
    type and interval, and its parents are opened, not focused. The back traces share their grouping with the tree's rows
    (`BackTraceGroups`), so a chart's bytes are the tree's.
  - **Choice and words**: each Retainers tab and the Allocations view choose Icicle or Sunburst, start at the icicle, and keep the
    choice while open (not across restarts). The rings carry no words: hover names an arc in the chart's terms (`ChartTexts`:
    kept alive for the dominator tree, allocated through it for the allocation trees), and the center names the focus. Texts are
    drafts for the owner.
- Consequences: a live trace has no chart, as it has no tree (its stack nodes are numbered anew). The CLI has no chart: its dominator tree (`retainers`) is
  text, and it has no allocation call tree or back traces yet (Backlog). A chart of many small frames is mostly groups at the default size; the tree below lists them. Screenshots are
  unchanged. The worker logs each allocation chart with each level's count, bytes and five widest segments, what it left out
  and whether it was cut, and the client logs each segment clicked with its bytes and share, so a contested chart is backed up by
  the bytes per frame (logging.md).
