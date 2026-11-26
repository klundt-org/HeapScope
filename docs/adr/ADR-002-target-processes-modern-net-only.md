# ADR-002 Target processes: modern .NET only

- Context: the primary use case is ASP.NET Core; .NET Framework adds a second runtime model and attach path.
- Decision: support .NET 5 and later targets. .NET Framework is a backlog item.
- Consequences: process discovery uses the diagnostics IPC of modern runtimes only.
- Amended in sprint 4.7 (peer review 4.5 H1): the decision stands, and what is tested is said. The fixtures run on .NET 8, 9 and
  10 and the known answers are checked on each in CI, on Windows and Linux; a dump of another version (5 to 7, or one newer than
  10) opens with a note that its answers are not checked (`TestedRuntimes`), and one before .NET 5 (.NET Framework, .NET Core 3)
  with a note that HeapScope does not analyze it. Running the known answers on .NET 8 found that its heap dumps taken on Windows
  leave out the memory its statics are kept in (Linux's createdump keeps it): the loader says so (`SnapshotFacts.StaticsChecked`
  missing) instead of showing what statics hold as unreachable.
