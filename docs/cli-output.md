# The command line's JSON output

Every command that takes `--format json` writes one JSON object, and its first member says which format it is:
`"Format": 1`. Scripts and CI checks can rely on what a format promises (sprint 4.8, peer review 4.5 M4; ADR-008). The
`table` and `csv` outputs are for people and spreadsheets: they change when the text reads better, with no format number.

## What a format promises

Within a format, the output only grows:
- a command keeps every member listed below, with the same name, type and meaning;
- new members may be added, to the object or to its rows, and new commands may write JSON;
- a list may hold new kinds of values where it names open sets (a heap kind, an inspection's name);
- the order of members other than `Format` may change.

So a script reads the members it knows and ignores the rest. A new format number comes with any change that would break such a
script: a member removed or renamed, a type changed (a number that becomes a string, a list that becomes an object), a unit
or a meaning changed (bytes that become kilobytes, a count that starts to include something else). HeapScope writes one
format at a time; a release that starts a new one says so at the top of its notes and of the CLI package's notes.

How the values are written, in every format:
- Member names are in PascalCase, as listed below; the rows of a grid (`Rows`, `Types` and the like) are keyed by the column
  names the table shows, so a column's name is part of the format too: renaming one is a new format (review M8; the contract
  test pins the top-level members, and the rows' keys are to be pinned too, BACKLOG.md).
- Counts and sizes are numbers (bytes, unless the name says otherwise); percentages are numbers from 0 to 100.
- An object's address is a string, `0x` and hexadecimal digits.
- `null` means unknown or not available for that snapshot (a gcdump has no generations; an object no root reaches has no
  retained size), never zero.
- Notes go to stderr, never into the JSON, so stdout is the JSON alone. The exit codes each command's help lists are part of
  the contract too.

## Format 1

The members of each command's object, in the order written. The contract test (`CliJsonContractTests`) runs each command on
the LeakyConsole fixture's dump and compares its members with this table; a change here and in the code goes together.

| Command | Members |
|---|---|
| `summary` | `Format`, `TotalObjects`, `TotalBytes`, `TotalTypes`, `Kind`, `IncludesUnreachable`, `UnfilteredRows`, `MatchingRows`, `HeapKinds`, `RetainedIsLowerBound`, `Dominators`, `Rows` |
| `instances` | `Format`, `TypeName`, `Module`, `MatchedTypes`, `TotalObjects`, `TotalBytes`, `MatchingObjects`, `ValuesAvailable`, `Dominators`, `Rows` |
| `object` | `Format`, `Address`, `TypeName`, `Bytes`, `HeapKind`, `IsRooted`, `ReferenceCount`, `Text`, `FieldsUnavailable`, `Fields`, `References`, `Retained` |
| `inspect` (every inspection) | `Format`, `Inspections` |
| `inspect <name>` | `Format`, `Name`, `Kind`, `Title`, `Cause`, `Fix`, `Baseline`, `Count`, `Bytes`, `WastedBytes`, `Note`, `Rows` |
| `paths --type` | `Format`, `Paths`, `MatchedTypes`, `Objects`, `Unreachable`, `UnreachableBytes`, `OtherPaths`, `OtherObjects`, `OtherBytes`, `CompleteGraph`, `TreeSeconds`, `UnreachableNote` |
| `paths --address` | `Format`, `Nodes`, `RootNodeIds`, `KeyPaths`, `Reachable`, `StoppedEarly`, `Visited`, `FieldsNote`, `CompleteGraph`, `Index`, `Truncated`, `UnreachableNote` |
| `referrers --type` | `Format`, `TypeName`, `Module`, `MatchedTypes`, `TargetObjects`, `ReferenceCount`, `ReferrerTypes`, `CompleteGraph`, `Index`, `Rows` |
| `referrers --address` | `Format`, `Address`, `Referrers`, `ReferenceCount`, `FieldsNote`, `CompleteGraph`, `Index`, `Rows` |
| `retainers` | `Format`, `Focus`, `Ancestors`, `FocusRetained`, `TotalRetained`, `TopLevel`, `Truncated`, `AncestorDepth`, `Grouping`, `Depth`, `Top`, `RetainedBound`, `Dominators`, `Rows` |
| `retained-by` | `Format`, `TypeName`, `Module`, `MatchedTypes`, `AllObjects`, `SetObjects`, `SetBytes`, `RetainedObjects`, `RetainedBytes`, `LowerBoundBytes`, `TotalRetained`, `Seconds`, `Dominators`, `Rows` |
| `set` (a grid view) | `Format`, `Result`, `Rows` |
| `set --view paths` | the members of `paths --type` |
| `diff` | `Format`, `ExactCounts`, `BaselineNotes`, `CurrentNotes`, `BaselineObjects`, `BaselineBytes`, `CurrentObjects`, `CurrentBytes`, `BaselineKind`, `CurrentKind`, `BaselineIncludesUnreachable`, `CurrentIncludesUnreachable`, `IncludesUnreachable`, `NewRows`, `GoneRows`, `UnfilteredRows`, `MatchingRows`, `MatchingBaselineBytes`, `MatchingBytesDelta`, `ObjectsMatched`, `IdentityNote`, `NewObjects`, `DeadObjects`, `KindDeltas`, `BaselineDominators`, `CurrentDominators`, `RetainedIsLowerBound`, `Allocations`, `AllocatedIsEstimate`, `Rows` |
| `trend` | `Format`, `Snapshots`, `TotalBytes`, `UnfilteredRows`, `ExactCounts`, `Rows` |
| `allocations` | `Format`, `Trace`, `ProcessId`, `ProcessName`, `DurationSeconds`, `From`, `To`, `Type`, `SampledBytes`, `Samples`, `BytesAre`, `LostEvents`, `SamplesWithoutStack`, `SamplesWithUnresolvedFrames`, `HasRundown`, `ConversionIssues`, `TypesBytes`, `Types`, `Stacks` |

Before format 1 (HeapScope 4.7 and earlier), the output had no `Format` member, and `inspect` without a name wrote a list
instead of an object: that list is now the `Inspections` member.
