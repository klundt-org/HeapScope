# Sprint 4.8 review: Ready to release

Reviewed 2026-11-27 against sprint-4.7 by all four reviewers, once the 12 tasks were in. Findings that overlap are listed once, under the
highest severity. Moderates decided autonomously: fixed, or deferred to the Backlog where said.

## High

- H1 (senior-dev, gc-specialist; solution-architect, Moderate) Under sudo on Linux the root worker took what decides which native DAC it
  loads from the invoking user's data folder, which that user can write: the saved DAC folder and download opt-in (read at each load
  since H15), the trusted DACs list and the private DAC copies (the folder H17 gave to the user). Anything running as the user could
  plant a DAC that the next `sudo heapscope-cli` ran as root. Fixed: under sudo, a saved DAC folder and opt-in are ignored (the command
  line's still apply; the worker's lookup line says so); the trust list and the DAC copies live in root's own data folder
  (`InvokingUser.TrustFolder`, from the password file). INSTALL says it. CI's sudo check plants a user-owned settings file and checks the
  root run ignored it. Status: Fixed.
- H2 (support-engineer; senior-dev, Moderate) The Settings page and `--saved-dac-folder` saved folders the DAC search then dropped without a
  word: `\\?\` paths, mapped network and removable drives on Windows, non-local mounts on Linux. Fixed: on Windows the check is the
  search's own rule (no `\\` path, a fixed drive), refusing with why; everywhere, a refusal for a missing DAC names a DAC folder the search
  left out and why (`DacLocator.DacFolderNotSearched`). The save runs off the UI thread. Status: Fixed.
- H3 (support-engineer) Restart worker left no line in the log, and what it ended failed as "The analysis worker stopped unexpectedly", so
  support would chase a crash that never happened. Fixed: the shell logs which Restart worker was pressed and the question's answer; the
  host logs the restart with the generation and pid; a call a restart ended says "This stopped because the worker was restarted (Restart
  worker). Run it again." (`WorkerCrashedException.EndedByRestart`). The crash report's offer and the choice are logged. Status: Fixed.
- H4 (support-engineer) The help pages told people to attach logs and crash reports to GitHub issues without saying issues are public,
  against the project's own advice. Fixed: SUPPORT.md, every issue form, CONTRIBUTING, the trial guide, INSTALL, the user guide, logging.md
  and the crash report's header say issues are public and ask for the lines around a message, never a whole log; SUPPORT.md says filter
  text can be dump data and offers a private way for a whole log. Texts are drafts. Status: Fixed.
- H5 (solution-architect) The app used a Core constant (`HelpLinks.Issues`), against ARCHITECTURE.md 3.1; a constant is inlined, so the
  assembly reference check could not see it. Fixed: `HelpLinks` is in Contracts beside `ProductInfo`, and `FrontEndSourceTests` checks the
  front ends' sources name no Core type. Status: Fixed.
- H6 (solution-architect) The release was started on the default branch, so its provenance named that branch's commit and workflow, not
  the tag's; and each build resolved the tag by name again. Fixed: a release runs on its tag (`gh workflow run release.yml --ref vX.Y.Z`),
  `prepare` refuses a run that is not, resolves the commit once, and every job checks out that commit; `ReleaseWorkflowTests` holds both.
  Status: Fixed.

## Moderate

- M1 (senior-dev) The new CLI settings tests wrote the DAC folder and the opt-in into the data folder every test process shares, where any
  worker follows them. Fixed: `CliDacSettingsTests` (integration) runs the CLI with a data folder of its own.
- M2 (senior-dev) Purge all's bookmarks fix was tested only by its event. Fixed: a test that `ForgetAll` empties the list and the next
  bookmark starts a new file. The shell's one-line subscription is not tested on its own.
- M3 (senior-dev) Failures shown without a log line remained (Types' retained sizes, About's worker details, the banner's restart). Fixed.
  Deferred: an architecture test that a catch showing `ex.Message` also logs.
- M4 (gc-specialist) A first load chose the memory mode by private bytes, a reopen from the cache by all the columns (it reads the
  references into memory), so the same snapshot could open in memory once and mapped the next time. Fixed: the choice counts all the
  columns again; the log keeps the split (ADR-015 amendment corrected).
- M5 (gc-specialist; solution-architect, Low) The field reader kept the download opt-in it was created with, so a snapshot opened while it
  was on could still download after the user turned it off. Fixed: `ClrMdLoaderOptions.AllowMicrosoftSymbolServerNow` is read again when
  the reader opens the dump. Deferred: one read of the lookup per open for the check, the load and the reader (Low).
- M6 (gc-specialist) Nothing guarded task 11's fix. Fixed: `A_load_switched_to_mapped_lets_go_of_the_graph_it_built` checks the graph is
  gone at the switch's own log line; it fails on the switch as it was (checked by running it there).
- M7 (solution-architect) The error id travels only inside the message's wording and is kept in a static. Deferred (Backlog): error data
  in the protocol and an injectable holder.
- M8 (solution-architect) The JSON rows are keyed by column headers, which the contract test does not pin. Fixed in part: cli-output.md
  says a column's name is part of the format. Deferred (Backlog): pinning the row keys.
- M9 (solution-architect) Under sudo, ownership is given only where a writer remembers `InvokingUser.Give`. Deferred (Backlog):
  `PrivateFiles.Copy`, an architecture test and a capture in CI's sudo check.
- M10 (solution-architect) INSTALL's steps ran in a container that already had the libraries. Fixed: they run first, with nothing
  installed, and their own apt line installs them.
- M11 (support-engineer) The lookup's sources were labeled `--dac-folder` even when appsettings or the environment gave them, and the
  Settings page's note named appsettings only. Fixed: "the command line or appsettings", and the note names all three.
- M12 (support-engineer) The sudo example left out `--out`, which a root capture of another user's process needs. Fixed. Deferred: the
  capture refusal's own sudo advice with the full path.
- M13 (support-engineer) A failed `Give` is silent, and sudo drops `XDG_DATA_HOME` and `HEAPSCOPE_DATA_FOLDER`. Fixed in part: INSTALL says
  it. Deferred: logging a failed `Give` and saying it in the capture's result.
- M14 (support-engineer) A worker that did not start in time was sent to a Restart worker the failed start hides, and the status bar
  promised half a minute against a 30 s limit. Fixed: the text names the banner, the status bar says "can take a while", and a failed
  start logs how long it tried.
- M15 (support-engineer) The crash report's offer promised "never anything from your dumps" while the report holds the exception as it
  was. Fixed: the offer says the error may quote text and to read it first.
- M16 (support-engineer, not verified in ClrMD) With downloads on, ClrMD's default locator may also fetch other modules' images, which the
  texts did not say. Fixed in part: the texts say "each module it asks for". Deferred: a locator that answers DAC requests only.
- M17 (support-engineer) A "shown as" line logged the worker's text, not the status the user saw. Fixed.
- M18 (support-engineer) logging.md lacked most of the sprint's new lines. Fixed.

## Low

Listed, not fixed:
- senior-dev: `ErrorIds` test is order-dependent; crash reports ordered by name, not time; the crash offer is fire-and-forget and marks
  reports offered before asking; `BecameUnavailable` raised raw on the pool, never cleared, may print after a short CLI run ends; a
  missing session document costs 75 ms per folder; `WriteJsonAsync` would let a `Format` member overwrite the version; the CLI's
  `--saved-dac-folder` refuses a relative path the run's `--dac-folder` resolves; the "said once" test sleeps; no test of the opt-in's
  second click.
- gc-specialist: the stale comment and null assignments in `LoadAndDecide`; the benchmark times one of the worker's two collections, and
  the comment on the decision collection may be wrong about LOH compaction; a references file on tmpfs counted as file cache; a
  tautology in `IndexCacheTests` (one line beside it is the real check).
- solution-architect: the lookup read several times per open (also M5); the `Format` collision; `InvokingUser.DataFolder` ignores
  `XDG_DATA_HOME` (INSTALL now says it); ARCHITECTURE.md 3.1 on Core's system calls, process-model.md 4.3 on the banner, the ADR index's
  ADR-012 entry.
- support-engineer: the missing-DAC message does not name `settings --saved-dac-folder`; "DAC folder saved" shown when the installation's
  setting wins; the lookup line is not repeated in a new log part; cli-output.md does not say which members hold dump contents.

## Measurements and runs

- The flaky tests 20 times in a loop (task 10), in a worktree of their own: the session readers test, CounterMonitor, the two gcdump
  cancels 0 failures; the churn recording 1 and the load cancel 3, all in loops 3, 4 and 6, while 2 GB loads on the same machine took
  every processor; loops 7 to 20 were clean with builds running beside them.
- Task 11 on the 2 GB DenseHeap dump: the worker after the mapped switch 50 MB private, GC heap 8 MB (743 and 698 MB before).
- On GitHub, on the sprint branch: the release workflow's dry run (run 45935293632, every build and archive run green, the sudo check
  passed) and CI (run 45939852199, green, INSTALL's Linux steps passed). The review's fixes are checked by the sprint's PR.

## Confirmations

Each High was sent back to the reviewers that raised it, with the fix's commits (1578a97 and 3fdddae).
- H1: not confirmed at first by senior-dev, gc-specialist and solution-architect. The trust folder and the DAC copies in it, now in
  root's home, still went through `PrivateFiles`, which gave each new folder and file to the invoking user. Fixed (2bd939d):
  `InvokingUser.Give` refuses root's home and anything under it, and, for a root home of `/` (the re-check's Low), the trust folder,
  anything under it and the folders above it; CI's sudo check fails when anything under `/root` is the user's. Confirmed by all three.
  Their Low notes: the CI check does not yet make the root run trust and copy a DAC, so it would pass without the rule (the unit tests
  hold it); folders a development build of this sprint gave away under sudo keep their owner (never released).
- H2: confirmed by the support engineer and the senior developer. Found in the re-check and fixed: the untrusted DAC refusal names a DAC
  folder the search left out too. On Linux the save still takes a folder on a non-local mount; the refusal names it (Low).
- H3: confirmed by the support engineer. Found in the re-check and fixed: a call a close ended got the restart's message; the message
  now says "restarted (Restart worker) or HeapScope is closing".
- H4: confirmed by the support engineer. Found in the re-check and fixed: the crash report's offer, the user guide, INSTALL and README
  still said "attach"; they say which lines to paste. For the owner: SUPPORT.md's private way for a whole log is email to the owner's
  address, which the sprint's decision on the help channel ("not Discussions or email") did not foresee; recorded as owner to confirm.
- H5 and H6: confirmed by the solution architect.
- Moderates sent back: M1, M2, M3, M4, M6, M10, M11, M14, M15 (in part), M16 (in part), M17 and M18 confirmed. M5 confirmed with a gap:
  an open reader's DataTarget keeps ClrMD's locator after it opened, which the DAC-only locator in the Backlog covers (rated Moderate
  there by the GC specialist). Found in the re-check and fixed: under sudo the missing-DAC message and `heapscope-cli settings` say a
  saved DAC folder and download choice are ignored and name the command line's options. Still open and in the Backlog: the capture
  refusal's sudo advice with the full path (M12) and a failed `Give` logged (M13).

## Markdown sweep

After the fixes, the tracked Markdown was searched for every name, command and claim the sprint and its review changed (the release
command and its inputs, `HelpLinks`' project, the worker's start texts, the log advice, the download and crash report wording, the
symbol server's options, the memory mode's rule), and each hit read against the code. Changed in the sweep: ARCHITECTURE.md 3.1
(Core's calls to the system for sudo), process-model.md 4.3 (the banner, Restart worker, a call a restart ended), the ADR index's
ADR-012 entry, BACKLOG.md (the flaky loop's results), and the git-ignored CLAUDE.md (the release command, the settings options, the
JSON format). Past review files and sprint records were left as written. No em or en dashes in the changed files (the architecture
tests' text rules).
