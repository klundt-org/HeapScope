# Getting help

Questions, problems and wrong results all go to one place: an issue on HeapScope's GitHub page,
<https://github.com/klundt-org/HeapScope/issues>. Choose **New issue**, then the form that fits: a bug, a result that looks
wrong, or a question. **Issues are public**: anyone can read what you put in one, so write nothing there you would not publish.
Security problems are the exception: report them privately, as [SECURITY.md](SECURITY.md) says.

## What to include

- **The details from the About page.** Its **Copy details** button puts HeapScope's version, the system, the log folder and the id
  of the last unexpected failure on the clipboard (`heapscope-cli --version` gives the version). Nothing is sent anywhere. The
  log folder's path has your user name in it: replace it if you would rather not publish it.
- **What you did, what you expected and what happened**, in words, with the message HeapScope showed. A message that gives an id
  ("with this id: ...") lets the log's lines for it be found.
- **The kind of snapshot** (a full dump, a gcdump, a core dump) and the .NET version of the process it came from, if you know it.

Never attach a memory dump, a gcdump, a trace or an index cache: they hold everything the process had in memory. Describe how to
make one instead; HeapScope's own sample process and the fixtures in `fixtures/` are safe to use.

## The log

HeapScope writes a log for each run, in the `logs` folder the About page names. It records the versions, the snapshots opened and why
each result came out as it did, so it usually answers "why". A log is for you and for whoever helps you, not for a public page. It
holds:
- file paths (of dumps and folders, with your user name) and the names of types and methods in the process you analyzed;
- the text you type in filters, unless you turned off **Include filter text in logs** on the Settings page. Filter text can be a
  value from your dump (an account number, a token, a name), and a log written before you turned it off still holds it;
- field values and strings read from dumps only if you turned on **Include dump values in logs** (off by default; each log says
  whether it holds them).

In an issue, paste only the lines around the message's id (or the message, if it has none), after you have read them and replaced
what you do not want public. Never paste or attach a whole log. If the lines are not enough, say so in the issue: a whole log can
then be shared privately, by email to rayklundt (at) outlook (dot) com, and is never posted. **Purge logs** on the Settings page
deletes HeapScope's logs when you no longer need them.

A crash report (`heapscope-...-crash-....log`, which HeapScope offers after it closed unexpectedly) is a log like the others: read it,
and paste the error and its first lines of stack, not the whole file.

## More

- [INSTALL.md](INSTALL.md), "If HeapScope does not start", and [the user guide](docs/USER-GUIDE.md#logs-and-getting-help).
- Commercial licensing: rayklundt (at) outlook (dot) com.
