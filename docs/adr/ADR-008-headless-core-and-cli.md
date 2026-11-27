# ADR-008 Headless core and CLI

- Context: correctness needs automated tests; CI leak checks need a scriptable tool.
- Decision: the engine has zero UI dependencies; a CLI exposes the same capabilities.
- Consequences: every feature lands in Application or the worker first, then gets a UI and a CLI command.
- Amended in sprint 4.8 (peer review 4.5 M4): the command line's `--format json` output is a versioned contract
  ([cli-output.md](../cli-output.md)). Every command writes one object whose first member is `"Format": 1`
  (`CliCommands.WriteJsonAsync`, the one place JSON is written); within a format, members are only added; a removal, a rename,
  a new type or a new meaning is a new format, said at the top of the release notes. `inspect` without a name wrote a list
  before format 1, and writes it as the `Inspections` member now. `CliJsonContractTests` checks each command's members
  against the document on a fixture dump. The table and CSV outputs are not part of it.
