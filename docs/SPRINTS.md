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
| 4.2 | Capture on macOS | In Progress | | |
| 4.3 | Live session | Planned | | |
| 4.4 | Allocated between two snapshots | Planned | | |
| 4.5 | Peer review | Planned | | |
| 4.6 | Analysis tabs | Planned | | |
| 4.7 | Sunburst | Planned | | |
| 4.8 | WPF leak detectors (optional) | Planned | | |

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
- Releases after 4.2 (capture everywhere), after 4.4 (the live session and Compare) and at the end of the phase.
- A peer review after 4.4, before the UI-heavy sprints, like 3.5's.
- The checks by hand (DEVELOPER.md 5) are automated as far as they can be, first, in 4.1.

| Sprint | Title | Size | What |
|---|---|---|---|
| 4.1 | Capture on Linux | L | Process list, full dumps and gcdumps, live counters and allocation recording on Linux, through the runtime's diagnostics channel; permission and ptrace rules; the tutorial on Linux; the checks by hand automated. |
| 4.1.1 | Hide processes HeapScope cannot capture | S | Owner, 2026-11-20: the Processes list hides the processes that need more rights than HeapScope has ("Needs administrator", on Linux "Needs root or its user"), with a check box to show them; when HeapScope runs elevated (as root) it can capture them, so all are shown. The list keeps marking which processes are elevated or another user's. |
| 4.2 | Capture on macOS | M | The same on macOS, built and checked on Linux, marked untested. |
| 4.3 | Live session | L | Allocations streamed beside the Live timeline from a trace read while it grows (ADR-037), and snapshots faster than a full dump. |
| 4.4 | Allocated between two snapshots | M | In Compare, from the trace recorded between them (from 2.5; ADR-028). |
| 4.5 | Peer review | M | A review of the phase so far, like 3.5's; its fixes plan the next sprints. |
| 4.6 | Analysis tabs | M to L | Several analyses of a snapshot side by side, each with its own path (from 2.7; peer review P21 and the Live page split). |
| 4.7 | Sunburst | S to M | A sunburst view beside the icicle chart for dominators and call trees. |
| 4.8 | WPF leak detectors (optional) | M | With a WPF fixture app (from 2.6): binding to a non-INotifyPropertyChanged source, collection bindings, dependency property descriptors, x:Name. Dropped if time runs short. |

---

## Sprint 4.2: Capture on macOS

**Goal:** what HeapScope captures on Windows and Linux (the process list, full dumps, gcdumps, live counters, allocation
recording, the tutorial's sample) is built for macOS, with refusals that say what to change. It is built and checked as
far as Windows and Linux allow, and marked untested: nobody has run it on a Mac.

**Starting point.** The diagnostics channel is the runtime's own on macOS too. What is missing: the facts about a process
(macOS has no `/proc`; `Process.Modules` gives the main executable only), the size estimate, another user's mark, the
rules that explain a failure, and the texts. The per-system split of 4.1 is partial (4.1 review M6, 4.1.1 review L18).

**Tasks**
1. [ ] The split made whole (M6, L18): `HostSystem` in Core in place of `bool linux` in `ElevationTexts` and
   `PlatformHints`; the effective user id and the readable bytes behind `IProcessFacts`; the rules that explain a failure
   behind one interface per system (`ICaptureRules`); a system without a reader refuses instead of getting Windows' facts;
   `PlatformHints.CanCapture` taken from Application. No behavior changes on Windows or Linux. Tests: the facts and rules
   chosen per system, every text per system, `CanCapture` equal to the capture layer's answer.
2. [ ] macOS facts (`MacProcessFacts`): the image path, effective user id and mapped regions through libproc, the command
   line and environment through `sysctl` (`KERN_PROCARGS2`); the runtime's folder from the mapped `libcoreclr.dylib`; the
   readable regions for the size estimate. The buffers are parsed by pure functions (`MacProcData`) over bytes. Tests: the
   parsers on built buffers (short, cut, padded, hostile counts) on every system; on macOS, the test process itself.
3. [ ] Elevation on macOS: the rule on effective user ids of 4.1, with the ids from task 2. Tests: the rule and the probe's
   source per system.
4. [ ] Refusals on macOS (`MacCaptureRules`): createdump's `task_for_pid` failure (signing and entitlements), a folder the
   target's account cannot write, a full disk, diagnostics turned off, another `TMPDIR` (each macOS user has their own),
   another user's process; each with its evidence for the log. Wording drafted for the owner. Tests: each rule on text.
5. [ ] The DAC beside a dump on macOS: `libcoreclr.dylib` copied with `libmscordaccore.dylib`, sizes allowed for. Tests: the
   copied names per system.
6. [ ] Capture turned on for macOS: `IsSupported`, the empty pages, tips and the Learn page, marked untested where a user
   reads it before relying on it. Drafts for the owner. Tests: the texts per system.
7. [ ] A check a Mac owner can run: `build/linux-capture-check.sh` runs on macOS too (its name and messages say "capture
   check"); INSTALL.md and the issue link say how to report it. CI's Linux job keeps running it.
8. [ ] Docs: ADR-025 amended, ARCHITECTURE and its pages, logging, security-privacy, DEVELOPER, README, INSTALL, USER-GUIDE,
   the CLI's README, info-text.md (drafts), Backlog (what stays unconfirmed, and the AllocationTraceTests CI failure of
   2026-11-21).

**Decisions**
- No Mac and no macOS runners: built and checked on Linux, marked untested (owner, 2026-11-18 and 2026-10-31).
- The structs and constants come from Apple's published headers (XNU `proc_info.h`), read 2026-11-23. Every read checks the
  size the system returns and gives nothing on a mismatch, so a wrong layout loses a detail and never a listing. Decided
  autonomously.
- Where a source could not be read (the macOS dump writer's own messages, how a region walk ends), the code assumes the
  least: an unknown message gets the generic text, and a walk stops at the first failed or non-advancing read. Decided
  autonomously.
- Another user's process follows 4.1's rule (run as that user, or as root); its temp folder differs on macOS, so the refusal
  names it and what to start HeapScope with. No search of other users' folders. Decided autonomously.
- HeapScope never signs or re-signs a target or changes its entitlements; a refusal says what the runtime reported. Decided
  autonomously.
- No new sample scenario or lesson: 4.1's lesson note gains macOS wording (a draft for the owner). Decided autonomously.

---

## Sprint 4.1.1: Hide processes HeapScope cannot capture

Closed 2026-11-21; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.1.1.md](reviews/sprint-4.1.1.md). ADR-014 is amended for it.

---

## Sprint 4.1: Capture on Linux

Closed 2026-11-20; its section is in [sprints/phase-4.md](sprints/phase-4.md) and its review in
[reviews/sprint-4.1.md](reviews/sprint-4.1.md). ADR-025 is amended for capture on Linux.
