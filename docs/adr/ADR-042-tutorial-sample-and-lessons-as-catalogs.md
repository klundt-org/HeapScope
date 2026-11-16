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
    describes it (none for the sample's own scenarios, whose types live in the sample), the type a lesson's answer names, its
    decoys (other types of the sample that a lesson's steps rule out), what it cannot run with and why, and what
    it plants after the sample's last full collection. The sample prints its catalog with a version (`--list`, JSON) and the
    app reads it, so the app never offers a scenario its sample lacks. A scenario may keep growing at an interval, so live
    views, traces, comparisons and trends show change.
  - **Lessons are a catalog in the app** (`LessonCatalog`, UI.ViewModels, versioned too): each entry names its scenarios, the
    page it starts on, its steps and a question whose answer is checked against the type its scenario planted, offered among
    three of the sample's own types (the scenario's decoys, then other scenarios' answers), so the answer cannot be told by
    its name. The check does not watch what the user clicks, so a lesson holds however the user gets there and whatever pages
    later sprints change. Each question names what the steps measure (the top row by Retained, what an inspection's row
    names), and an integration test (`LessonTests`) follows every lesson's steps on a real sample through the command line
    and checks they arrive at the answer (sprint 3.13 review H1).
  - **The extension contract**: a feature a user would learn adds its scenario (one catalog entry in the sample, its known
    answer, its tests) and its lesson (one catalog entry, its texts approved in info-text.md), or its sprint says why not
    (DEVELOPER.md's Definition of Done). Tests keep the two catalogs consistent: every lesson's scenarios exist, every
    scenario has a lesson or a recorded reason, every answer is named by its known answer, every decoy is a type of the
    sample, and every scenario with a lesson has its steps checked on a real sample.
  - **What the sample may do**: it runs as the user and is refused while HeapScope runs elevated (it would be elevated too);
    at most three run at once in one HeapScope process; each exits when the HeapScope process that started it exits (the
    fixtures' parent watch); the app marks the rows of the samples it started. What a scenario plants is bounded (count times
    payload at most 256 MB, growth no faster than every 100 ms), and a growing sample stops growing when its heap reaches
    512 MB (sprint 3.13 review M5). Each running sample has a record in the data folder (its pid and start time), so
    `heapscope-cli sample stop` stops only a sample, never a process that reused its pid, on every system (review H4).
    Process discovery lists the sample although it sits under the installation's folder, since it is there to be captured.
  - **Where lessons run** (review M3): every lesson starts with a capture, so where capture is not available (Linux and macOS,
    ADR-025) the Learn page says so and starts none, and the Processes page does not offer the sample. A per-lesson
    requirement (a WPF scenario on Windows only) is added with the first lesson that needs one (Backlog).
- Consequences: packages grow by the sample's size (measured, budgets.md); the leak library is shipped code, licensed like the
  rest; a change to a planted problem changes a known answer, a fixture test and a lesson together; pre-built tutorial files
  are not shipped (Backlog), so a lesson needs the sample to run on the user's machine. Amends ADR-039 (a `sample/` folder
  beside `worker/` in every package).
