# HeapScope user guide

The [README](../README.md) shows what HeapScope does. This guide covers how each part works, and
[INSTALL.md](../INSTALL.md) takes you from download to a first snapshot.

## Try it: learn on HeapScope's own sample

HeapScope ships with a small .NET process of its own, the sample, with memory problems planted on purpose. Each problem is
a scenario: a static cache that only grows, event handlers never removed, timers never stopped, async methods that never
finish, a finalizer backlog, wasted memory, large objects, steady allocation from one call site, allocations in bursts and allocations that are dropped again, among others. Because
HeapScope knows what each one planted, you can practice on it and be told whether you found it.

- **The Learn page** (low on the left, Ctrl+7) lists the lessons, one per scenario. Choose one and press Start lesson: its
  sample starts and HeapScope shows the page the lesson starts on. The steps stay in a panel on the right of every page while
  you work. When you have found the problem, answer the lesson's question on the Learn page: the answer is checked against
  the type the sample planted. Lessons you finish are marked done and remembered. Stop lesson stops its sample.
- **The Processes page** has a Sample process section to start the sample yourself: tick the scenarios, and Keep growing to
  add more of each problem every second (for Live, an allocation recording, Compare and Trend). The sample's row is marked
  Sample in the Kind column, ready to capture or record. Stop sample stops it.
- **On the command line**, `heapscope-cli sample list` lists the scenarios, `heapscope-cli sample start --scenarios
  StaticCache,Timer` runs the sample until Ctrl+C (add `--grow` to keep it growing), and `heapscope-cli sample stop --pid
  <pid>` stops it from another terminal. Capture it with `heapscope-cli capture --pid <pid>`.

The lessons, scenario by scenario, and where each one's answer shows:

| Lesson | Scenario | Where you find it |
|---|---|---|
| A cache that never lets go | StaticCache | Snapshots, Retained sizes ticked, filtered to Fixtures.Leaks, sorted by Retained: the top row; Why alive shows the static field |
| Event handlers never removed | EventHandler | Inspections, Event handler leaks: Subscriber types |
| A closure that holds its data | Closure | Sorted by Retained: a display class, whose field holds what the lambda captured |
| Objects that are never disposed | UndisposedList | Filtered to Fixtures.Leaks: the type with thousands of objects; Why alive shows the list |
| A dictionary that grows with every request | UnboundedDictionary | Two captures compared, filtered to Fixtures.Leaks, sorted by Bytes +/- |
| Timers never stopped | Timer | Inspections, Timer leaks: Target |
| Disposed objects still referenced | DisposedReferenced | Inspections, Disposed but referenced |
| Async methods that never finish | PendingAsync | Inspections, Async methods that never complete: the waiting method's type |
| A finalizer queue that never drains | FinalizerBacklog | Inspections, Finalizer backlog |
| Memory wasted by shape | Waste | Inspections, Empty and Oversized collections: what the collections hold |
| Large arrays on the large object heap | LargeObjects | Inspections, Large arrays |
| Where allocations come from | SteadyAllocation | Record allocations; the Allocations page's Types and Back traces |
| What a burst allocates | AllocationBursts | Live, Record allocations ticked: the types of an interval dragged over one burst |
| What was allocated between two snapshots | Churn | Live, Record allocations ticked, and two gcdumps: Compare, Allocated between |

The sample runs as you, never as administrator (HeapScope refuses while it runs elevated), at most three at once from one
HeapScope window or command, and stops when HeapScope closes. A growing sample stops growing when its heap reaches 512 MB;
stop it when you are done. Two scenarios that both block the finalizer thread (FinalizerBacklog and Waste) cannot run
together. The lessons start with a capture, which runs on Windows and Linux and is untested on macOS. On Linux and macOS the Learn page
notes who can capture whom.

## Finding your way

- **Processes you cannot capture.** The Processes page and the Live page list only the processes HeapScope can capture as
  it runs now. Those that run elevated or as another user (on Linux and macOS, another user's) are hidden, and a note says how many;
  check "Show elevated processes" (on Linux and macOS, "Show other users' processes") to list them, marked with what they need. Run
  HeapScope as administrator (on Linux, as root) and they are all listed. `heapscope-cli list` always lists every process, with
  its mark.
- **Info icons.** The (i) beside a page or tab title says what it is for, when to use it and how to start. One beside a group
  of settings says what each of them does, a line each, so it helps even when a setting is turned off. Hover over the icon,
  click it, or reach it with Tab and press Enter; Escape closes it. A tooltip shows even on a button, menu item or input that
  is turned off, and many say there what they need.
- **Analysis path.** Each open snapshot keeps your last 30 steps (types, a type's objects, an object, referrers,
  retention, links into the Retainers tab) in the Snapshots page's left column. Opening something from an earlier step
  starts a branch; the later steps stay. Back and Forward (Alt+Left, Alt+Right, the mouse buttons) move along it.
- **Bookmarks.** Ctrl+D (Cmd+D on macOS) marks the shown object, or the selected row's object, with a color. Bookmarks
  of every open snapshot are listed together and come back when the same dump is opened again.
- **Object sets.** Select several types (Ctrl or Shift and click, or Shift+Down) and press Ctrl+Shift+O (Cmd+Shift+O), or
  open a namespace or assembly row, a Compare row, an inspection row, your bookmarks or what a set retains: the set
  gets its own step with Types, Instances, Generations, Dominators, Paths and Similar Retention views and "Objects
  retained by this set". A leak detector's row opens the objects behind it: a growing collection's row opens the collection itself, not its elements, an event row opens each subscriber delegate and the object it calls, not the publisher, so its object count can differ from the row's in either direction, and a timer row opens the queue entries, plus the timer object for a `System.Timers.Timer`, not the owner the row names. The CLI's `set` command shows the
  same views.
- **Retention views.** Similar Retention groups a set's objects by how they are retained (the paths of all their direct
  holders, not only the shortest one), each group with its most different paths; a group opens as a set of its own.
  Key retention paths can be shown as a diagram, the paths merged into one graph from the roots down; "Fit to width"
  scales it down to the page, to three quarters of its size at most; a wider diagram keeps its scroll bar. A type's "Why alive" page has a Similar retention button.
- **Comparison depth.** Compare can show each type's New and Dead objects, matched by address for two dumps of one
  process, and open a row's new, dead or survived objects as a set. It also shows each type's retained bytes in both
  snapshots, and the change per generation. The match is an estimate. A GC moves most young objects, so those usually
  count as dead and new. Survived means most for Gen2, LOH (unless compacted), POH and frozen objects, which keep their
  addresses, but a new object of the same type and size at a dead one's address still counts as survived. New and
  survived objects can include garbage not yet collected. "Trend across snapshots" puts the
  objects and bytes of up to 20 ticked snapshots side by side, with how often each type grew. The CLI's `diff`
  matches objects (`--match-objects`), adds retained bytes (`--retained`), and gates on one heap kind's growth
  (`--fail-on-growth 50MB --growth-of gen2`) or on each row (`--fail-on-growth-per-row`). For a stable gate, capture after a full GC in the target or compare
  gcdumps, since a full dump counts garbage not yet collected. That GC moves objects, so skip it when you match them by
  address. `--growth-of` a heap kind needs full dumps, since a gcdump records no generations. `trend` compares several
  dumps.
- **Allocated between two snapshots.** Two snapshots say what is there now and was not, not what was allocated and is gone again. When
  both were captured while one run on the Live page recorded allocations, tick Allocated between in Compare: each row says about how
  many bytes of its types were allocated between the two, estimated from samples (about one per 100 KB), and the note says how much was
  allocated in all. An empty cell means no sample, not no allocation. "Show where it was allocated" in a row's menu opens the
  Allocations page at that time and type. Stop the run first: its trace is read when it is complete. A gcdump is joined by type name, and
  types without an object in either snapshot have no row: the note says their bytes, and the Allocations page has them.
  On the command line: `diff --allocated`.
- **Reading the Retainers chart.** Each bar is what the bar above it keeps alive, and its width is how much. The two band
  colors only separate the levels; a selected bar and its path turn amber. Most bars are too narrow for a label, so hover
  for a bar's name and size. Click selects a bar, and a double-click shows the tree below it. Bars too narrow to point at
  are grouped into one bar, "+N smaller", that says how many it holds and their size. The tree below has them (expand a
  group or a "more" row if they are not shown); deeper in the chart, a double-click on the bar above them focuses on it, and
  the tree then has them.
- **Live allocations.** Tick Record allocations under Snapshot rules before Start, and the Live page shows below its chart what
  the process allocates while it runs, usually less than a second behind it: bars over time, the 20 types allocated most, and, when
  you tick Show call stacks, where. Drag across the bars to choose an interval; a click shows the whole run again. Sizes are
  estimates from samples, about one for every 100 KB allocated. In a long run the bars stand for longer stretches, and an interval is counted in whole ones. A
  process that allocates nothing for a while may show its last samples one sampling interval late. The trace is
  recorded as before, and Open allocations opens it with everything once the run stopped.
- **Live workflow.** When a process cannot be monitored, a warning says why: it runs elevated or as another user, so
  run HeapScope as administrator. A warning also appears when no samples arrive after about 10 seconds (or three
  intervals, if longer), since a process can turn off the runtime events Live reads; Get snapshot still works then. The Live page takes a snapshot of the monitored process now (Get snapshot) and forces a full
  garbage collection (Force GC, a dotted line on the chart). Under "Snapshot rules" it captures on a schedule, when the heap has grown by an amount you set, or when the
  heap passes a size, and records an allocation trace for the whole run when asked; "Past runs of this session" reopens
  earlier timelines and snapshots. One capture per process runs at a time, from any page or rule. Snapshots can be closed
  and deleted with their caches, sessions deleted, and old sessions cleaned up from the Home page; each asks first.
- **Runtime leaks.** Detectors for async methods that never complete (their state machines waiting by the thousand), a
  finalizer backlog behind a blocked or slow finalizer thread, and, against a baseline, the application's types that
  keep growing. The objects no root reaches open as a set from a full dump's Types view (`set --unreachable`).
- **Group by interface.** The types grid and Compare group by interface on full dumps: a type counts under each
  interface it implements, so the rows overlap. In the types grid, an interface row opens as a set.
- **Filters.** In the types and Compare grids, terms separated by spaces must all match; `-term` leaves names out;
  `module:name` (or `m:name`) matches the module. Each term is a substring, CamelHumps with capitals (`SCE` finds
  `StaticCacheEntry`), or uses `*` for anything.
- **CSV export.** Every grid has "Export to CSV..." in its context menu (Ctrl+E or Cmd+E). It writes all rows, not only
  those on screen. String text, field values and command lines are left out unless you say yes for that export.
- **Jobs.** Long work shows a progress row above the status line while it runs, with Cancel. Finished jobs move to the
  Jobs page; the status line says how many finished and how many failed, and a click on it opens the page.
- **Keyboard.** F1 lists every shortcut on the About page: Ctrl+1 to 9 (Cmd on macOS) for the pages, Alt+1 to 4 for the Snapshots
  tabs, Ctrl+F for the filter, Ctrl+L for the analysis path, F5 to refresh.
- **Appearance.** HeapScope follows your system's light or dark theme, and changes with it while it runs. To choose one,
  start the app with `--Ui:Theme Light` or `--Ui:Theme Dark`.

## Logs and getting help

HeapScope writes a log file per run, which records the versions, the snapshots opened and why each result was produced
(field values and strings read from a dump only if you turn on Include dump values in logs on the Settings page, which
asks you to confirm, or `heapscope-cli settings --dump-values-in-logs on --yes`; it is off by default, and each log says
whether it holds them; the text you type in filters, unless you turn that off with Include filter text in logs or
`heapscope-cli settings --filter-text-in-logs off`): `%LOCALAPPDATA%\HeapScope\logs` on Windows, `~/.local/share/HeapScope/logs` on Linux and
`~/Library/Application Support/HeapScope/logs` on macOS (under the folder `HEAPSCOPE_DATA_FOLDER` names, when set).
The About page shows the folder and copies the version details for a support request. For more detail in one run,
start the app with `--Logging:File:MinimumLevel Debug`, or set the environment variable
`Logging__File__MinimumLevel=Debug` (the CLI reads settings from the environment, not from its arguments). Logs are kept
14 days. They hold file paths and type names, so share them only with someone you trust. Purge logs on the Settings page
(or `heapscope-cli purge logs`) deletes them, but the ones other running HeapScope processes write (the app's own log goes
on in a new file, so all it wrote before goes too; after you turn dump values off, it waits two seconds so the last values
go with it); Purge all deletes the logs,
every session of the library and the bookmarks, never a dump you opened from your own folders.
