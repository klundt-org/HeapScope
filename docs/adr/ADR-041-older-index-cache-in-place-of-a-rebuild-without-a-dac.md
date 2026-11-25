# ADR-041 An older index cache in place of a rebuild without a DAC (added in sprint 3.12, amends ADR-006, ADR-032, ADR-035 and ADR-036)

- Context: a full dump's index cache from an older loader is rebuilt (sprint 3.10, review M7), and the rebuild needs the
  dump's DAC (Data Access Component). After a .NET update removes the runtime the dump was taken on, or when the only DAC
  lies untrusted next to the dump (ADR-035), the rebuild fails and the dump could not be opened at all, although its older
  cache still held the graph (3.10 review M14, M29 to M31, M37). A current cache whose DAC had gone listed field values as
  available, and every read then failed (the 3.12 review of this work, High).
- Decision:
  - A rebuild that fails for want of a DAC (`UntrustedDacException`, `RuntimeComponentMissingException`) opens the older
    cache instead, when its loader version is at least the kind's floor (`SnapshotKindHandler.OldestFallbackLoaderVersion`:
    3 for full dumps, the loader of v3.9.0, the first public release; owner, 2026-11-11). A damaged dump, a cancel, an
    explicit rebuild and a reopen after a worker restart never fall back. The failed load's memory is collected before the
    older cache is read, and its metadata is read alone first (`IndexCacheReader.ReadMetadata`), so the two graphs never
    stack.
  - The process name, the one answer loader version 4 changed, is read again from the dump (no DAC needed:
    `ClrMdDumpLoader.ReadProcessName`), so Compare and the static collections detector match it as a rebuild would.
  - Every full dump opened from a cache checks its DAC first, before the cache is read (`SnapshotKindHandler.CheckDac`):
    one that needs trust is refused when the front end can ask (ADR-035), and otherwise, as when the DAC is missing, the
    graph opens without a reader and value reads are refused from the start with one reason (`ValueReadsRefused`, read by
    `FeatureRules`). The reasons and the older cache's note are short and give a next step that works while the snapshot
    is open (`DacRefusals`, owner's wording of 2026-11-13); the full detail is in the refusal's log line.
  - `SnapshotDescriptor.UntrustedDac` (protocol 22) names an untrusted DAC that is why values are refused; the app shows
    Trust this DAC and reopen, which asks the open's question, trusts the DAC by its hash, asks the memory question when
    the open will load the dump (the probe reports an older loader's cache as one to rebuild), then closes the worker's
    snapshot and opens the dump again under the same handle, so Compare's pair and Trend's ticks keep it.
  - The symbol server's DAC is looked up before the runtime is created, as ClrMD does (by time stamp and size, or by the
    build id of the binary it is archived under, on the dump's platform): a download that fails is a missing DAC, and one
    that arrives but cannot be loaded is a dump that cannot be read, which never falls back.
- Consequences: a dump whose DAC is gone stays readable for its graph (types, references, roots, retained sizes, paths,
  inspections that read no values); the inspections, views and filters that read values say why they cannot. A cache older
  than the floor is never opened in place of a rebuild. Opening from a cache costs one look at the dump's DAC (a ClrMD data
  target, no heap walk). Tests reach a machine without the dump's runtime through a worker test switch
  (`--test-hide-installed-dacs`, honored only with `--enable-test-hooks`).
- Amends: ADR-006 (an older cache can open, with a note, instead of always being rebuilt), ADR-032 (an older cache's
  process name is read again, so the identity rule matches as before), ADR-035 (a DAC that needs trust can also be trusted
  after the open, from the snapshot), ADR-036 (the older cache is read with the same signature check; nothing unsigned opens).
- Amended in 4.5: loader version 5 reads GC handles that an older cache lacks (Server GC processes with fewer heaps than processors) and the
  references to the LoaderAllocator of 3.12, so the note says what such a cache can lack, not only the process name. The oldest version
  that may open stays 3. Such a cache lacks
  the fact `HandlesChecked`, so the unreachable set is refused on every full dump opened from one. The process name is still the one answer
  that is read again.
