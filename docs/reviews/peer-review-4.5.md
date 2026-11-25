# Peer review: HeapScope 4.4.0, the whole code base

A review of everything built through sprint 4.4 (develop `dd848c7`), like 3.5's, before the UI-heavy sprints and a first full release.
Five fresh reviewers read the code base, not a diff, in parallel on 2026-11-25: solution architect (SA), senior developer (SD), CLR and
GC specialist (GC), support engineer (SE) and DevSecOps (DSO, a general reviewer with a read-only brief). The lead engineer merged
overlapping findings and checked the Critical and High ones: "verified" marks what was confirmed in the code, by measurement or by
query. "Knowledge" marks what rests on the CLR's or a library's behavior.

Critical: wrong results, data loss, security exposure, or a blocker. High: real cost now. Moderate: worth doing soon. Low only where
worth mentioning. Sizes: S, M, L. The split into sprints is the owner's decision; the last section proposes one.

## Critical

- **C1 GC handles are missing in dumps of Server GC processes, and what only they hold looks like garbage** (GC; verified by
  measurement). The runtime keeps a handle table per processor and its debugging library walks one per GC heap. On 24 processors: with
  DATAS (the default with Server GC on .NET 9 and 10) at 18 heaps, 8,988 of 12,000 strong handles; with a fixed heap count of 2, 200 of
  2,400. So it is any Server GC process with fewer heaps than processors, as most ASP.NET Core services are: pending I/O held by pinned
  and async handles, ConditionalWeakTable values and GCHandles looked unreachable, were left out of the reachable-only detectors and
  retained nothing, without a note. In every release so far. **Fixed in this sprint**: a second walk with a larger heap count
  shown, every added handle checked; loader version 5, so caches are rebuilt; a note when it cannot be told; `HandleTablesTests`
  against counts the process itself gives, and `build/check-handles.sh` on Linux in CI. Review of the fix: below.

## Security, fixed in this sprint

- **C2 "Show in folder" could run a planted program** (DSO, as High; verified in the code, and by the reviewer by running it). It started
  `explorer.exe` (`open` on macOS) by its bare name, which is looked for in the current folder first, where a file of that name may lie
  beside a dump. **Fixed**: full paths, and `Programs_are_started_by_their_full_paths` keeps it so.

## High

Results:
- **H1 Only .NET 10 is tested, while README and ADR-002 promise .NET 5 and later** (SA, GC; verified). Every fixture, the sample and CI
  capture run on net10.0. Never checked: statics and thread statics in their older storage, segments, the detectors' private field names,
  counters and the gcdump flush on .NET 6 to 9. Multi-target the fixtures, run the known answers per runtime; say on open when a runtime
  is untested; narrow the claim until then. M.
- **H2 A detector that cannot read its fields says "0 findings"** (GC; SE as Moderate for "0 found" when nothing was compared; verified).
  Unreadable counts go to the log; the answer carries a note only for one detector. A clean result must mean "checked". S to M.
- **H3 Statics the loader cannot name read as GCHandles** (GC; verified). Closed generic statics and struct statics: why alive blames
  `GCHandle.Alloc`, and the static collections detector says 0. Keep "static storage" as the detail; name boxed and generic statics. S to M.
- **H4 Grids keep the previous snapshot's rows and totals under the new one** (SD; verified), and for good when the new query fails; opening
  a row or an export then mixes two snapshots. Types, Compare, Trend. Clear on a new subject. S to M.

Large heaps:
- **H5 8 GB was never measured** (GC; known, Backlog). The estimate for 100M objects is 5.2 GB for the first load, over the 4 GB budget on
  the 16 GB baseline, and the worker does not check a load. Measure one; edges straight into the cache; a load check. S to M.
- **H6 Inspections run outside the memory reservations, and Duplicate strings is unbounded** (GC; verified). 20M strings in pairs keep
  1.3 to 2.8 GB. M.
- **H7 Off Windows the memory budget is the last collection's reading** (SA as High, GC as Moderate; verified), 0 before the first, so
  in-memory mode is chosen where Windows would map; on macOS the worker's own memory reads 0. S to M.
- **H8 A trace at its own default limit cannot be opened beside two snapshots on 16 GB** (SA; verified by the constants: 1 GB limit, 96 MB
  plus 3 bytes per byte), which "allocated between" needs. Two readers of allocations, the costlier for the finished trace. S (the limit), then L
  (one streaming reader).

Holding up:
- **H9 Stopping a recording of a frozen process hangs** (SD; verified; the unanswered stop is knowledge). `StopAsync` without a bound in
  `TraceRecorder` and `GcTrigger` (`CounterMonitor` bounds it); a breakpoint in the debugger is enough. With it, closing the app can leave
  an invisible process. S.
- **H10 Synchronous file and process work on the UI thread** (SD; verified). `JobTracker` runs a job's start on the caller's thread: opening a
  dump reads the trust list, Record allocations takes the session's lock (up to 30 s). S.
- **H11 The trace's disk guard looks at the wrong disk off Windows, and gives up on one error** (SD; verified). S.
- **H12 The integration tier never runs on Linux, and the app's window was never shown there** (SA, SE, SD; verified), while README says
  "Windows and Linux: everything". Integration tests on net10.0, xvfb in CI, one real desktop. M.
- **H13 Analysis tabs (4.6) do not fit the page model** (SA): one set of views re-pointed, paths per snapshot, links without a workspace, 32
  result sets. An extraction sprint first. L.

Users:
- **H14 There is no place to get help** (SE; verified). Messages say "send HeapScope's log to support"; none exists, and CONTRIBUTING and INSTALL
  say not to attach logs. One channel, one section, issue templates. S.
- **H15 A missing DAC is a dead end in the app** (SE; verified). The message names settings the Settings page does not have and INSTALL does not
  document. M.
- **H16 A mistake in `appsettings.json` crashes every CLI command** (SE; verified in the code), with nothing in the log. S.
- **H17 The sudo advice on Linux does not work as written** (SE): `secure_path`, root's library, and a dump owned by the service. S to M.

Supply chain and release:
- **H18 Secret scanning and push protection are off** (DSO; verified), probably since the move to the org. S, owner. The owner turned on secret
  scanning and CodeQL on 2026-11-25 (push protection: the API still said off); CodeQL runs from `codeql.yml` since, with one rule off (DEVELOPER.md 7).
- **H19 The nightly fuzz failed 15 of its first 23 nights and nobody acted** (DSO; verified; the records name three). Fixed in 4.2.1; nothing
  would tell of the next. S.
- **H20 nuget.org has 3.13.2 while INSTALL names it first** (DSO; verified), without the untrusted-input fixes of 4.2.1. Upload now (owner); a
  publish job (M). The owner uploaded 4.2.1 on 2026-11-25; 4.5.0, which fixes C1, follows its
  release.

## Moderate

- **M1** A release is public before it is built, and the archives are never run; tags and assets can be replaced; provenance is too
  broad (DSO). M.
- **M2** THIRD-PARTY-NOTICES lists older versions than shipped, and nothing checks it (DSO). S.
- **M3** Opening an untrusted dump runs native code with the user's rights, elevated too, and no document says so (DSO). S to L.
- **M4** CLI output is the wire contract, unversioned (SA). M, free before outside users.
- **M5** The worker: a crash banner that goes at once, no Restart worker for a stuck one, one process for live and snapshots (SA). S to M.
- **M6** Five hand-written JSON file protocols; lesson progress can be lost (SD). M.
- **M7** Views that latch "started" and offer no retry (SD); hidden Compare and Trend query (SD). S each.
- **M8** A crash racing a restart breaks the error contract (SD); raw event raises in `SampleService` (SD); a Processes page recording does not
  mark its session in use (SD). S each.
- **M9** "Unreachable" stated as fact although stacks of a running process are not complete (GC). S, wording.
- **M10** Loader caches from before 3.12 lacked LoaderAllocator references (GC). **Fixed** with loader version 5.
- **M11** Thread statics have no fixture (GC). M. The reverse index without a cache is unreserved; the capture's disk reserve is too small for the
  caches; counter intervals are trusted (GC). S each.
- **M12** The capability model stops at snapshots and does not know "applies" (SA), which 4.8 needs. M.
- **M13** The app's crash path, Copy details, a log that cannot be written, failures shown without a line, the worker's 30 s first start, purge's
  promise, the path to a first answer, INSTALL's Linux steps (SE). S each.
- **M14** Required checks may be stale; flaky tests recur; the token and the org are a single point of failure; macOS runners are free for a
  public repository; no .NET servicing policy (DSO). S to M, several the owner's.
- **M15** Classes grow back where features land; sprint and review ids in about 1,600 comments; tests without a UI thread (SA, SD). M.
- **M16** The sunburst is sized without a layout for call trees (SA). S to cut, M to do.

## Low

The bundles the reviewers list: job handles kept, a bad path that leaves the page opening, Purge all's bookmarks on screen, first runtime
taken silently, no static analysis, drifted security documents, the hand-kept protocol number.

## What stands between this and a first full release

All five, in other words: the runtimes it claims (H1), Linux as tested as Windows (H12), a clean result that means checked (H2), one
measured 8 GB dump (H5), a way to get help (H14), a release path that builds before it publishes (M1), and people outside who have
used it on their own services. Owner decisions: signing, macOS, what "full" promises.

## The fix of C1, reviewed

Reviewed by the CLR and GC specialist on 2026-11-25. The approach was judged right and unlikely to add a wrong handle; three High findings
about what it claimed (Complete without proof, Recovered for a partial walk, one bad handle discarding all after it), four
Moderate (the recorded processors as a bound, memory on millions of handles, static storage rooted twice, Unknown as a note only) and
three Low, all fixed and measured again: with DATAS at 8 heaps 785 of 2,400 by the runtime's own walk and all by HeapScope's; with 2
heaps 192 and 206, all found, with the processors known and unknown. Below .NET 10 it is untested like the rest (H1). A fourth round found
two Moderate (a place that changes nothing taken as the count; the CI check could pass without testing), both fixed.

Reviewed by the other three for their areas on 2026-11-25:
- Support engineer: 1 High (every other answer still called such objects garbage) and 5 Moderate (no step out of a kept doubt, no reason
  in the log, no counts by kind, a wrong step after an older cache, a note without a step).
- Solution architect: 1 High (growth measured between a snapshot that lacks handles and one that has them) and 5 Moderate (the same
  contradiction; the fact's polarity and two sources; no ADR; no way to turn the walk off; documents).
- Senior developer: 6 Moderate (the contradiction; the search's cost neither bounded nor measured; one heap untested; cancellation in one walk of
  three; failures swallowed; pure rules only in the heavy job) and Low ones.

All fixed: `UnreachableNote(SnapshotInfo)`, the positive fact `HandlesChecked` as the one rule, detectors' notes and a refused baseline (unless both sides have it)
(`UncheckedHandlesTests`), reasons, counts, walks and seconds in the log, `Worker:SecondHandleWalk`, at most 1,024 walks, every walk
cancellable, a pinned one-heap case (103 of 2,400 by the runtime's own walk, all found), the pure rules and readers tested on every system
(Loaders.ClrMd.Tests), ADR-010 amended. Left for 4.6: Compare's retained columns when only one side has the fact (the result needs a field
for it). Confirmed by all four on 2026-11-25. The confirmations left two Moderate (growth between two snapshots that both
lack handles; a step, "Rebuild", that the app does not have), fixed as above and in the note, and Low ones. Fixed of these: the fact's bit
(5, since 4 meant the opposite in this sprint's first commits), a silent exit of check-handles.sh, whole walks to learn "none", a test of the
walk turned off. Left: the walk limit is no time limit, and what a limit stops is not kept; a cached `Unknown` is not tried again by itself
(Backlog, with 4.6).

## Done well

Layering that is enforced and real; the reopen protocol; result sets and leases; the loader version rule; refusals with evidence; capture
refusals on Linux; deletion that plans, confirms and follows no link; pinned actions, read-only tokens, no vulnerable package;
dominators, retained sizes and the object walk as the CLR has them; the sampled figures said to be estimates.

## The split (the owner agreed on 2026-11-25)

1. **4.6 Right answers**: H2, H3, H4, H9, H10, H11, H16, H19, M6 to M9, Compare's retained columns (above), and the owner's
   settings (H18, H20).
2. **4.7 Runtimes, Linux and large heaps**: H1, H5 to H8, H12, M11.
3. **4.8 Ready to release**: H14, H15, H17, M1 to M5, M13, M14; then a trial by people outside, then the first full release.
4. **4.9 Workspaces** (H13, M15), **4.10 Analysis tabs**, **4.11 Sunburst** (M16), **4.12 WPF leak detectors** (M12).
