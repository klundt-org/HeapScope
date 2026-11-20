# ADR-043 Dump values in logs, by consent (added in sprint 3.13.2)

- Context: since Phase 1 the logs never hold values read from a dump (field values, string text): `Provenance` writes any
  text it does not know to be a name by its length only (ARCHITECTURE.md 11 and 12). That keeps a shared log safe, but a
  contested result sometimes turns on a value, and support cannot see it. The owner asked for an option to log values,
  off by default and on only with the user's explicit consent (2026-11-19), worded for users as "the logs never hold values
  read from a dump unless you turn that on (it is off by default, and asks you to confirm), and like everything else they
  stay on your machine."
- Decision:
  - **A setting, off by default**: Include dump values in logs (`HeapScopeSettings.IncludeDumpValuesInLogs`), in
    `settings.json` beside Include filter text in logs, followed by every HeapScope process within a second
    (`LogPrivacy.IncludeDumpValues`). A settings file that cannot be read means off, whatever was read before, and the app
    and the command line refuse to save over it, so saving one setting never resets the other.
  - **Consent each time it is turned on**: the Settings page asks first, and a no leaves it off; the command line needs
    `--yes` and otherwise says what it would do and changes nothing, no other option included. Turning it off asks nothing
    and deletes nothing, but starts a new part of the process's log: Purge logs then deletes the process's earlier parts,
    which hold the values, and keeps only the part being written. A purge itself starts a new part first, after waiting until
    two seconds have passed since dump values were turned off (a worker sees a change within a second, and its lines reach
    the client log just after), so the values a worker wrote in that moment go too. Logs that other running HeapScope
    processes write are kept until they close.
  - **What it adds**: the worker's request and result lines (`Provenance`) write each text quoted (its first 200 characters,
    and its length, never splitting a character) and a list's first 20 items, each described, then how many more; records go
    four levels deep instead of two (an object's struct fields and array elements), and a line stops at 16 KB. A value that
    cannot be described is named by its type, so logging never fails an answer. The filter text keeps its own setting.
    Nothing else changes: the index cache, traces, exports (which ask per export) and messages keep their rules.
  - **A log says so**: every process's start line and every log part's header name both settings ("logs hold filter text
    on, dump values off"), and the worker logs every change, as a warning with a note not to share the log from there on
    when dump values turn on. Lines are not marked one by one.
- Consequences: a log written with it on can hold secrets, so the setting's tooltip, question and the docs say to turn it
  off and purge the logs before sharing one. Off, nothing changes from before. Logs still never leave the machine:
  HeapScope sends nothing anywhere (ARCHITECTURE.md 12).
