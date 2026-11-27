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
    closes it). Closing a tab disposes it and releases its sets. Tabs are not kept across restarts, nor when a dump is opened
    again. The tab strip shows on the page's Snapshot and Retainers tabs, the views of one tab; Compare spans snapshots and the
    inspections are shared, so they have no tabs, and New tab and Close tab (Ctrl+T and Ctrl+W, Cmd on macOS) do nothing there.
  - **Links land in their tab**: a tab is the navigator of its own views, so a link of its snapshot lands in the tab it came
    from, and one into another snapshot goes to the page, which shows that snapshot's current tab. The inspections, shared, send
    their links to the tab shown. A tab's path holds steps of its snapshot only. A Retainers link points the tab's retainers at
    its target before the tab queries.
  - **The worker keeps the shown tab's sets first**: the client says which snapshot and which tab it shows
    (`ShowSnapshotAsync(snapshot, scope)`, protocol 26; the scope is the tab's id), before the tab's views query. Only the shown
    tab queries a tab's views (ADR-044), so a new set of one of them is that tab's; the inspections are the snapshot's, shared by
    its tabs, so their sets carry no tab and stay with the snapshot. Over the store's limits the other snapshots' sets go first,
    then the sets of the shown snapshot's other tabs, then the rest, each least recently used first. The client tells one at a
    time, the latest shown, so a quick switch away and back ends with the right tab told; the tell goes before the tab's views
    query, but without waiting for it, so a first query can reach the worker first and be tagged with the tab shown before.
    That costs only eviction order, and the eviction line says which tab and rule took a set. The cap stays 128 sets: tabs add sets
    a hidden tab runs again when shown, and the 256 MB byte budget stays the memory guard.
- Consequences: the front end's cost is measured in budgets.md with three Medium snapshots open and the most tabs on one,
  each tab's grid of objects paged as far as a grid keeps pages; each snapshot's graph (about 1.5 GB for Medium, in the
  worker) is what counts, and tabs share it. A query still running when the tab changes
  is tagged with the new tab; its set is evicted with the others in time. "Open in a new tab" from a row is in the Backlog.
