# Sprint 4.2 review: Capture on macOS

Reviewers: solution-architect, senior-dev, gc-specialist, support-engineer (2026-11-23), on `sprint/4.2` against the tag
`sprint-4.1.1`. Each reviewer read the working tree (none had a shell or ran anything; the gc-specialist read the strings of
the macOS runtime pack's binaries, which were checked again here). Findings that overlap are merged and keep the highest
severity. Counts: 8 High, 15 Moderate, 40 Low (with those of the confirmations).

Before the review: Debug and Release builds with 0 warnings; the fast tier passed (1,596 tests, 17 skipped), and the light
integration tier (287). The owner decided the Moderates' handling in advance (autonomy for Phase 4): each was applied as
recommended, marked "decided autonomously". The sprint's texts are drafts for the owner (docs/design/info-text.md, "Sprint
4.2 additions"). Nothing here has run on a Mac.

## High

### H1. The size estimate left out the shared cache
- Reviewers: gc-specialist. Where: `MacProcData`, `DumpSizeEstimator`.
- What: libproc's region walk reports the dyld shared cache as one empty submap; createdump walks into it, so a full dump
  holds several GB the estimate did not have: the free-space check would pass and the dump could fill the disk while the
  target is paused.
- Fix: `IProcessFacts.UncountedBytes`, on macOS the cache's own size (`_dyld_get_shared_cache_range`, 5 GB when not given),
  added to walk and fallback. Tests: `MacFactsTests.The_estimate_takes_the_larger_figure...`.
- Status: Fixed; confirmation below.

### H2. The estimate's fallback was a constant
- Reviewers: gc-specialist. .NET 10.0.15 sets no `PrivateBytes` on macOS (checked in the runtime pack), so the "larger of"
  rule compared with 400 MB, and a walk that ended early in a large process passed the check.
- Fix: `IProcessFacts.PrivateBytes`, on macOS the physical footprint (`proc_pid_rusage`), 0 is not a size. Tests:
  `The_footprint_is_read_from_a_whole_struct_and_zero_is_not_one`.
- Status: Fixed; confirmation below.

### H3. A failed macOS read left no trace
- Reviewers: support-engineer. A missing call, an errno, another struct size and a parse failure all read the same, and the
  log had none of them.
- Fix: each read keeps its outcome; once per run "Capture on this system is untested. macOS reads of HeapScope's own process
  ..." (`SelfCheck`); the "Listed" line counts what could not be read; the evidence gives the outcomes. Tests:
  `Off_macos_every_fact_is_empty_and_says_why`, the evidence tests.
- Status: Fixed; confirmation below.

### H4. A failed read was logged as "running no"
- Reviewers: support-engineer (High), solution-architect (Low). Fix: running comes from the process list, and an unreadable
  user id ends no other check. Tests: `A_process_that_is_not_found...`, the evidence tests.
- Status: Fixed; confirmation below.

### H5. "Not a .NET process" rested on a walk that may have ended early
- Reviewers: support-engineer (High), senior-dev, gc-specialist (Moderate). Fix: the rule is gone on macOS (the generic text
  stays), and the evidence says how many regions were read, where the walk stopped and why.
- Status: Fixed; confirmation below.

### H6. The check script printed every process's command line for a public issue
- Reviewers: support-engineer. Fix: it prints the sample's row and the number of the others; INSTALL.md says what the output
  shows.
- Status: Fixed; confirmation below.

### H7. The check script deleted the logs that explain a failure
- Reviewers: support-engineer (High), senior-dev, solution-architect (Moderate). Fix: on a failure the log folder stays and
  is printed, with a note that it is not for the issue.
- Status: Fixed; confirmation below.

## Moderate

- M1 (support-engineer, senior-dev, solution-architect): the check script stopped at its first failure, the full dump, the one
  macOS is likeliest to refuse. Fix: every step runs, and the end lists what failed. Decided autonomously.
- M2 (senior-dev, solution-architect): eight or more tests expected Windows' or Linux' answer on a Mac. Fix: they ask
  `HostSystems` or the texts themselves. Decided autonomously.
- M3 (solution-architect, support-engineer; senior-dev Low): macOS texts offered sudo in one place and not in another, and sudo
  looks in another temp folder there. Fix: macOS texts say to run as that user and never offer sudo or root; the ADR and the
  sprint's decision say so. Decided autonomously (drafts for the owner).
- M4 (senior-dev, solution-architect, gc-specialist, support-engineer): the target's executable was opened by path with no
  regular-file check (a pipe would hang HeapScope). Fix: macOS no longer opens it (with H5). Decided autonomously.
- M5 (senior-dev): the write failure test compared the rule with itself. Fix: each row has its text, the HRESULTs and a mixed
  message; the library refusal is tested. Decided autonomously.
- M6 (gc-specialist): the write failure rule matched a message macOS' createdump does not print. Fix: the real texts, and
  "Operation not permitted" as macOS refusing the folder; Linux' rule knows "Could not create output file" too, so an
  "Operation not permitted" there is not called ptrace. Decided autonomously.
- M7 (gc-specialist; senior-dev Low): a huge estimate wrapped the free-space check into a pass. Fix: the check subtracts, and a
  sum no process has is not a size. Decided autonomously.
- M8 (solution-architect): leftovers of the split: the elevation probe per system, rules for a system without capture, the
  estimate's policy, one helper for the refused-user line. Fix: each done. Decided autonomously.
- M9 (support-engineer): the log had one estimate figure, not what it rests on; a missing DAC copy was silent; an unanswered
  capture had no evidence; a write rule did not say what it matched. Fix: the estimate line (every system), the DAC lines,
  `NotAnswering` in a capture, the matched text. Decided autonomously.
- M10 (support-engineer; senior-dev and solution-architect Low): an environment entry could be read as the last argument and
  shown. Fix: a trailing argument with an entry's shape is left out. Decided autonomously.
- M11 (support-engineer): logging.md and INSTALL.md lagged (the `dotnet-dump` command was missing). Fixed. Decided autonomously.

## Low (listed, not fixed unless noted)

- L1 (support-engineer): an unset `TMPDIR` read as `/tmp`. Fixed ("not set").
- L2 (support-engineer): unreadable ids logged "(null)" in Live and trace. Fixed with M8's helper.
- L3 (support-engineer): the environment becomes managed strings; security-privacy.md now says so.
- L4 (support-engineer, solution-architect): "untested" is not in the CLI's output, the Record tooltip or the empty pages.
- L5 (support-engineer, solution-architect, senior-dev): stale comments and docs; most fixed with the sweep.
- L6 (support-engineer): the log header lacks the machine's architecture; the self-check line has both.
- L7 (senior-dev): temp folders compared by case. Fixed.
- L8 (senior-dev): the macOS-only test was weaker than Linux'. Fixed.
- L9 (senior-dev): rule classes share texts by copy; `LinuxProcessFacts` repeats its own try and catch; the "@(#)Version" marker
  is defined twice.
- L10 (solution-architect): sysctl was told the pool's length, above the system's limit. Fixed.
- L11 (solution-architect): after the entitlement refusal the message still advises a folder and free space.
- L12 (solution-architect): `CanCapture` lives in a class of words.
- L13 (solution-architect): macOS calls `LinuxCaptureRules.RootLibraryRefusal`.
- L14 (solution-architect, gc-specialist): a region walk per listed process, unmeasured; paths decoded that are not used.
- L15 (gc-specialist): the command line is read twice per process and 1 MB cleared each time.
- L16 (gc-specialist): a runtime before .NET 7 gives an HRESULT only, so no gcdump is offered.
- L17 (gc-specialist): user ids were signed in the log. Fixed.
- L18 (gc-specialist): the loader's macOS folders lack Homebrew's.
- L19 (solution-architect): unknown user id means no runtime read; the DAC line says so now.
- L20 (support-engineer): no line names the system beside the header's own description.
- L21 (solution-architect): the ADR said "untested" comes off with the script. Fixed (the Backlog's list).
- L22 (solution-architect): INSTALL's `codesign` command dropped entitlements. Fixed.

## Confirmations

First round (2026-11-23; all read the code and ran nothing):
- gc-specialist: H1, H2, H5, M4, M6, M7 Confirmed, with L23 to L26.
- support-engineer: H3 to H7, M1, M3, M9, M11 Confirmed; M10 not (M14 below), with M13 and L27 to L34.
- senior-dev: H5, H7, M1, M4, M5, M7 Confirmed; M2 not (H8), with M12, M14, M15 and L35 to L38.
- solution-architect: H7, M1, M3, M4, M8 Confirmed; M2 not (H8), with M12 and L39 and L40.

New findings of that round, fixed:
- H8 (senior-dev, solution-architect): a test of the "Listed" line expected its old end; its branch runs only off Windows, so
  only CI's Linux job would have shown it. Fixed, and the counts have a test.
- M12 (solution-architect, senior-dev): a capture of a listed process that did not answer got a not-found reason, which
  contradicts its socket having been found (also on Linux). Fixed: the old message, with the evidence in the log only.
  Decided autonomously.
- M13 (support-engineer; senior-dev Low): the check script removed its logs when it was stopped. Fixed: they go only when every
  step passed. Decided autonomously.
- M14 (support-engineer, senior-dev; solution-architect Low): with one empty argument, or an entry with another name, an
  environment value still reached the command line. Fixed with the cases as tests. Decided autonomously.
- M15 (senior-dev; support-engineer and solution-architect Low): a trailing argument of that shape could decide a not-found
  reason. Fixed: it decides nothing. Decided autonomously.

New Low findings (listed, not fixed unless noted): L23 (gc) if a macOS reported the shared cache as more than an empty submap,
it would be counted twice and captures refused for space; L24 (gc) an x64 HeapScope under Rosetta measures the x64 cache; L25
(gc) a path holding "ptrace" or "task_for_pid" matches its rule; L26 (gc) the evidence's readable bytes are partial when the
walk stopped at the runtime; L27 (support) Live, trace and a refused library capture log no self-check; L28 (support) the
counts said "not readable" (fixed: "not known"); L29 (support, architect) logging.md lacked a line (fixed); L30 (support) a
missing folder was called not writable (fixed); L31 (support) the estimate line does not say the cache size is assumed; L32
(support, architect) USER-GUIDE on root (fixed); L33 (support) a gcdump that gets no answer has no evidence; L34 (support) the
left-out argument is not marked; L35 (senior-dev) quadratic move of entries (fixed); L36 (senior-dev) the self-check ran on the
caller's thread (fixed); L37 (senior-dev) case of /var (fixed); L38 (senior-dev) stale names (fixed in part); L39 (architect)
the script's log lines were lost on a pass (fixed: printed); L40 (architect) ARCHITECTURE and ADR-014 (fixed).

Second round:
(asked)
