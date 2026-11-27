# ADR-014 Elevation (amended in sprints 4.1, 4.1.1, 4.2 and 4.8)

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
- Amended in sprint 4.2 (untested): macOS follows the rule on user ids, but its texts say to run as the process's user only (the
  mark reads "Needs its user"): each user has their own temp folder there and sudo does not keep it. Run as root, HeapScope still
  lists only the processes whose sockets are in the folder it looks in.
- Amended in sprint 4.8 (peer review 4.5 H17): run with sudo on Linux, HeapScope is still the invoking user's tool. It keeps that
  user's data folder (`~/.local/share/HeapScope` of the user `SUDO_UID` names, from `/etc/passwd`), unless `HEAPSCOPE_DATA_FOLDER`
  names another, and gives that user what it creates (`PrivateFiles`' folders and files, a capture's new `--out` folders, DAC
  copies) and a dump it protects after a capture, never following a link (`lchown`). `InvokingUser` holds the rule. The
  command line's note gives the full path to sudo, whose `secure_path` drops the user's `PATH`.
