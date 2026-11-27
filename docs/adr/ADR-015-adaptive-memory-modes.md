# ADR-015 Adaptive memory modes (added 2026-10-22, follows from the ADR-004 amendment)

- Context: on a 16 GB laptop the target API, an IDE and the worker compete for memory; a Medium snapshot with
  2.x indices needs about 3 GB of graph data on top of everything else.
- Decision: graph data lives in `Column<T>` values backed by either managed arrays (in-memory mode) or
  read-only memory-mapped index cache sections (mapped mode). The worker picks the mode per snapshot from a
  memory budget derived from physical and available memory; configuration can force either mode.
- Consequences: index cache sections are page aligned and stored in the in-memory layout so they can be mapped
  without conversion; algorithms take spans only; the index cache moves from "nice to have" to required for
  mapped mode.
- Amended in sprint 2.3: optional indices follow the snapshot's mode (`ReverseIndexBuilder.ChooseMode`) and, with an
  index cache, are computed straight into their mapped sidecars, so a mapped computation holds only its working arrays.
  The 1.9 decision to reconsider the graph's own mode when indices are computed is not built: an index whose working
  arrays do not fit is refused with InsufficientMemory and a next step (decision 4); switching a live in-memory graph
  to mapped is a Backlog item.
- Amended in sprint 4.7: a full dump's first load keeps its references (`Edges`) in a private temporary file it writes as it
  reads them and maps read-only, owned by the graph, in either mode: a third backing beside managed arrays and cache sections.
  Chosen over writing them straight into the index cache, as the sprint first planned, because their count is not known until
  the reference pass ends (the cache's layout needs it) and the cache's copy of them is file I/O, not the worker's memory. Its
  cost: the references are on disk twice (the file and the cache) while the snapshot is open, the file on the temporary
  folder's drive; a snapshot kept in memory keeps the file until it closes. With the collection of the frozen chunks, it took the
  first load's peak down by about 40% on a 2 GB DenseHeap dump (budgets.md; the 8 GB dump is still to be measured).
- Amended in sprint 4.8 (task 11, F13): the memory mode's log line gives the graph's private and file-backed parts
  (`HeapGraph.PrivateColumnBytes`, `FileBackedColumnBytes`; a file-backed column is an `IFileBackedMemory` view, the references'
  temporary file of a load). The mode is still chosen by all the columns: the sprint first chose by the private part, and its
  review (M4) kept the rule, since a later open from the cache in memory reads the references into the worker's memory, and a
  first load and a reopen must decide the same way. The switch itself runs after the method that loaded the graph has returned, so no local of that frame
  keeps the graph alive through the collection (it kept all of it before: the 2 GB dump's 698 MB of GC heap after the drop, 8 MB
  now; budgets.md).
