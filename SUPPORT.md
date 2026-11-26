# Getting help

Questions, problems and wrong results all go to one place: an issue on HeapScope's GitHub page,
<https://github.com/klundt-org/HeapScope/issues>. Choose **New issue**, then the form that fits: a bug, a result that looks
wrong, or a question. Security problems are the exception: report them privately, as [SECURITY.md](SECURITY.md) says.

## What to include

- **The details from the About page.** Its **Copy details** button puts HeapScope's version, the system and the log folder on the
  clipboard (`heapscope-cli --version` gives the version). Nothing is sent anywhere.
- **What you did, what you expected and what happened**, in words, with the message HeapScope showed. A message that gives an id
  ("with this id: ...") lets the log's lines for it be found.
- **The kind of snapshot** (a full dump, a gcdump, a core dump) and the .NET version of the process it came from, if you know it.

Never attach a memory dump, a gcdump, a trace or an index cache: they hold everything the process had in memory. Describe how to
make one instead; HeapScope's own sample process and the fixtures in `fixtures/` are safe to use.

## The log

HeapScope writes a log for each run, in the `logs` folder the About page names. It records the versions, the snapshots opened and why
each result came out as it did, so it usually answers "why".

Before you attach one, read it. A log holds:
- file paths (of dumps and folders) and the names of types and methods in the process you analyzed;
- the text you type in filters, unless you turned off **Include filter text in logs** on the Settings page;
- field values and strings read from dumps only if you turned on **Include dump values in logs** (off by default; each log says
  whether it holds them).

Attach the log, or paste only the lines around the message's id, after you have removed what you do not want to share. If you are
not sure, describe the problem first and say that you have a log.

## More

- [INSTALL.md](INSTALL.md), "If HeapScope does not start", and [the user guide](docs/USER-GUIDE.md#logs-and-getting-help).
- Commercial licensing: rayklundt (at) outlook (dot) com.
