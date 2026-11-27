# HeapScope Sprints

Phases: **0.x** architecture, framework and skeleton; **1.x** quick wins; **2.x** heavy hitters; **3.x** depth to
match dotMemory (planned 2026-10-28); **4.x** what Phase 3 leaves out (planned 2026-11-18); the Backlog ([BACKLOG.md](BACKLOG.md)) everything else. Closed sprints are
in [sprints/](sprints/): [Phase 0](sprints/phase-0.md), [Phase 1](sprints/phase-1.md),
[Phase 2](sprints/phase-2.md), [Phase 3, the peer review sprints, 3.9 to 3.12](sprints/phase-3.md), [Phase 4](sprints/phase-4.md).

## Status

| Sprint | Title | Status | Closed | Tag |
|---|---|---|---|---|
| 0.0 | Project charter, architecture and process | Closed | 2026-10-22 | sprint-0.0 |
| 0.1 | Solution skeleton | Closed | 2026-10-22 | sprint-0.1 |
| 0.2 | Snapshot abstraction and compact graph | Closed | 2026-10-22 | sprint-0.2 |
| 0.3 | Worker process and IPC | Closed | 2026-10-22 | sprint-0.3 |
| 0.4 | Fixtures and test harness | Closed | 2026-10-22 | sprint-0.4 |
| 0.5 | Index cache format | Closed | 2026-10-22 | sprint-0.5 |
| 1.2 | Full dump loader | Closed | 2026-10-23 | sprint-1.2 |
| 1.1 | Process discovery and capture | Closed | 2026-10-23 | sprint-1.1 |
| 1.3 | Heap summary by type | Closed | 2026-10-23 | sprint-1.3 |
| 1.4 | Snapshot diff | Closed | 2026-10-23 | sprint-1.4 |
| 1.5 | Object search and inspection | Closed | 2026-10-23 | sprint-1.5 |
| 1.6 | Simple inspections | Closed | 2026-10-23 | sprint-1.6 |
| 1.7 | Live counters and timed snapshots | Closed | 2026-10-23 | sprint-1.7 |
| 1.8 | gcdump support | Closed | 2026-10-24 | sprint-1.8 |
| 1.9 | Phase 1 hardening | Closed | 2026-10-24 | sprint-1.9 |
| 2.1 | Reverse references | Closed | 2026-10-24 | sprint-2.1 |
| 2.2 | Retention paths | Closed | 2026-10-24 | sprint-2.2 |
| 2.3 | Dominator tree and retained size | Closed | 2026-10-25 | sprint-2.3 |
| 2.3.5 | Cross-platform and sharing decision | Closed | 2026-10-25 | sprint-2.3.5 |
| 2.3.6 | Avalonia port | Closed | 2026-10-26 | sprint-2.3.6 |
| 2.3.7 | Port peer review | Closed | 2026-10-26 | sprint-2.3.7 |
| 2.4 | Retention explorer UI | Closed | 2026-10-26 | sprint-2.4 |
| 2.5 | Allocation tracking | Closed | 2026-10-26 | sprint-2.5 |
| 2.6 | Smart leak detectors | Closed | 2026-10-27 | sprint-2.6 |
| 2.7 | Navigation and polish | Closed | 2026-10-27 | sprint-2.7 |
| 3.1 | Object sets | Closed | 2026-10-28 | sprint-3.1 |
| 3.2 | Retention views | Closed | 2026-10-28 | sprint-3.2 |
| 3.3 | Comparison depth | Closed | 2026-10-29 | sprint-3.3 |
| 3.4 | Live workflow and housekeeping | Closed | 2026-10-29 | sprint-3.4 |
| 3.5 | Leak detection depth | Closed | 2026-10-29 | sprint-3.5 |
| 3.6 | Urgent fixes | Closed | 2026-10-29 | sprint-3.6 |
| 3.7 | Hardening | Closed | 2026-10-30 | sprint-3.7 |
| 3.8 | Modularity | Closed | 2026-10-30 | sprint-3.8 |
| 3.9 | Packaging and distribution | Closed | 2026-11-01 | sprint-3.9 |
| 3.10 | Visual design and README | Closed | 2026-11-05 | sprint-3.10 |
| 3.11 | Screens that explain themselves | Closed | 2026-11-10 | sprint-3.11 |
| 3.12 | First-show performance and refactor analysis | Closed | 2026-11-15 | sprint-3.12 |
| 3.13 | Tutorial | Closed | 2026-11-15 | sprint-3.13 |
| 3.13.2 | Dump values in logs, by consent | Closed | 2026-11-19 | sprint-3.13.2 |
| 4.1 | Capture on Linux | Closed | 2026-11-20 | sprint-4.1 |
| 4.1.1 | Hide processes HeapScope cannot capture | Closed | 2026-11-21 | sprint-4.1.1 |
| 4.2 | Capture on macOS | Closed | 2026-11-23 | sprint-4.2 |
| 4.2.1 | Patch: damaged gcdumps and traces | Closed | 2026-11-24 | sprint-4.2.1 |
| 4.3 | Live session | Closed | 2026-11-25 | sprint-4.3 |
| 4.4 | Allocated between two snapshots | Closed | 2026-11-25 | sprint-4.4 |
| 4.5 | Peer review | Closed | 2026-11-25 | sprint-4.5 |
| 4.6 | Right answers | Closed | 2026-11-25 | sprint-4.6 |
| 4.7 | Runtimes, Linux and large heaps | Closed | 2026-11-26 | sprint-4.7 |
| 4.8 | Ready to release | In Progress | | |
| 4.9 | Workspaces | Planned | | |
| 4.10 | Analysis tabs | Planned | | |
| 4.11 | Sunburst | Planned | | |
| 4.12 | WPF leak detectors (optional) | Planned | | |

---

## Sprint 3.9: Packaging and distribution

Closed 2026-11-01; its section is in [sprints/phase-3.md](sprints/phase-3.md) and its review in
[reviews/sprint-3.9.md](reviews/sprint-3.9.md). ADR-039 records the distribution decisions.

---

## Sprint 3.10: Visual design and README

Closed 2026-11-05; its section is in [sprints/phase-3.md](sprints/phase-3.md) and its review in
[reviews/sprint-3.10.md](reviews/sprint-3.10.md). ADR-040 records the visual design.

---

## Sprint 3.11: Screens that explain themselves

Closed 2026-11-10; its section is in [sprints/phase-3.md](sprints/phase-3.md) and its review in
[reviews/sprint-3.11.md](reviews/sprint-3.11.md). ADR-040 records the info icons, and ADR-032 the match of pinned objects.

## Peer review sprints

3.6, 3.7 and 3.8 worked through `docs/reviews/peer-review-3.5.md`; all closed ([sprints/phase-3.md](sprints/phase-3.md)).

---

## Phase 4

Planned 2026-11-18 with the product owner from the Phase 4 candidates (what Phase 3 leaves out). Each sprint adds its
scenario to the sample and its lesson to the tutorial where it has one (sprint 3.13's extension contract, ADR-042).
Owner decisions, 2026-11-18:
- No Mac to test on: macOS is built and checked on Linux as far as it can be, and marked untested, like the macOS packages.
- The same autonomy as Phase 3: green sprint PRs are merged and tagged and the next sprint starts; costly or irreversible
  steps, user-facing wording and releases still go to the owner. At the end of the phase, stop and report.
- Releases after 4.2 (capture everywhere), after 4.4 (the live session and Compare) and at the end of the phase. 2026-11-25: 4.5.0 is released
  in place of 4.4.0, as a pre-release, and its CLI goes to nuget.org; the first full release comes after 4.8 and a trial by people outside.
- A peer review after 4.4, before the UI-heavy sprints, like 3.5's.
- The checks by hand (DEVELOPER.md 5) are automated as far as they can be, first, in 4.1.

| Sprint | Title | Size | What |
|---|---|---|---|
| 4.1 | Capture on Linux | L | Process list, full dumps and gcdumps, live counters and allocation recording on Linux, through the runtime's diagnostics channel; permission and ptrace rules; the tutorial on Linux; the checks by hand automated. |
| 4.1.1 | Hide processes HeapScope cannot capture | S | Owner, 2026-11-20: the Processes list hides the processes that need more rights than HeapScope has ("Needs administrator", on Linux "Needs root or its user"), with a check box to show them; when HeapScope runs elevated (as root) it can capture them, so all are shown. The list keeps marking which processes are elevated or another user's. |
| 4.2 | Capture on macOS | M | The same on macOS, built and checked on Linux, marked untested. |
| 4.2.1 | Patch: damaged gcdumps and traces | S | Owner, 2026-11-24: the nightly fuzz test failed on 2026-11-20, 22 and 23 (seeds 20, 22 and 23). A gcdump states the node its root tree starts at, and the loader did not check it: the reader's own error refused such a file, until the JIT had inlined the accessor that throws after many loads in one process, and then it was reported as a HeapScope error. Fixed and on `develop` (48,000 mutated files over 8 seeds since, none failing). Added 2026-11-24 (owner): a trace states sizes and counts too, and they are checked before it is read; a failure on its events is a refusal. TraceEvent is 4.0.2. |
| 4.3 | Live session | L | Allocations streamed beside the Live timeline from a trace read while it grows (ADR-037), and snapshots faster than a full dump. |
| 4.4 | Allocated between two snapshots | M | In Compare, from the trace recorded between them (from 2.5; ADR-028). |
| 4.5 | Peer review | M | A review of the phase so far, like 3.5's; its fixes plan the next sprints. |
| 4.6 | Right answers | L | Peer review 4.5 (the split agreed by the owner on 2026-11-25): H2, H3, H4, H9, H10, H11, H16, H19, M6 to M9, Compare's retained columns when only one side's GC handles were checked, and a cached load whose handles could not be checked tried again when its cause is gone. Also the trace of a run that had no bytes after 60 s in CI (3 times since 2026-11-20, `A_run_records_an_allocation_trace_for_its_whole_length`, which says its state since 4.5): a test or a start that hangs (near H9); and the bookmarks status of a damaged file not shown within 10 s once on Linux CI (`A_damaged_bookmarks_file_is_said_when_the_snapshot_opens`). |
| 4.7 | Runtimes, Linux and large heaps | L | Peer review 4.5: H1, H5 to H8, H12, M11. |
| 4.8 | Ready to release | L | Peer review 4.5: H14, H15, H17, M1 to M5, M13, M14; then a trial by people outside, then the first full release. |
| 4.9 | Workspaces | L | Peer review 4.5: H13 and M15, what analysis tabs need. |
| 4.10 | Analysis tabs | M to L | Several analyses of a snapshot side by side, each with its own path (from 2.7; peer review P21 and the Live page split). |
| 4.11 | Sunburst | S to M | (Peer review 4.5 M16) A sunburst view beside the icicle chart for dominators and call trees. |
| 4.12 | WPF leak detectors (optional) | M | (Peer review 4.5 M12) With a WPF fixture app (from 2.6): binding to a non-INotifyPropertyChanged source, collection bindings, dependency property descriptors, x:Name. Dropped if time runs short. |

---

## Sprint 4.8: Ready to release

**Goal:** someone outside can install HeapScope, get help, recover from the common dead ends (a missing DAC, sudo on Linux, a stuck
worker), trust what a release contains and script its CLI output, so that a trial by people outside can start; the first full release
follows it when the owner says. Peer review 4.5: H14, H15, H17, M1 to M5, M13, M14; from the Backlog: the memory the drop before the
mapped switch does not return, a note when a load's references fall back to memory, `ColumnBytes` split.

**Tasks**
1. [x] H14: one place to get help. GitHub Issues on the repository, with issue templates (a bug, a wrong result, a question) and a
   `SUPPORT.md`; a "Getting help" section in README and INSTALL; every message that says to contact support names that page and the
   error's id, and says what the log holds and how to check it before attaching it (CONTRIBUTING and INSTALL agree). Wording drafts for
   the owner. Tests: an architecture test that no user-facing text says "support" without the page; the templates' fields.
2. [x] H15: a missing DAC is not a dead end. The app's Settings page gets the DAC folder (picked or typed) and the opt-in Microsoft
   symbol server, saved where the worker reads them; the missing-DAC message names that page in the app and the options in the CLI;
   INSTALL says where the DAC is found and how to copy one. Tests: the settings round trip, the worker reading them, the message per front end.
3. [x] H17: sudo on Linux works as written. The advice names the full path (`secure_path` drops the user's `PATH`), and HeapScope run
   under sudo keeps the invoking user's data folder and gives the files it writes (dumps, sessions) to that user (`SUDO_UID`), so a
   dump of a service is readable afterwards. Tests: the texts; the owner change on Linux CI (as root in the container).
4. [x] M1: a release is built and run before it is public. The workflow creates a draft, builds every package, runs each archive it
   can run (Windows and Linux: `--version`, open a fixture dump), refuses to replace an existing tag's assets, attests only the
   assets, then publishes. Tag protection and immutable releases are repository settings (owner, listed). Tests: the workflow's dry run
   on a test tag in a fork-free way (workflow_dispatch with publish off).
5. [x] M2: THIRD-PARTY-NOTICES lists the versions shipped, and a test compares it with the lock files. Test: `Notices_list_the_shipped_versions`.
6. [x] M3: opening an untrusted dump runs the dump's runtime's DAC, native code, with the user's rights: security-privacy.md and INSTALL
   say so (draft), and opening a dump while elevated adds a note. Sandboxing the reader stays in the Backlog (L). Tests: the note.
7. [x] M4: CLI output is a versioned contract: every `--json` output carries `"format": 1`, docs/cli-output.md says what may change
   within a format and what bumps it. Tests: each command's JSON has the field; a contract test per command's top-level shape.
8. [ ] M5: the worker. A crash banner stays until the worker is back or dismissed; "Restart worker" for a stuck one (also the CLI's
   timeout message names it). One process for live and snapshots stays (Backlog, with its reason). Tests: the banner's life; restart.
9. [ ] M13: the support paths, each small: the app's crash path writes a report it offers; Copy details carries version, system, id;
   a log that cannot be written is said once; every failure shown has its log line; the worker's first start says what it is doing
   and is measured; Purge does what its text promises; README's path to a first answer; INSTALL's Linux steps checked on a clean
   container. Tests: per item.
10. [ ] M14: CI and the project's single points: the required check confirmed, the known flaky tests (CounterMonitor, the gcdump cancel,
    the load cancellation, the churn recording start) made deterministic or bounded, a .NET servicing policy in DEVELOPER.md, and the
    token and org's single points written down with what the owner holds. Tests: the flaky tests 20 times in a loop without a failure.
11. [ ] From the Backlog: the memory the drop before the mapped switch does not return (find what holds it; `load-dump` back near
    3.12's 34 MB), a snapshot note when a load's references fell back to memory (draft wording), `ColumnBytes` split into private and
    file-backed for the memory mode's log. Tests: `load-dump`'s private bytes after the drop; the note; the split.
12. [ ] The trial: docs/trial.md, what to try, how to report, and what is known not to work; the release checklist for the first full
    release in DEVELOPER.md 7. The trial itself and the release are the owner's.

**Decisions**
- Owner, 2026-11-26 (asked: it changes ADR-017's 1.2 amendment): the symbol server opt-in is per user, on the Settings page and
  `heapscope-cli settings --symbol-server on --yes`, off by default and on only after the user confirms; ADR-017 amended. The DAC
  folder is per user too (`--saved-dac-folder` on the command line, since the run's `--dac-folder` is taken); the command line and
  appsettings win over both.
- Decided autonomously (owner to confirm): the help channel is GitHub Issues on klundt-org/HeapScope, with templates, not
  Discussions or email; a log may be attached after the user checks it (it holds paths and type names, filter text only when the
  user turned it on).
- Decided autonomously (owner to confirm): no macOS runners stays (owner's rule) although the reviewer notes they are free for public
  repositories; the macOS packages stay untested.
- Decided autonomously: repository settings (tag protection, immutable releases, required checks) are the owner's; the sprint
  drafts them in DEVELOPER.md 7 and the owner applies them.
- Decided autonomously: the JSON output's version is a top-level `"format": 1`, additive only within a format. Built as `"Format": 1`,
  the first member, in PascalCase like every other member; a list (`inspect` without a name) became the object's `Inspections`.
- Decided autonomously: under sudo, HeapScope gives what it writes to `SUDO_UID` and keeps that user's data folder.
- Decided autonomously: one worker process for live and snapshots stays; a second process is Backlog.
- Decided autonomously: user-facing texts are built as drafts (docs/design/info-text.md "Sprint 4.8 additions") for the owner.

---

## Sprint 4.7: Runtimes, Linux and large heaps

Closed 2026-11-26; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.7.md](reviews/sprint-4.7.md). ADR-002, ADR-015, ADR-020, ADR-025, ADR-033, ADR-034, ADR-037 and ADR-041 are amended for it.
---

## Sprint 4.6: Right answers

Closed 2026-11-25; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.6.md](reviews/sprint-4.6.md). ADR-020 is amended for it.

---

## Sprint 4.5: Peer review

Closed 2026-11-25; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/peer-review-4.5.md](reviews/peer-review-4.5.md). ADR-010, ADR-034 and ADR-041 are amended for it.

---

## Sprint 4.4: Allocated between two snapshots

Closed 2026-11-25; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.4.md](reviews/sprint-4.4.md). ADR-028 is amended for it.

---

## Sprint 4.3: Live session

Closed 2026-11-25; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.3.md](reviews/sprint-4.3.md). ADR-037 is amended for it.

---

## Sprint 4.2.1: Damaged gcdumps and traces

Closed 2026-11-24; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.2.1.md](reviews/sprint-4.2.1.md). ADR-028 and ADR-037 are amended for it.

---

## Sprint 4.2: Capture on macOS

Closed 2026-11-23; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.2.md](reviews/sprint-4.2.md). ADR-025 and ADR-014 are amended for it.

---

## Sprint 4.1.1: Hide processes HeapScope cannot capture

Closed 2026-11-21; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.1.1.md](reviews/sprint-4.1.1.md). ADR-014 is amended for it.

---

## Sprint 4.1: Capture on Linux

Closed 2026-11-20; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.1.md](reviews/sprint-4.1.md). ADR-025 is amended for capture on Linux.
