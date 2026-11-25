# ADR-010 Diagnostics libraries (amended in sprints 1.8, 3.12 and 4.5)

- Context: reading dumps and talking to runtimes is complex; Microsoft's libraries are the reference.
- Decision: ClrMD, Microsoft.Diagnostics.NETCore.Client, TraceEvent, latest stable at sprint 0.1.
- Consequences: versions verified at 0.1 (section 14); upgrades are deliberate.
- Amendment (sprint 1.8): TraceEvent ships no gcdump reader or writer; that code exists only as MIT source in PerfView
  and dotnet-gcdump. HeapScope vendors dotnet-gcdump's copy (`src/HeapScope.ThirdParty.GcDump`, dotnet/diagnostics
  `b1a4548`, from PerfView `76dc28af`) with the changes listed in its `CHANGES.md` (five by sprint 3.7), listed
  in `THIRD-PARTY-NOTICES.md`. An update replaces the files from a newer commit and reapplies the listed changes.
- Amendment (sprint 3.12, R4 and task 8; the review's M1): the full dump loader walks the heap's objects itself, beside
  ClrMD, since ClrMD's per-object type factory was the largest part of a first load. ClrMD still opens the dump, finds the
  segments, allocation contexts, roots, handles and types, and reads every reference. The walk assumes CoreCLR's object
  layout, as ClrMD reads it: the method table in the first pointer of an object, its two low bits the GC's marks; an array's
  or string's component count a 32-bit value at one pointer; a string's size counting its terminator; no object smaller than
  three pointers; objects aligned to a pointer, and to 8 bytes on large and pinned segments; a thread's allocation context
  ending one minimum object before the next object. These are CoreCLR's rules for .NET 5 and later on 64- and 32-bit Windows,
  Linux and macOS, with segments or regions; CI verifies them on 64-bit .NET 10 with regions, on Windows and Linux, and
  the rest is assumed. An object whose method table maps to no type hands the rest of its segment to ClrMD's walk; a
  length that runs past its segment counts as unreadable and marks the segment incomplete. ClrMD yields no reference from an
  object of a collectible type to its LoaderAllocator, which the GC marks, so the loader adds it. A runtime that changes the
  layout (or .NET Framework, if it is ever read) needs this walk changed or turned off; a ClrMD upgrade is checked by
  `LoaderKnownAnswerTests` (every object's address, type, size and finalizer flag and every count against ClrMD's own walk,
  and a walk handed to ClrMD in mid-segment, on Windows) and `WalkComparisonTests` (every object, on each CI system's own
  fixture dump, Linux's core dump included).
- Amendment (sprint 4.5, peer review C1): a load of a Server GC dump walks the GC handles a second time (`HandleTables`), through a second
  ClrMD runtime whose reader shows Microsoft's debugging library one number of the dump as another: the heap count. Why: the runtime
  keeps a handle table per processor, the library walks one per heap, and nothing else gives the rest. Alternatives: reading the
  tables ourselves (their layout changes between versions and is not documented), or only refusing what rests on them (every answer
  about ASP.NET Core services would be in doubt). Assumed: the heap count is a 32-bit number of the runtime's or GC's module that the
  walk reads, and a table past the last gives nothing. Nothing is assumed about where it lies: the place is found by what it does, each
  added handle must be of the heap's objects, and what cannot be shown ends as `Unknown`, with a note and the unreachable set refused.
  Verified on 64-bit .NET 10 on Windows and Linux; other versions are not (peer review H1). `Worker:SecondHandleWalk` turns it off. A
  ClrMD or runtime upgrade is checked by `HandleTablesTests` (Windows, 1, 2 and changing heap counts against the process's own counts)
  and `build/check-handles.sh` (Linux, in CI).
