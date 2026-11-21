# ADR-014 Elevation

- Decision: dumping or attaching to elevated processes requires HeapScope to run elevated. Detect this and
  explain it clearly; never fail silently.
- Implemented in sprint 1.1: the process list marks such targets ("Needs administrator", with a hint), and a capture
  is refused before the runtime is asked, with "Run HeapScope as administrator". Tested with a fake probe, and against
  the real elevated .NET services on the development machine. HeapScope never elevates itself.
- Amended in sprint 4.1: on Linux "elevated" means another user's process (by effective user id), which needs HeapScope
  run as that user or as root; the mark reads "Needs root or its user", and `CaptureFailure.NeedsAdministrator` and
  `ProcessEntry.NeedsAdministrator` keep their names with that meaning there (ADR-025's amendment).
- Amended in sprint 4.1.1 (owner, 2026-11-20): the Processes page and the Live page hide the processes HeapScope cannot
  capture as it runs now, with a check box to list them and a note that says how many are hidden; listed, they keep their
  mark. Run elevated (or as root), HeapScope can capture them all, so nothing is hidden. The command line's `list` is
  unchanged. The rule is the view models' `ProcessListFilter`.
