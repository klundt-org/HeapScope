# ADR-042 The tutorial: a shipped sample and lessons, both catalogs (added in sprint 3.13)

- Context: a new user has no process of their own that leaks, and nothing that shows what each page is for; the owner asked
  for a sample to look at (2026-11-10) and for hands-on lessons that walk through finding and diagnosing planted problems
  (2026-11-14). Phase 4 adds features that users must learn too, so the tutorial must grow with them without rework (owner,
  2026-11-15). The test fixtures already plant known problems with known answers (ADR-013), but none ships.
- Decision:
  - **One sample process ships**, `heapscope-sample`, in a `sample/` folder beside `worker/` in every package: self-contained
    per system as the worker is, framework-dependent in the CLI's dotnet tool (ADR-039). It is built on the fixtures' leak
    library (`fixtures/Leaks`), so one known answer serves the fixture tests and the lessons. It references nothing of
    HeapScope.
  - **Scenarios are a catalog in the sample** (`ScenarioCatalog`): each entry names what it plants, the known answer that
    describes it, and what it cannot run with. The sample prints its catalog (`--list`, JSON) and the app reads it, so the
    app never offers a scenario its sample lacks. A scenario may keep growing at an interval, so live views, traces,
    comparisons and trends show change.
  - **Lessons are a catalog in the app** (`LessonCatalog`, UI.ViewModels): each entry names its scenarios, the page it starts
    on, its steps and a question whose answer is checked against the scenario's known answer (the planted type among decoys
    from the same dump). The check does not watch what the user clicks, so a lesson holds however the user gets there and
    whatever pages later sprints change.
  - **The extension contract**: a feature a user would learn adds its scenario (one catalog entry in the sample, its known
    answer, its tests) and its lesson (one catalog entry, its texts approved in info-text.md), or its sprint says why not
    (DEVELOPER.md's Definition of Done). Tests keep the two catalogs consistent: every lesson's scenarios exist, every
    scenario has a lesson or a recorded reason, every answer is its known answer's.
  - **What the sample may do**: it runs as the user and is refused while HeapScope runs elevated (it would be elevated too);
    at most three run at once; each exits when the HeapScope process that started it exits (the fixtures' parent watch); the
    app and the command line mark its rows as a sample.
- Consequences: packages grow by the sample's size (measured, budgets.md); the leak library is shipped code, licensed like the
  rest; a change to a planted problem changes a known answer, a fixture test and a lesson together; pre-built tutorial files
  are not shipped (Backlog), so a lesson needs the sample to run on the user's machine.
