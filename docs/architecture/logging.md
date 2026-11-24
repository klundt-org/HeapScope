# HeapScope architecture: Error handling and logging

Part of [ARCHITECTURE.md](../ARCHITECTURE.md); the section numbers are the same as before the split (sprint 3.8), so
references such as "ARCHITECTURE.md 11" still name the right section.

## 11. Error handling and logging
- Microsoft.Extensions.Logging everywhere. The UI and the CLI each write a file per process and day,
  `heapscope-yyyyMMdd-<ui|cli>-<pid>.log` (`FileLoggerProvider` in Application, sprint 2.3.7), in
  `%LOCALAPPDATA%\HeapScope\logs` on Windows, `~/.local/share/HeapScope/logs` on Linux and
  `~/Library/Application Support/HeapScope/logs` on macOS (`Logging:File:Directory` overrides it). A file per process
  means two HeapScope processes never write over each other's lines (sprint 2.3.7 review H1). Each file starts with a
  header: the build with its source revision, the system, architecture, runtime, self-contained or not, the process and
  the log folder. Lines carry an ISO 8601 time with offset and the active scopes. A file rolls at 50 MB
  (`-2`, `-3`...); files older than 14 days are removed when a process opens its first file. `Logging:File:MinimumLevel`
  (Information by default) raises the detail for one run, and is passed to the worker (`--log-level`). The UI also logs
  to debug output, Avalonia's warnings (area, template and source type only) and failures before its host exists; the
  CLI also writes Warning and up to stderr. The worker logs single-line records to stderr, which the client forwards
  into its own log.
- **Correlation (sprint 2.3.7)**: every job (`JobTracker`: open, capture) and every CLI command runs under an
  `Activity`; its trace id is a scope of every line it causes, in the front end and, through StreamJsonRpc's activity
  tracing (W3C trace context in each request), in the worker. Not every app call has one yet (checked in sprint 3.12): the
  Inspections tab runs each inspection through `SnapshotSession.RunInspectionAsync`, not as a job, and the Object view's
  load runs outside `QueryRunner`, so their worker lines carry no trace id of the front end's; match them by snapshot id and
  time. The Backlog's query runner item (refactor analysis, rank 1) brings them under an activity. Job start and end lines carry the job id, title, time and
  outcome; the CLI logs its command line (paths and numbers as given, a `--filter` value quoted or by its length as the setting
  says, other values by their length) and exit code.
- **Provenance of results (sprint 2.3.7, review H2 to H4)**: the log backs up any result a user contests.
  - Each process's start: versions, system, architecture, runtime, options; the worker adds the protocol, its path, the
    GC settings and a versions line (loaders, index cache format, retention semantics, ClrMD, each inspection's version).
  - Each snapshot open: one identity line that ties the worker's snapshot id to the file: path, size, fingerprint,
    kind, capabilities, facts, process, runtime, capture time and its source, from the cache or the dump (or a reopen),
    memory mode, cache path and identity, writer and loader versions, counts, dropped references and roots, unreadable
    objects, incomplete segments, static roots, the DAC and why it was trusted, and the notes shown. A load from the dump
    also logs its phase times (since 3.12 freeze and collect apart, and each phase's process CPU, GC pause and pages read from disk, all three the whole process's). The client logs its handle with the worker id on open, reopen and a changed-file refusal.
    Since 3.12 the source says "older index cache (loader version N) in place of a failed rebuild" when that is where the
    graph came from, and whether the open could ask the DAC question ("DAC question allowed" or "not asked").
  - The older cache in place of a rebuild (sprint 3.12, ADR-041): why a cache was not used ("loader version N, current M; it
    opens instead if the rebuild cannot reach the DAC", or "is older than version 3, so it never opens"); a warning when it
    opens, naming both loader versions, the memory mode and the rebuild's error and message, or when it could not be opened
    either; and the process name read again from the dump when it differs from the cache's. A snapshot that opens without
    value reads logs "opens without reading field values or strings" with the DAC error's full message (the folders
    searched, the untrusted file and its hash), which the short reason on screen leaves out; the identity line then says
    its DAC was "recorded when the cache was written and not usable now". A process name that cannot be read again is a
    warning ("could not be read again from the dump ...; the older index cache's is kept"). Trust this DAC and reopen logs
    the question and the answer like the open's own question, then "closed in the worker, to be opened again in place"
    and the open under the same handle.
  - Each DAC choice (`DacChoice`): the file, the rule that accepted it (time stamp and size, file version, own build
    id, runtime build id with the installed-folder or version-string pairing), every file looked at with why it was not
    used; a symbol-server download is a warning. Kept in the cache metadata, so a reopen still says it.
  - Each answered query: `WorkerErrors.GuardAsync` logs the operation, the request and the result (`Provenance`), with
    page reads and probes at Debug. Inspections add their version, thresholds (`IInspection.Parameters`) and basis (what
    was scanned or skipped; since 3.12 every inspection first in the same terms, "scanned N, left out M, rows listed at most
    50", and no list of rows in a log line longer than 50). Retention paths log each root shown (its kind and description) and each key path (root,
    type steps, objects, bytes), with stopped-early, truncated and the root path fallback. Compare logs how its pair was
    chosen; result set re-runs are logged. The dominator tree logs its focus, grouping, row count, total, the trail
    (types, addresses, retained), the first 20 open keys, the revealed key and its row, and the first ten rows of the
    list just opened (or the top); the icicle its widest bars; set retention the type, all and reachable members, what
    they retain, the lower bound and the first ten retained types. Each user action on the Retainers tab (expand,
    collapse, focus, grouping, a link from another tab, a set) logs one line under its own activity, which its tree,
    icicle and set queries join (sprint 2.4 and its review). Grid queries and the panel loads that go through `QueryRunner`
    run under an activity too, so their worker lines carry their trace id; the Object view's load does not yet (see
    Correlation above).
  - Monitoring logs each run's rules and, when a rule fires, the metric and its source, the threshold after steps, the
    baseline and the rearm mode (`RuleDecision`); the CLI logs its growth gate's decision.
- **A dump's process name (sprint 3.10)**: the snapshot's identity line says `process {Process} ({Pid}; name: {Source})`, where the
  source is the rule that chose the name: the executable's path the dump records (with the path), else the only .exe, the first of
  several, the first module not named like a library, or unknown, each with why the path was not used. Kept in the cache metadata, so a reopen still says it; a cache written before the
  rule was kept, by an earlier build of 3.10, says "not recorded".
- **Opening again, the index key, and chart groups (sprint 3.11)**: opening a dump that is open, when the worker answers with a new
  snapshot (after a refused reopen, or a worker restart not yet followed by a reopen), logs "Snapshot {Path} was opened again: worker
  snapshot {PreviousId} is now {Id}; its row is shown afresh and its analysis path starts again". An open that changes nothing logs
  only its open line, and a silent reopen after a restart logs its own lines ("Snapshot {Path} reopened: ..."). A read of the index
  cache's key file that is retried under the lock is not logged: the key's outcome says Loaded either way. The Retainers chart's
  "+N smaller" groups are not logged: they depend on the window's width and are made in the view, from the bars the icicle query
  logged; the worker sends no bar under 0.1 percent of the top bar. Info icons and tooltips log nothing. The object matching
  line of a comparison also counts its survivors that are pinned and in a younger generation than before, which match only
  because a root pins them (review finding M13), and the addresses whose objects differ only in being in a younger generation,
  unpinned, which count as dead and new: a new object at a reused address or an unpinned object the GC demoted, which the
  match cannot tell apart. The rest of the reused addresses hold another type, size, area or method table (M14).
- **The tutorial (sprint 3.13 and its review H5)**: the sample's catalog read (its file, the executable or the dotnet host,
  the catalog version and each scenario with the type it plants), or why it was not (exit code and the sample's own message);
  each start (pid, scenarios, growing and its interval, time to ready, the command line) and refusal (the scenarios asked,
  the reason, the pids running at the limit); each stop with who stopped it (the Processes page, a lesson, Ctrl+C, another
  HeapScope process, HeapScope closing) and each exit on its own (pid, reason, exit code, run time, the sample's last message,
  2 KB at most); a lesson's start (scenarios, answer type and scenario, catalog and lesson versions, the choices offered), its
  answer (chosen, expected, the sample and whether it still runs, right or wrong), its end and a sample gone under it; the
  progress file's writes and a damaged file kept as `.bak`. Every process list logs the pids it listed (200 at most) and the
  pids it left out as HeapScope's own or could not read (off Windows, since 4.1, also the temp folder it looked in for
  diagnostics sockets, so a process using another one is explained), and the Processes page logs a sample it started that
  the list does not show, so "the sample is not in the list" can be backed up. All of these are names,
  pids and counts: the sample's messages are its own, never a dump's contents.
- **Hidden processes (sprint 4.1.1 and its review H1, M1)**: the Processes page and the Live page hide the processes that
  need more rights than HeapScope has; each logs "The {page} page lists {shown} of {count} processes; '{box}' is
  checked|unchecked; hidden as needing more rights than HeapScope has: {pids}" (200 at most, then "and N more") after a
  listing when that changed since its last line, and on every toggle of the box ("(just changed)"). The "Listed" line
  says on Windows whether HeapScope runs elevated, and on Linux and macOS gives the effective user ids, which decide the
  marks, and of how many processes the user, the command line and the runtime folder could not be read (sprint 4.2).
- **Capture on macOS (sprint 4.2)**: the same lines as on Linux (below), with "macOS check of pid {pid}: ..." (user ids,
  how the environment read ended, diagnostics off, both `TMPDIR`s, the region walk: how many regions, where it stopped and
  why) and, for a dump that was not written, "macOS; rule: {the rule that decided} ('the text it matched')". Once per run,
  at the first list or capture, "Capture on this system is untested. macOS reads of HeapScope's own process ..." gives
  every read's outcome: read, "errno N", "N bytes returned, M expected" (another struct layout) or "the call is not there".
  "running" is whether a process has the id, not whether anything about it could be read.
- **The estimate (sprint 4.2, every system)**: before a full dump, "The full dump of pid {pid} is estimated at {bytes}
  bytes, from the memory walk|the private bytes (walk ..., private bytes ..., not in the walk ...); free in its folder: ...".
  On macOS the larger of walk and private bytes decides, and "not in the walk" is the shared cache.
- **DAC beside a dump (sprint 4.2)**: "No DAC is copied ..." when the runtime folder is not known, and a runtime library left out.
- **Capture on Linux (sprint 4.1 and its review H6, M4)**: each capture logs "Captured {name} (pid): {bytes} bytes in {s} s;
  estimated {bytes} ({n}% of it written)", so the disk check's margin is known on each system (CI flags a Linux dump larger
  than its estimate; the first runs wrote 94 to 95%). A pid that is not found logs "Pid {pid} was not found: Linux check
  of pid ...", what the reason given rests on: whether it runs, its effective user id and HeapScope's, whether its
  environment could be read, which diagnostics setting turned the port off (its name only), its `TMPDIR` and HeapScope's,
  whether it shares HeapScope's mount namespace (its own `/tmp`), and where its runtime is (`libcoreclr.so`'s folder, the
  JIT, inside a single-file executable, or none). A dump the runtime could not write logs "The write failure of pid {pid}
  was judged by: ptrace_scope {n}; container yes/no; rule: ...", the rule that named the cause (no space left, a folder the
  target's account cannot write, ptrace refused, scope 3, or none). A full dump refused because HeapScope runs as root and
  the process as another user says so, with both effective user ids. Live and recording log the same evidence when a
  process does not answer ("Pid {pid} could not be monitored: ..." and "could not be traced: ..."), and a refusal of
  another user's process logs both effective user ids in a capture, Live and a recording ("was refused as another
  user's"); the process list adds HeapScope's effective user id and each marked process's. The refusal itself lands on
  the job's failed line under its trace id. `/proc`
  command lines and environments are never logged: of the environment only `TMPDIR` and the name of a setting that is 0.
- **What a log may hold (sprint 2.3.7 review M6)**: paths, file and process names, type, module and field names, counts,
  sizes, addresses, ids, versions, flags and HeapScope's own notes. Never field values or string contents from a dump:
  `Provenance` writes any other text by its length only, and error messages that would echo a filter leave it out. The
  one exception is the user's (sprint 3.13.2, ADR-043; owner, 2026-11-19): with Include dump values in logs on (off by
  default; the Settings page asks first, the command line needs `--yes`), `Provenance` writes each such text quoted (its
  first 200 characters, and its length) and a list's first 20 items, each described, then how many more. Every process's
  start line names both settings ("logs hold filter text on, dump values off"), and the worker logs each change, with a
  warning not to share the log from there on. The
  user's filter text (sprint 3.12, B1; owner, 2026-11-12) is logged, quoted on one line, unless the user turns off Include
  filter text in logs on the Settings page (on by default: logs stay on the user's machine, and the setting's tooltip says
  to turn it off before sharing one); its shape (length and term counts) always is. The setting lives in `settings.json` in
  the data folder (`HeapScopeSettings`), and every process follows a change within a second (`LogPrivacy`): the worker's
  provenance lines, the CLI's command line (by option: a `--filter` value follows the setting whatever it looks like) and
  the growth gate's line. Logs are local evidence for support; they are not meant to be
  published, since paths name the user and the machine. A result meant to be shared goes through the shared analysis
  export (ADR-026), which strips names by default.
- Exceptions are caught at boundaries only: worker RPC methods, view model commands, CLI command handlers.
  Inner code throws specific exceptions (`CapabilityNotSupportedException`, `SnapshotFormatException`,
  `ElevationRequiredException`, `WorkerCrashedException`) and does not swallow them.
- Worker errors cross the pipe as JSON-RPC errors with a `HeapScopeErrorCode` as the JSON-RPC error code so clients
  can show a precise message.
- Users always see a plain-language message and a next step. Stack traces go to the log only. A worker failure that is
  not a refusal (sprint 3.12, B5) shows the owner's fixed message with an id; the same id is on the error line in the
  log ("failed unexpectedly (id ...)"), which support asks for.
- Navigation and polish (sprint 2.7): the analysis path logs each step opened, shown or closed by kind (Types,
  Instances, Object, TypeReferrers, TypePaths, Retainers, RetainedBySet, and Set and SetInstances since 3.1), its id on the path, its parent's id, its depth,
  the worker snapshot id and the step count, and each switch of snapshot, never a type name or an address; a CSV export
  logs the grid's fixed name, the worker result set it reads (whose query the worker logged), the rows expected and
  written, the columns, whether values were included and the time, never the file's path or a cell (a failure logs its
  exception type and HResult); bookmarks log counts, opens and file problems only. Provenance logs a filter by its shape
  (length and counts of terms, excluding terms and module terms), and its text as the setting above allows.
- Settings (sprint 3.12, B1; sprint 3.13.2): a saved setting ("Setting saved to {File}: include filter text in logs on|off",
  "Setting saved to {File}: include dump values in logs on, confirmed by the user" or "... off"); the question to turn dump
  values on and the answer ("Asked to include dump values in logs; the user answered yes|no"), or on the command line "Settings
  not changed: --dump-values-in-logs on needs --yes"; after dump values are turned off, "This log goes on in {Part}; Purge logs
  deletes this process's earlier parts, which may hold values read from dumps"; Purge logs,
  the files and bytes it deletes and keeps (the part being written); Purge all, the question and the answer, what it deletes
  (sessions, bookmarks, logs, files, bytes) and keeps, each failure, or why it was refused; the current session let go of.
  The worker follows the `settings.json` of its client's own data folder (`--settings-folder`). A file that is there but
  cannot be read keeps the last filter text setting read, or filter text out (review H4, H5), and always turns dump values off
  (3.13.2 review M1); the Settings page and `heapscope-cli settings` refuse to save over it, so one setting never resets the
  other (3.13.2 review H2). The app, the worker and the command line say on their start lines which file they follow and what
  the logs hold ("logs hold filter text on, dump values off"), and so does the header of every log part, a new day's file
  included; the worker logs each change it sees ("The logs now hold filter text on, dump values on ({File}); from here on
  this log holds values read from dumps, so do not share it", a warning when dump values are on; M19), and the app logs each
  change the user saves. Turning dump values off starts a new part of the process's log, and so does every purge (after
  waiting out the two seconds a worker needs to see dump values turned off), logged first in the new part ("This log goes on
  in {Part} before the purge, after waiting {N} ms for the workers to see dump values turned off"), so Purge logs deletes all
  this process wrote before, and Purge all keeps its own record: its start line says it was confirmed, since the question
  and answer were logged in the part it deletes (3.13.2 review H1 and re-reviews).
  Purge logs names the logs it kept (being written by running HeapScope processes) and the files it could not delete; Purge
  all logs one line per session (its files, bytes and the dumps kept outside it, 50 sessions at most), a refusal where it is
  decided, and an outcome when a snapshot cannot be closed (M5, M10, M13, M14).
- Start and first shows (sprint 3.12): "First window rendered N ms after process start"; the background build of the pages
  ("Prewarmed {View} in N ms", "in place" for the Snapshots page; a step that fails is a warning naming the view; "View
  prewarm done|stopped: N views in N ms of UI work over N ms (views)", "Prewarm garbage collected (gen 1) in N ms", or
  "View prewarm skipped" when `Ui:Prewarm` is false or in a screenshot run); the watchdog's lags and long operations over
  100 ms with the UI thread's CPU and the GC pauses in them. The smoke runs' lines ("Smoke slow operations" and the others)
  are in DEVELOPER.md.
- Session files (sprint 3.12, B4): an entry with an enum value this build does not know is kept as it was and saved
  unchanged, and logged once per session and process with the values ("Snapshots: Kind=LiveCapture", review M18).
- Every worker method runs through `GuardAsync`, the control methods and closes included (review M17), and the view models
  log what they showed for a worker failure ("{Section} shown as: {Message}"). The command line keeps the worker's lines out
  of its console (file log only, M16) and names the log folder for an unexpected failure.
- Object sets (sprint 3.1): a set is logged by its shape (`ObjectSetSpecs.Shape`: the kind, the first 20 type keys with
  their modules, the namespace or assembly, the inspection, row and baseline, and for objects their count and a short
  hash of the sorted addresses, never the addresses), in every request's provenance too. A resolution logs the
  snapshot id, objects, bytes, types, time and how it matched (the type entries matched for the keys, and the keys that
  matched nothing, with their modules; for an inspection row the rule version and whether its own objects or its hidden type, module and scope were
  used, and a re-run of the inspection with its reason); a cache hit logs the set and its size; evictions log at Debug.
  A set's dominators log the top members' count, what they retain, the members no root reaches and the five largest by
  type; set retention logs the shape and "n/a" as the lower bound of a set that is not whole types. The front end opens
  a set and switches its views in a `HeapScope.Sets` activity (the view's query and its worker lines share its trace
  id), logging the snapshot and the shape. A disputed set view is traced to what the set held.
- Retention views (sprint 3.2): Similar Retention logs its rule version, the reverse index's memory mode, the set's
  shape, its members, groups, unreachable members, the members with more referrers than were looked at, the grouping's
  time, and the five largest groups (`Provenance.Describe(RetentionGroupDto)`: number from 1, pattern key, objects,
  bytes, types and first type, path count, the paths shown as roots and type names); every other listed group at Debug.
  A retention group set logs its number, pattern key and the inner set's shape and size. The interface table logs the
  type entries, how many were not read and the first 20 of them with the reason; an interface set logs the first 20
  matching type names and the unread count. A set's key paths log the set's shape in place of the type. A growth gate
  logs its grouping.
- Comparison (sprint 3.3): a diff logs whether it matched objects and why not, the New, Dead and survived totals, whether
  retained bytes were added and where each side's dominators came from, and its column count; a compared set logs its
  change, the other snapshot, how many of the inner set's objects it kept and the survivors in all; a trend logs its
  snapshots in order, grouping, rows and the first rows' growth; the CLI's growth gate logs what it checked (total or a
  heap kind, per row or summed) and how many rows exceeded.
- Live workflow (sprint 3.4): a forced collection logs the target, whether its end was seen and the time; a refused
  capture or collection logs what it was, why it was asked and what was running; a run logs its id at the start, the
  allocation trace's path, size and length, and each time it is recorded in its session (snapshots, forced collections);
  reopening a past run logs its id, timeline, samples, events and snapshots; a rule deferred by a busy process logs once
  per busy stretch, and a re-armed rule says why; the trace's early stop logs its reason; every deletion logs the
  question (files and bytes), the answer, the paths and bytes removed, the files left (with the reason) and kept (paths
  of the user's own files, never their contents), or why it was refused; cleanup logs its cutoff, what it proposed and
  what it kept and why.
- Leak detectors (sprint 2.6): the inspection line carries the rule's version and thresholds, the baseline used (or
  none), the elapsed time, and the basis: what was scanned (static collections counted and in the baseline, delegate
  types, subscribers and publishers read, timers by state, types probed for a disposed flag, clients and scopes by
  state), what was left out and why, counted by reason (unreachable publisher, held by its own target, framework
  callback field, field not an event, one-shot or paused timer, unreadable field), every reported row with its counts,
  and the largest below the threshold, so a disputed finding is traced to its rule and inputs; the front end logs each
  run with its snapshot and baseline, and each baseline choice.
- Runtime leak detectors (sprint 3.5): each basis line counts what was scanned and left out (async state machines waiting,
  ended and unreadable, by the largest methods; finalizer queue objects by type; types reachable now and in the baseline,
  framework types left out, the largest growth below the thresholds); the unreachable set's line has its objects, bytes
  and the reachable count.
- DAC trust (sprint 3.6, ADR-035): every open and reopen logs, under its trace id, the DACs it trusts to load from next to
  a dump, each with its hash, file, reason (the user when opening which dump, HeapScope's capture, `--trust-dac` for this
  run) and since when; the worker's DAC choice line names the hash it loaded ("trusted by SHA-256 ... and loaded from a
  private copy"); a refusal lists the trusted folders searched and why each file was not used; the app logs its question
  and the answer; `TrustedDacs` logs each grant, a hash trusted already, a capture's copy left untrusted (elevated, or a
  copy that differs from its source) and a trust file it cannot read (kept as `.bak` before a new one is written).
- Session documents (sprint 3.6): `SessionLibrary` logs a current session replaced because its folder or document is
  gone, a current or opened session refused as unreadable with the reason, sessions left out of the list (a count at
  Information, each with its reason at Debug), a removed empty session and one kept because something else writes into
  it; a save that fails after its retries says so. A monitoring run refused in an unreadable session logs the reason.
- Allocation traces (sprint 2.5): opening a trace logs its file with its size and write time, the reader's version, the
  process (and that it came from the first allocation tick), the recording's start and duration, samples, frames, lost
  events, samples without a stack or with unresolved frames, whether the rundown arrived and TraceEvent's conversion
  problems; a re-read logs both file identities; a recording logs why it ended (duration, Stop, process exit, cancel);
  each query logs its interval, type and the rows it answered (type and method names with their bytes; for trees the
  open keys, the revealed row and the rows under it; for stacks every stack answered with its rank, innermost stack
  node and first 16 frames);
  user actions on the page (an interval, a type, an expansion, the tree kind) log under a `HeapScope.Allocations`
  activity whose trace id the worker's answer lines carry. Never event payloads beyond type names and sizes.
- Hardening (sprint 3.7): the worker logs where its index cache key came from (`Index cache key {Path}: Loaded`,
  `Created`, `ReplacedDamaged`) or, when the key file can be neither read nor created, a Warning naming the path, the
  error and the next step (this run signs with a key of its own; ADR-036). A cache not used logs its reason with the
  snapshot's note, for the base cache as for sidecars (`Untrusted` says whether it was unsigned or signed with another
  key). A gcdump refused as inconsistent names the reader's exception type in its message, and the worker logs a
  refusal's wrapped exception with its stack at Debug; any other error while building the graph is a HeapScope error,
  logged at Error with its stack. A capture whose dump could not be made private logs a Warning with the path and error,
  and the result says so. The app logs a session snapshot or Show in folder refused because the path is not on this
  machine. Paths and exception types only, never dump contents.
- Modularity (sprint 3.8): every view's query logs a worker refusal as shown ("{Section} shown as: {Message}") under the
  activity whose trace id the worker's answer lines carry, grids included (they showed refusals without logging them);
  an optional retained column the worker refused is logged once. An event listener that throws is logged as a Warning
  ("A listener of {Event} failed") and changes nothing else. A past run's timeline or trace that is not used logs which
  check refused it (not recorded, not local, outside the folder, through a link, missing, not a CSV file). A snapshot's
  open line names its unavailable features; the worker's log lines name the handler that answered (the operation name
  is unchanged; the shared services log under their own categories, such as `ObjectSetResolver` and `SnapshotStore`).
  The worker logs the reachability walks it keeps per snapshot. Every log file's header names the data folder and where
  it came from (the default, `HEAPSCOPE_DATA_FOLDER`), and the worker's start line names the folder it was given; the
  variable moves the logs too, so look for a run's log in `<data folder>/logs`.
- Packaging (sprint 3.9, ADR-039): every log header (and the About page) names the build form (`self-contained build`
  for a release archive or a publish, `dotnet tool`, `framework-dependent build` from source) and the folder the process
  runs from. The client's "Started worker process" line names the worker's file and its host: "its executable", or the
  `dotnet` host that runs the worker's assembly in the dotnet tool. In the tool the worker's own start line gives
  `dotnet` as its path; its header's folder is the tool's `worker` folder. A worker that cannot start says which case it
  is (no worker in the folder, an assembly without a dotnet host, a 32-bit host, a configured path not found) with a
  next step.
