# ADR-045 Analysis tabs (added in sprint 4.10, amends ADR-044)

- Context: ADR-044 gave each open snapshot one workspace, so following two leads in one dump still meant leaving one: going
  back along the path to branch, or losing a filter and a sort to look at something else. dotMemory opens analyses side by side
  in tabs. The 4.9 review (M4, M6, M7) left three things for tabs to settle: what several analyses of a snapshot cost the front
  end, how the worker prefers the tab on screen when its siblings share the snapshot, and where a link lands when a snapshot has
  more than one tab.
- Decision:
  - **A snapshot's analysis has tabs**: `SnapshotAnalysisViewModel` holds a snapshot's tabs, each a `SnapshotWorkspaceViewModel`
    with its own types grid, path and retainers, and one `InspectionsViewModel` they share, so the inspections run once per
    snapshot. A new tab starts at the types grid; the page shows a snapshot's current tab and keeps it when another snapshot is
    selected and this one again. At most 8 tabs per snapshot, a ninth refused with why; the last tab stays (closing the snapshot
    closes it). Closing a tab disposes it and releases its sets. Tabs are not kept across restarts. New tab and Close tab are
    Ctrl+T and Ctrl+W (Cmd on macOS).
  - **Links land in their tab**: a tab is the navigator of its own views, so a link of its snapshot lands in the tab it came
    from, and one into another snapshot goes to the page, which shows that snapshot's current tab. The inspections, shared, send
    their links to the tab shown. A tab's path holds steps of its snapshot only. A Retainers link points the tab's retainers at
    its target before the tab queries.
  - **The worker keeps the shown tab's sets first**: the client says which snapshot and which tab it shows
    (`ShowSnapshotAsync(snapshot, scope)`, protocol 26; the scope is the tab's id). Only the shown tab queries (ADR-044), so a
    new set of the shown snapshot is that tab's. Over the store's limits the other snapshots' sets go first, then the shown
    snapshot's other tabs', then the rest, each least recently used first.
- Consequences: measured before tabs (budgets.md), the UI process holds about 61 to 69 MB of managed heap with one to three
  Medium snapshots open, each with a drilled path and an inspections run, so a tab costs the front end little; each snapshot's
  graph (about 1.5 GB for Medium, in the worker) is what counts, and tabs share it. A query still running when the tab changes
  is tagged with the new tab; its set is evicted with the others in time. "Open in a new tab" from a row is in the Backlog.
