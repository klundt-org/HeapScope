# ADR-044 A workspace per open snapshot (added in sprint 4.9, amends ADR-029)

- Context: peer review 4.5 (H13) found that switching snapshots lost work. ADR-029 kept an analysis path per snapshot,
  but the types grid, the inspections and the retainers were shared by the page and pointed at the selected snapshot
  again on every switch: a filter, a sort, a finished inspection run and the retainers shown were lost, and each switch
  queried the worker again. The worker's result set store evicted least recently used sets with no idea which snapshot
  was on screen, so the shown one's rows could give way to a hidden one's.
- Decision:
  - **A workspace per open snapshot**: `SnapshotWorkspaceViewModel` owns a snapshot's types grid, analysis path
    (navigator), inspections and retainers, pointed at that snapshot once, when it is first shown. The Snapshots page
    keeps one per open snapshot, made on first selection, and shows the current one; its `Types`, `Navigator`,
    `Inspections` and `Retainers` pass through to it, so the views bind as before. Switching shows a workspace as it was
    and queries nothing. A hidden workspace queries nothing either: a first inspection run that has not finished stops and
    runs again when shown, with the baseline it had (a chosen None included); a rerun of the growth tiles stops alone, and a
    baseline that changes while hidden (a snapshot opened or closed) leaves them stale; they run when the workspace's
    Inspections tab is shown, and the finished results stay. Closing a snapshot, or opening its dump again, disposes its workspace and
    releases its result sets. Compare, Trend and the bookmarks stay on the page: they span snapshots.
  - **Links land in their workspace**: a `NavigationTarget` names its snapshot; following one shows that snapshot's
    workspace first, then the step. A link into a snapshot that is no longer open is refused with why, never opened in
    another snapshot's workspace.
  - **The worker keeps the shown snapshot's sets**: the client says which snapshot its window shows
    (`IResultSetService.ShowSnapshotAsync`, protocol 25), and again after a reopen in a new worker. Each stored set names its
    snapshot; over the store's limits the sets of the other snapshots go first, least recently used among them, then the
    rest. Sets of no snapshot (traces, a synthetic graph) and Compare's diff and trend sets are never preferred. Before the
    client says anything, eviction is least recently used, as before. The count cap is 128 sets (32 before): an inspections run
    alone holds about 20, so two or three workspaces fit, and the 256 MB byte budget stays the memory guard. Closing a snapshot
    in the worker drops its sets, so none keeps a closed graph alive through its lease. A set evicted from a hidden workspace runs its query
    again when paged (`ResultSetHandle`), so a workspace shown again re-runs only what was evicted.
- Consequences: the front end keeps every open snapshot's views and row lists while it is open (ADR-029 already kept its
  path's steps); measuring front end and worker memory with three Medium snapshots open, and trimming the row lists of
  workspaces not shown, stay in the Backlog. The page and workspace tests run on a UI thread of their own
  (`SingleThreadUiDispatcher`), so a view changed off it fails them. Several workspaces of one snapshot (analysis tabs, sprint
  4.10) build on this: the workspace is the unit a tab holds. Two things change then (decided autonomously in the 4.9 review):
  the worker's preference is by snapshot, so the tabs of one snapshot share it, and 4.10 tags sets by workspace instead (an
  opaque scope id, a protocol change of its own); and links are routed by snapshot, so 4.10 makes the workspace the
  navigator of its own views, handing a link into another snapshot to the page.
