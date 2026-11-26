# ADR-037 Live sources (added in sprint 3.8, amends ADR-023)

- Context: Phase 4's live session shows a running process's allocations as they happen, beside its counters. Today
  (peer review P6) counters and the allocation trace run in the front end: `EventPipeCounterSource` samples counters
  (ADR-021), and a monitoring run records the allocation EventPipe session to a `.nettrace` file in the session folder
  through Capture (`AllocationRecording`). The worker reads only finished trace files with TraceEvent (ADR-028) and can
  send nothing to a client but progress. Three placements were open: parse the live stream in the front end (it would
  hold TraceEvent's parsing and the stacks, which ADR-007 and ADR-028 keep out of the UI process); run the EventPipe
  session in the worker (the dependency table keeps Capture and the diagnostics client out of the worker, and a target
  that misbehaves would take the open snapshots with it); or keep recording in the front end and let the worker read
  the file while it grows.
- Spike (sprint 3.8, `perf/HeapScope.Benchmarks` `live-tail`): one process records the allocations profile HeapScope
  uses (the runtime provider's GC, Loader and JIT keywords at Verbose, with rundown) to a `.nettrace` file; a reader
  tails the growing file with TraceEvent's `EventPipeEventSource` over a stream that waits at the end of the file until
  the recording ends. The target allocates in a loop, flat out or pausing a millisecond every 200 allocations (about 70
  allocation ticks a second, an application under load). Measured on the 16 GB developer laptop:

  | Rate | Writer buffer | Ticks read | Lost | Delay, event to reader (p50 / p95 / max) | Peak working set |
  |---|---|---|---|---|---|
  | flat out, 15 s | 64 KB (as `TraceRecorder`) | 394,841 | 0 | 181 / 235 / 925 ms | 62 MB |
  | about 70 ticks/s, 15 s | 64 KB (as `TraceRecorder`) | 1,078 | 0 | 2,666 / 5,062 / 5,508 ms | 45 MB |
  | about 70 ticks/s, 15 s | none (each chunk written at once) | 1,079 | 0 | 157 / 217 / 240 ms | 45 MB |

  An earlier run with the GC and Type keywords and no rundown, flat out for 10 and 30 s, gave the same delay (179 ms
  median) and 1,143,130 ticks in 30 s with none lost. The delay is the runtime's buffer flush (100 to 200 ms) plus the
  reader's 20 ms poll when every chunk reaches the file at once; behind a 64 KB write buffer a realistic rate waits
  seconds for the buffer to fill. A reader that reaches the end before the writer flushed its last bytes fails with
  "read past end of stream", so it may stop only after the writer has flushed and said it is done.
- Not measured, and gates for Phase 4's live session (3.8 review): the reader ran in the recording process, so the
  memory figures include the writer; the reader counted ticks and resolved no stacks, so the cost of folding stacks as
  they arrive, in a separate worker process, is unknown. The first Phase 4 sprint measures both before building on
  this decision.
- Decision:
  - The front end keeps every EventPipe session: counters (cheap, already there) and the allocation recording, which
    writes the trace into the session folder. The file stays the record of the run (ADR-028), and a process that
    misbehaves never touches the worker.
  - A live recording writes each chunk of the session's stream to the file as it arrives (no write buffer), which the
    table shows is what keeps a live view a quarter of a second behind at a realistic rate.
  - The worker reads a live trace by tailing that file, as a resource of its own beside finished traces. It keeps
    aggregates, not every tick: types and sizes per time bucket, and stacks folded by instruction pointer (module and
    offset), named when names are known. Methods JIT-compiled before the session are named only by the rundown at its
    end, and a session attached to a warmed-up process has almost all of its frames in that state, so names cannot be
    the fold key; a short rundown-only session at the start that names them up front is to be measured. Its memory is
    reserved like any other work.
  - The front end asks for what changed since a time it already has: a request and an answer, as every call on the
    worker today. No push channel from the worker.
- Consequences, the contract Phase 4 builds (3.8 review):
  - The live file: written under its final name (no partial file and rename while live), shared for reading and
    deleting while it is written, owner-only from its creation as every file HeapScope writes (P15); a completion
    marker or the front end's notice says it ended. The tailing reader opens it with read, write and delete sharing so
    it never blocks the writer.
  - "The recording ended" is a new worker contract method (the front end's notice, sent after the file is flushed), so
    the live session is one protocol bump.
  - A live trace has its own identity for reopening after a worker restart: its path and creation time, with the file
    only ever growing. The finished-trace rule (the same size and write time, `TraceChanged` otherwise) does not apply
    while it grows.
  - A live view is about a quarter of a second behind the process. The worker gains no dependency (TraceEvent is
    already in Loaders.Trace). The session folder holds the live trace while it is read, so a run's allocations survive
    a worker restart. Building the live allocations view is Phase 4 work; this ADR fixes where it runs. The spike is
    measurement code only and does not ship.
  - What the worker checks of a trace before it reads it, and what a trace that grows needs of that check: ADR-028, amended in
    4.2.1.
  - gcdump capture stays in the front end (ADR-023 amended).
- Amended in 4.3 (the gates, measured on Windows and Linux; `live-stacks`, the numbers in the sprint's Findings): a reader in a
  process of its own keeps the delay of the table above (p95 at most 242 ms flat out on Windows, 239 ms on Linux) and loses no
  event, in 57 to 85 MB of which the probe's own samples are part; reading and folding the stacks costs 11 to 18 ms in 15 s at
  about 70 samples a second and 200 to 400 ms in 10 to 15 s flat out. Every live stack was the one TraceLog gives for the finished file.
  - Stacks are read from TraceEvent's event record, which is not public: by reflection, its layout checked once at the start.
    If the check fails (another TraceEvent), the live view shows types without stacks and the log says why; the finished file
    still has them. A reader of the format of our own was weighed and is not built: one more parser to own.
  - A short rundown-only session before the recording (123 to 171 ms on about 800 methods, 316 to 491 ms on 45,600) names what was
    compiled before; method events name the rest. At most 0.1% of the frames were unnamed when they arrived.
  - The recording carries a System.Runtime counter (every second where measured; as built, see below): without it the runtime keeps a thread's last samples when it
    goes idle, for seconds.
  - A heap-only dump stops the process 2.5 times as long as a full dump, so no new kind of snapshot is added.
- Amended in 4.3 (as built):
  - The worker reads only whole objects of the file that `TraceFileCheck` has walked (`LiveTraceStream`), so a growing file is
    checked as a finished one is; where the recording ends inside an object, the reader ends with the one before it.
  - Samples are added up by stretch of time, type, stack and kind (`LiveAllocations`): stretches of a second, doubling when a
    run has more than 1,800 of them or too many entries; stacks and types past the limits are added up under "other", counted
    and said. A live trace is an `AllocationTrace` whose entries stand for more than one sample, so the queries, trees and views
    of a finished one answer for it, but for the call tree, whose keys would not hold while stacks are folded again. A query's
    interval is counted in whole stretches and the answer says which, since they may have grown since it was chosen. 192 MB are
    reserved while it is read (98 MB is what it keeps at its limits, measured). Frames are folded by method name, or by address
    until one is known; a method loaded over another's code takes its place, and an unloaded one is gone.
  - Its identity across a worker restart is the recording's own start, which the file states, and not the file's creation time,
    which some file systems do not keep. A restarted worker reads what the file holds before anything is
    answered from it.
  - The methods compiled before are named by the partial file of a rundown-only session that is deleted when the recording ends;
    the finished file has its own rundown. Monitoring runs record this way (`TraceRecorder.RecordLiveAsync`); the Processes page and
    the command line keep the partial file and its rename. Not while a capture of the process runs: starting or stopping a
    session can disturb a gcdump's.
  - The System.Runtime counter comes at the run's own interval: a process has one timer for its counters, and the shortest
    interval asked for wins, so one every second would have overridden a longer run's. An idle process's last samples then
    arrive an interval late at most.
  - A recording that ends by an error keeps what it has.
  - Choosing an interval is asking again, not a push: the Live page asks twice a second where the trace stands, and for the
    timeline and types at most every two seconds. Call stacks are asked for on demand.
- Amended in sprint 4.7: `TraceRecorder.RecordAsync` (the Processes page and the command line) writes unbuffered too, as a live
  recording does. Behind its 64 KB buffer, as in the table above, an idle target's trace stayed 0 bytes on disk for minutes: the
  "trace start over 60 s" of CI and the 4.6 gate was that buffer, not a slow session.
