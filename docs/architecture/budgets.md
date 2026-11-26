# HeapScope architecture: Scale targets and budgets

Part of [ARCHITECTURE.md](../ARCHITECTURE.md); the section numbers are the same as before the split (sprint 3.8), so
references such as "ARCHITECTURE.md 10" still name the right section.

## 10. Scale targets and budgets

Tiers:
- **Small**: dump under 1 GB, about 5M objects.
- **Medium**: dump up to about 8 GB, about 50M objects. Baseline developer laptop: 16 GB RAM (mapped mode
  when tight). With 32 GB or more, snapshots open fully in memory.
- **Large**: 8 GB+, 100M+ objects. Backlog.

Initial budgets (sprints refine them with measurements):

| Budget | Target | Measured |
|---|---|---|
| Medium (~4 GB, ~50M objects) first load on a 16 GB laptop | < 3 min | 13.0 to 13.3 s on the fixture Medium dump (3.12, `load-dump`, 32 GB machine, after task 8); 35.1 to 48.2 s in 3.10; 15.8 s in 1.2; about 20 s end to end in the UI smoke run |
| Medium reopen from index cache | < 20 s in memory; < 2 s mapped | 0.6 s in memory, 0.6 s mapped warm; 0.8 s and 1.0 s cold (0.5, synthetic 50M); 0.6 s for the fixture Medium (1.2) |
| Worker peak memory, Medium, in-memory mode (machines with room, e.g. 32 GB) | < 1.5x the dump's managed heap size | 2.4 GB for a 3.36 GB managed heap, 0.71x (1.2) |
| Worker peak memory, Medium first load on a 16 GB laptop (graph, builder buffers and ClrMD caches) | < 4 GB | 2.4 GB (1.2, fixture Medium at 1.14 references per object) |
| Worker private memory, Medium, mapped mode (after load) | < 1 GB | +9 MB private, +202 MB working set for the mapped graph (sprint 0.5, synthetic 50M) |
| UI dispatcher block | never > 100 ms | every UI sprint |
| Slow UI operations (over 100 ms) in a smoke open run, cold first shows included | at most 12 (exit 7 above; measured 0 to 4, 5 to 10 under load, sprint 3.12) | every UI sprint |
| Type summary and diff on loaded Medium | < 5 s | type aggregation 0.05 s on Medium, 0.03 s on DenseHeap (54M objects); CLI `summary` end to end from the index cache 1.9 s and 2.0 s (1.3); CLI `diff` of Medium against DenseHeap end to end from the index caches 2.8 to 2.9 s, of which the aggregations 0.05 to 0.08 s each (1.4) |
| Phase 2 indices (reverse references, semi-NCA dominators, retained sizes; prototype, 1.9) | under 60 s and within the 4 GB worker budget on a Medium-scale real heap | Medium 9.5 s, peak 3,514 MB (met in memory); DenseHeap 12.9 s, peak 4,119 MB in memory (missed by 23 MB), 2,360 MB with the graph mapped (met); hence computation follows the memory mode (section 6.4) |
| Retention paths (2.2, `retention` on real index caches) | the root path tree within the 60 s Phase 2 budget; a type's key paths and an object's paths interactive (under 5 s) | Medium tree 0.8 to 1.2 s (219 MB), key paths up to 1.7 s (16M objects), an object's paths at most 6 ms, peak 2,802 MB in memory and 1,197 MB mapped; DenseHeap tree 1.6 to 2.3 s, key paths at most 0.4 s, an object's paths at most 176 ms, peak 3,320 MB in memory and 1,416 MB mapped. Met on the 32 GB machine; a 16 GB machine is not measured (section 6.4) |
| Dominators and retained sizes (2.3, `dominators` on real index caches, the worker's paths) | under 60 s for a Medium-scale real heap; Medium within the 4 GB worker budget in both memory modes | After the review: Medium reverse index and dominators 3.9 s in memory (peak 3,289 MB), 5.6 s mapped (peak 699 MB); DenseHeap 4.6 s in memory (peak 3,875 MB) and 7.0 s mapped (peak 661 MB). Met for both in both modes on the 32 GB machine (DenseHeap in memory missed by 189 MB before the review's M4). With the working set capped at 1 GB (a stand-in for memory pressure): Medium 7.9 s, DenseHeap 11.1 s, no thrashing; a real 16 GB machine is a Backlog check. With 2.4's tree children kept (20 bytes per object), on a slower day (2.3.7's code took 8.2 s for Medium's dominators): Medium 6.4 to 6.9 s in memory (peak 3,291 MB), 11.4 s mapped (697 MB); DenseHeap 7.3 s in memory (3,877 MB), 10.6 s mapped (658 MB). Met |
| Reverse references index (2.1, `reverse-index` on real index caches; the Release worker on DenseHeap) | built well inside the 60 s Phase 2 budget; worker within 4 GB | Medium 0.6 to 0.8 s, 470 MB, benchmark peak 2,423 MB in memory and 970 MB mapped; DenseHeap 0.8 to 1.7 s, 831 MB, benchmark peak 3,454 MB in memory and 1,695 MB mapped; worker peak commit 2,644 MB after building DenseHeap's index in memory, plus up to 512 MB of ClrMD cache when fields are named; reopen from the sidecar 0.1 to 1.1 s; releasing the built index 7 to 14 ms. Met on the 32 GB machine; a 16 GB machine is not measured (section 6.4) |
| Compact graph cost | ~30 to 38 bytes per object | 34.0 bytes per object at 3 references (sprint 0.2, synthetic 50M) |

Baseline measured in sprint 0.4 on the fixture sample dumps (`dump-stats`, raw ClrMD walk, no HeapScope graph yet;
reference pass by address as the loader will do it; 24 logical cores, 32 GB RAM, Release). Memory is peak private
bytes (committed memory, the budget's metric); the benchmark's own sorted address list (8 bytes per object) is
included.

| Dump | Size | Objects | Managed heap (live + dead) | References per object | Object pass | Reference pass | Peak private bytes |
|---|---|---|---|---|---|---|---|
| Small | 568 MB | 5.6M | 317 MB | 1.23 | 1.5 s | 1.6 s | 189 MB |
| Medium, ClrMD cache default | 4,022 MB | 57.5M | 3,359 MB | 1.14 | 41 s | 42 s | 3,475 MB |
| Medium, ClrMD cache 512 MB | 4,022 MB | 57.5M | 3,359 MB | 1.14 | 30 s | 51 s | 2,079 MB |

Readings for 1.2: the two ClrMD passes take about 80 s, leaving about 100 s of the 3 minute first-load budget for
reference resolution and the cache write. Uncapped, ClrMD commits about as much as the managed heap; capped at
512 MB the total is about 2.1 GB at the same speed, so graph columns (about 1.55 GB at 1.14 references) plus a capped
ClrMD fit the 4 GB first-load budget. Caveat: the fixture heaps have 1.14 to 1.23 references per object, against the
2 to 4 assumed in 6.2 for real applications; 1.2 also measures a denser heap (for example a dump of a real
ASP.NET Core application under load) before signing off its budgets.

Measured in sprint 1.2 with HeapScope's loader (`load-dump`, Release, 24 logical cores, 32 GB RAM, ClrMD cache 512 MB,
memory-mapped dump reader):

| Dump | Objects | References per object | Object pass | Reference pass | First load (with cache write) | Peak private bytes | Graph columns |
|---|---|---|---|---|---|---|---|
| Small (568 MB) | 5.6M | 1.23 | 1.3 s | 1.0 s | 3.7 s | 227 MB | 142 MB |
| Medium (4,022 MB) | 57.5M | 1.14 | 7.0 s | 6.7 s | 15.8 s | 2,395 MB | 1,457 MB |
| Roslyn compiler server (423 MB, a real application) | 0.35M | 2.01 | 0.4 s | 0.2 s | 1.7 s | 130 MB | 10 MB |
| DenseHeap (4,440 MB; 3.4 GB heap of real Roslyn objects) | 54.1M | 3.03 | 27.5 s | 29.2 s | 64.4 s | 2,636 MB | 1,759 MB |

Remeasured in sprint 3.10, on new dumps (the older ones need a runtime no longer installed): runtime 10.0.14, `load-dump`, Release,
three runs each with desktop applications open, the same machine. Medium (3,984 MB, 54.8M objects): first load 35.1 to 48.2 s,
reopen in memory 0.9 to 1.3 s, peak 2,197 to 2,470 MB. DenseHeap (4,017 MB, 48.6M objects): first load 53.5 to 75.0 s, reopen
1.0 to 1.2 s, peak 2,361 to 2,364 MB. All budgets met. Medium's first load is 2 to 3 times the table's 15.8 s and later's 19 s.
The object pass alone ranged from 13 to 25 s between runs of one dump, and the loader has gained work since; sprint 3.12's
trace settled it (task 8, below): the machine's load and ClrMD's per-object type factory.

The leak fixture heaps are sparse (1.1 to 1.2 references per object). The DenseHeap fixture (`fixtures/DenseHeap`,
captured with `fixtures/scripts/New-DenseDump.ps1`) builds a Medium-size heap at real-application density out of the
Roslyn compiler's own objects: first load 64 s against the 3 minute budget, peak 2.6 GB (0.78x the managed heap)
against 4 GB, 34.1 bytes per object, nothing dropped, mapped transition 1.4 s down to 34 MB private. The benchmark ran
right after the capture (the dump file partly cold); opened through the UI a minute later the worker loaded it in
29.0 s, and reopened it from the index cache in 1.0 s, with the longest dispatcher operation 44 ms.

Cancellation (acceptance criterion: a load stops within 2 s), measured on the DenseHeap dump with
`load-dump --cancel-at <phase>`: canceled as "Opening dump" and "Reading objects" start 0.2 s, "Reading references"
0.8 s (it includes the collection after freezing), "Reading roots" 1.1 s, "Building graph" 1.1 s. `load-dump` checks
the budgets of this section (the 1.5x heap check passes any peak under 512 MB, so small dumps are not failed by fixed
costs) and returns 1 when one is missed; the DenseHeap run met all of them (first load 58.9 s,
peak 2.6 GB, mapped transition 1.0 s down to 33 MB private).

Before opening a dump without an index cache, the Application layer estimates the first-load peak
(`SnapshotMemoryEstimate`: 0.55 bytes per dump byte plus 768 MB, calibrated on the samples above; for the dense dump it
gives 3.2 GB against the measured 2.6 GB; since 4.7 it is Core's `FirstLoadEstimate`, which the worker also reserves while it
loads, less its idle 256 MB, and logs against the load's peak) and warns when it exceeds available physical memory: the UI asks to
continue, the CLI prints the warning (or fails with `--no-overcommit`). `SnapshotSession.GetOpenWarningAsync` holds the
rule for both front ends; it asks the worker (`ProbeSnapshotAsync`) whether a valid index cache exists where the
locator puts it, so a reopen from cache never warns.

Large heaps (sprint 4.7, task 10): an 8 GB DenseHeap dump (9,054 MB file, 6.6 GB managed heap, 106.2M objects, 320.4M
references, 3.02 per object) on claude-dev-2 (12 logical cores, 32 GB, Windows 11, runtime 10.0.15, the worker's GC settings),
before the references went to a file:

| Measure | Result | Budget |
|---|---|---|
| First load (with the cache write) | 37.3 s (objects 8.4 s, references 21.9 s, cache write 4.1 s) | < 180 s, met |
| Peak private bytes, first load | 5,279 MB (0.80x the managed heap); `FirstLoadEstimate` 5,748 MB | < 4 GB, missed; < 1.5x the heap, met |
| Graph columns | 3,451 MB (34.1 bytes per object) | |
| Mapped transition, reopen in memory | 1.1 s (3,092 MB private after the drop), 1.5 s | < 2 s, < 20 s, met |
| Indices in memory (reverse references, dominators) | 12.6 s, peak 7,594 MB | < 60 s met; 4 GB missed (the mode chooses mapped where it would not fit) |
| Indices mapped | 17.8 s, peak 1,287 MB, peak working set 7,060 MB | met |
| Indices mapped, working set capped at 4 GB | 18.9 s, peak 1,287 MB, 4.1M page faults | met |

The peak was the build's copy of the chunked references into one array while the chunks were held (1.3 GB), and below it
freezing, which held the object chunks and columns together until the GC ran (3,850 MB). Both fixed: the references go to a
mapped temporary file as they are read, and freezing collects its chunks (graph-format.md 6.3). On a 2 GB DenseHeap dump (3,145
MB file, 33.1M objects, 100.7M references; the primary machine, 24 logical cores), `load-dump --edges memory` against the default:

| Load | Peak private bytes | First load | Graph hash |
|---|---|---|---|
| References in memory, before the fix | 1,920 MB | 14.3 s | e8a102e4d933d936 |
| References in a file | 1,411 MB | 13.5 s | e8a102e4d933d936 |
| References in a file, chunks collected at freeze | 1,161 MB (0.56x the heap) | 14.5 s | e8a102e4d933d936 |

The 8 GB dump after the fix: to be measured (claude-dev-2's application control blocked the rebuilt benchmark).

Comparing (sprint 1.4): `diff` of Medium against DenseHeap from their index caches takes 2.8 to 2.9 s end to end.
Inspecting (sprint 1.5): the UI smoke drill on Medium opens the instances of `System.Byte[]` (16M objects), the largest
object and its references; after the responsiveness changes of section 8 the longest checked dispatcher operation is
36 to 63 ms and probe lag 49 to 128 ms across Medium and DenseHeap runs (Release and published ReadyToRun builds).
With the full test suite running at the same time (the 1.5 review re-measure), Debug smoke runs on Small and DenseHeap
pass every time: longest operation 43 to 53 ms, probe lag 80 to 158 ms. Instances of `System.String` on Medium
(6.6M strings, Release CLI, from the index cache): a text filter 3.8 s and a regular expression 4.5 s end to end,
against 1.9 s for opening alone; values are read and tested one at a time, and only a sort by value keeps text (at
most 250,000 strings).
Inspections (sprint 1.6, Release CLI `inspect`, all eight, from a freshly rebuilt index cache): Medium (57.5M objects)
7 s end to end, of which duplicate strings 2.7 s, finalizable and fragmentation 0.6 s each, the rest 0.1 to 0.2 s;
DenseHeap (54M objects) 15 s end to end, duplicate strings 5.1 s, collections 1.4 s, finalizable 1.4 s, fragmentation
1.5 s. In the UI the Small smoke run's inspections phase took 2.4 s with a longest dispatcher operation of 23 ms.
Rebuilding Medium's cache with free space and reference-array marks: 37 s (first load 19 s from the dump).
Live monitoring (sprint 1.7): the first counter sample arrives about one interval after the session starts; LeakyApi
leaking 20 requests of 2,000 items shows a GC heap rise of more than 10 MB within three samples; under load the heap
now climbed about 14 MB a second between collections while the after-GC size changed only at each GC. The Live smoke
run charts 1,800 samples (a full 30-minute window) in about 29 s with a longest dispatcher operation of 18 to 26 ms;
3,600 samples (an hour, the window sliding) stayed at 29 ms.
The second walk of GC handles (sprint 4.5, Server GC with 2 heaps on 24 processors): 0.1 s for 9,600 handles, 7.1 s for 1.9 million, a
load phase of its own ("handles").
Live allocations (sprint 4.3, the numbers in the sprint's Findings): figures 0.25 to 0.30 s behind LeakyApi under load, the Live
page's longest operation 27 to 35 ms beside the stall of BACKLOG.md that its smoke run has without them too; a live trace at its limits keeps 75 MB, 98 MB with
two views, and answers a view in 6 ms.
Leak detectors (sprint 2.6, Release CLI `inspect`, a fresh LeakyApi dump with every leak at 300,000 items each, 658 MB):
growing static collections 0.1 s, event handler leaks 1.5 s (300,000 subscribers on 1,024 publishers), timer leaks
0.4 s, disposed but referenced 0.3 s, HttpClient instances and service scopes 0.1 s each; all fourteen inspections
5.7 s end to end (measured before the review; reachability is now computed once per snapshot and kept, 0.125 bytes per
object, so later detectors skip that walk). The disposed probe reads each type's first reachable object, found in the
counting pass. On the clean LeakyApi run, which holds look-alike patterns above every threshold, none fires, with or
without a baseline.
Allocation traces (sprint 2.5, Release, the `allocations` benchmark with the worker's GC settings, Server GC with 12
heaps on the 24-thread machine; growth over private bytes before the read, the 2.5 review): a 54 s LeakyApi trace under
LoadGen whose process was killed before the rundown (5.6 MB, 45,110 samples) reads in 1.1 s with 40 MB of growth; 150 s
of LeakyApi under continuous load (25.6 MB, 168,073 samples) 1.1 s and 131 MB; 600 s (72 MB, 565,625 samples) 1.9 s and
227 MB, about 76 MB plus 2.1 bytes per file byte. The worker reserves 96 MB plus 3 bytes per file byte while reading
(`TraceStore.ReadReservation`, about 1.4 times the measured growth; sizes above 72 MB are extrapolated, and growth
follows samples, about 240 bytes each, more than file bytes; `TraceReadCost` since 4.7, where a recording's limit is fitted so its
read takes at most a quarter of physical memory, and the default limit is 512 MB, a 1.6 GB read beside two snapshots on 16 GB); an open trace keeps 29 bytes per sample (25 before 4.4) plus its names (14 MB for the 600 s trace, 2.3 MB more since 4.4).
Whole-trace queries take 2 to 13 ms (types 244 ms on the first call, JIT included). A 12 s trace of 5,000 requests
(4.1 MB, 5,415 samples) lists the five leaks' `Grow` methods as its top five call stacks. The UI smoke run's
allocations step (Release, the 150 s trace, after the Small-sized dump's phases): the cold first show 3.0 s with a
maximum lag of 74 ms; the whole run's longest dispatcher operation 66 ms, exit 0. TraceEvent's conversion cannot be canceled; it took under
2 s on these traces, and a much longer, busier trace would hold its reservation longer (Backlog: a streaming reader).

Measured in sprint 3.10 (the visual design with Inter, ADR-040) against the build before it, as published ReadyToRun builds,
win-x64, the open and compare smoke run on two small demo dumps, four runs each (the first run after a build is left out, as the
system scans the new files): first window 495 to 510 ms before, 523 to 544 ms after; maximum dispatcher lag 161 to 180 ms before,
169 to 184 ms after, under its 200 ms gate; longest dispatcher operation at most 82 to 132 ms before, 89 to 131 ms after. Those
are first shows of a page, which the run logs; every checked phase stayed inside the budget, and every run exited 0. The cold
first shows over 100 ms went from 3 to 7 per run to 5 to 13. Inter and its tabular figures account for little of that: with
neither, an unpublished build still showed more of them than the build before, so the cost is mostly the new styles and templates
compiling on first use. The published folder grew from 352 MB to 359 MB, the font and the license folder included.

UI stalls attributed (sprint 3.12, task 3). Five WPR recordings (CPU sampling, context switches, ready thread, .NET events) of
the published ReadyToRun build on 2026-11-12, 23:47 to 00:05, on the 24-thread, 32 GB development machine with its usual
background work mostly stopped (another project's tests ran at times; the load log is `artifacts/bench/3.12/load-quiet.log`).
`ui-stalls` (perf/HeapScope.Benchmarks) splits each stall the app's watchdog logged (lags and long operations over 100 ms)
into causes by the UI thread's context switches, ready time, the runtime's GC and JIT events and its CPU samples' stacks.
Shares of the total lag time per run:

| Run | Lags over 100 ms | GC | Styles and templates | JIT | Layout | Rendering | Other code | Blocked (not running) | Waiting for a CPU |
|---|---|---|---|---|---|---|---|---|---|
| Quiet, run 1 | 16, 4,290 ms | 13% | 18% | 10% | 6% | 6% | 13% | 35% | 0% |
| Quiet, run 2 | 10, 2,643 ms | 14% | 16% | 14% | 6% | 3% | 18% | 29% | 0% |
| Four busy cores (task 6) | 23, 5,857 ms | 25% | 23% | 12% | 9% | 7% | 16% | 7% | 0% |

What the recordings show:
- The UI thread never waited for a processor in any stall, even with four cores busy: the lag under load (Backlog, from 0.3)
  is not CPU starvation of the UI thread, so a CPU rate cap, EcoQoS or GC heap affinity for the worker would not reach it.
  Under load the UI process's own garbage collections grow to a quarter of the lag: gen 0 and gen 1 collections the UI's
  small allocations trigger (non-concurrent, 14 to 93 ms each in these stalls) and background gen 2 collections.
- The first shows of a page are mostly styles and control templates built on first use (Avalonia's styling and template
  frames), then GC, layout and rendering.
- Cold start's first window (1.5 s in run 1) is about 280 ms of JIT and 310 ms of the runtime's own code (type loading,
  ReadyToRun fixups; no symbols for it), and about 880 ms off the processor: one wait of 112 ms was inside Microsoft
  Defender's real-time scan (`mprtp`) of a file being loaded; the others carried no user-mode stack.
- One 220 ms lag in each quiet run has the UI thread off the processor the whole time with no stack (the app's own
  watchdog also measured 0 ms of UI CPU in it): not work on the UI thread; its cause is not in these recordings.
- The traced runs are slower than untraced ones (the first window took 7.0 and 8.5 s while recording, 0.8 to 4.9 s without);
  the shares, not the times, are the result.

The UI's gen 0 budget (sprint 3.12, task 6), A/B smoke runs of the published build on 2026-11-13 with Defender and another
project's tests at about 3 cores (`load-window2.log`), alternating the default against a 64 MB gen 0 budget
(`DOTNET_GCgen0size`). GC time inside the logged lags per run: with four more cores busy, 392, 408 and 430 ms by default
against 145, 174 and 211 ms with 64 MB; on the quieter machine 237, 265 and 224 ms against 214, 270, 146 and 157 ms. Slow
operations (over 100 ms) per run under load 5, 7 and 10 against 6, 7 and 7; the worst lag moved little (203 to 275 ms
against 189 to 266 ms), since styles and templates are the rest of it. The runtime reads the budget only from the environment
variable, and ignores `System.GC.Gen0Size` in the runtime configuration (the sprint 3.12 review, H2: a test program counted 73
gen 0 collections with and without it, 14 with the variable). A worker CPU cap, EcoQoS or GC heap affinity would not have
helped, since no stall waited for a processor.
In the owner's quiet window of 2026-11-15 the larger budget was measured again, this time for the longest single
operation too, and lost: with 64 MB (the app relaunched with the variable) a snapshot open's gen 0 pauses grew to 80 to 109 ms
each, against 10 to 58 ms by default, so the open phase's longest operation went from 108 to 143 ms to 209 to 255 ms and the
first window from 721 to 955 ms to 801 to 1,182 ms (six alternating pairs); 32 MB, set directly, was worse than the default
too (141 to 173 ms against 113 to 131 ms, four pairs). Fewer, longer pauses break the 100 ms operation budget, which is what a
user feels as stutter; the total GC time task 6 measured is not. The app keeps the runtime's default gen 0 budget, and the
relaunch built to ship the larger one was removed (review M2, the owner's decision).

UI cold start on Avalonia (sprint 3.12, task 7), the published ReadyToRun build on 2026-11-13: the first window rendered
6.9 s after start on the first run after publishing (the files read and scanned for the first time), then 0.77 to 0.87 s
on later runs, and 0.99 to 1.10 s with four more cores busy; the Compare tab's first show had a maximum lag of 23 to 60 ms
and a longest operation of 49 to 121 ms.

Cold first shows prewarmed (sprint 3.12, task 4): after the first window, at the dispatcher's idle priority and one view per
step, the app builds the Snapshots page in its place (kept for its first show) and builds the other pages and the views a
page shows only with data alone, then drops them and collects gen 1 (about 8 ms); about 0.2 to 0.3 s of UI work over 0.35
to 0.45 s, its worst lag 53 to 88 ms, with "Getting pages ready" in the status bar meanwhile (`ViewPrewarmer`,
`Ui:Prewarm=false` turns it off). A/B smoke runs of the published build on 2026-11-14 in the owner's quiet window,
alternating on and off (a local script under the git-ignored `artifacts/`), demo dumps, six pairs: slow operations (over 100 ms) per run 1 to 2
on against 2 to 9 off; the Snapshots page's first show at most 16 to 17 ms against 58 to 83 ms; the open phase's longest
operation 92 to 140 ms (median about 109) against 113 to 204 ms (about 134); the Allocations page's first show level (55 to
69 ms against 41 to 71 ms); the Retainers tab's first show about 14 ms slower (median about 100 ms against 86 ms, cause not
found). Medium dump, three pairs: the Snapshots page's first show 16 to 21 ms against 70 to 95 ms, the open phase's longest
operation 129 to 181 ms against 158 to 209 ms, slow operations 4 to 5 against 4 to 6. Two designs were measured and
dropped: every page built in place made the Retainers and Allocations first shows 20 to 45 ms slower (the hidden Jobs, Live
and Processes pages stayed bound to their view models and followed the run's jobs), and the dropped views' garbage left to
the default gen 0 budget was collected inside the Allocations page's first show. The open phase's 100 ms operation check failed in
most runs of both arms on this day, and in one of two runs of the build before task 6: the machine, not this change.

The tutorial's sample in the packages (sprint 3.13, task 4, ADR-042): `sample/` holds the sample self-contained, 77.0 MB of
files in the win-x64 package (2026-11-15), against 356.5 MB for the rest (the worker 107.4 MB): 22% more, under the half again
that would have gone to the owner (sprint 3.13 decision 2). Most of it is the .NET runtime, a second copy beside the worker's;
the sample's own code is under 1 MB. The CLI's dotnet tool carries it framework-dependent (its assembly only). Its memory is
bounded (review M5): what a scenario plants is at most 256 MB (count times payload), and a growing sample stops growing when its
heap reaches 512 MB (with every growing scenario on, about 0.7 GB an hour before the limit).

The first load's object pass (sprint 3.12, task 8, with R4). The WPR recording of a Medium load (58.0M objects, 2026-11-12)
showed the loading thread on a processor for 50.7 of 56.5 s: CPU work, not reading the dump (1.1 s of page-in waits, the
file in the cache). ClrMD's type factory resolved each object's type (8.6 s), arrays asked the DAC each (2.8 s), sizes were
read twice and the finalizable check hashed every object. The same build loaded the same dump in 40.1, 51.6 and 24.0 s in
three runs that day, and in 20.8 to 21.5 s the next: much of the "slower first load" since 1.2 is the machine's load, not
the loader. The loader now maps method tables to types itself, once per type, computes sizes as ClrMD does, walks segments in
address order, merges the finalizable list, and reads references with each object's type known, skipping types that hold
none unless collectible (review H3). Medium, alternating the two builds three times each on 2026-11-13: object pass 10.9 to 11.1 s down to 4.9 to 5.1 s,
reference pass 7.4 to 7.6 s down to 5.3 to 5.6 s, first load 20.9 to 21.5 s down to 13.0 to 13.3 s, peak private bytes the
same (2,769 to 2,774 MB). The graph is the same: its columns hash identically on the fixture dump and the Medium dump
(`load-dump` prints the hash).
