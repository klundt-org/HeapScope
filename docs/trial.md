# Trying HeapScope

Thank you for trying HeapScope before its first full release. This page says what to try, how to tell us what you found, and
what is known not to work yet. Your dumps never leave your machine: nothing here asks you to send one.

## Before you start

- Install a release as [INSTALL.md](../INSTALL.md) says: the Windows or Linux download needs no .NET, and the command line is also a
  `dotnet tool`. macOS packages exist but nobody has run them yet.
- Note your version: the About page's **Copy details** (or `heapscope-cli --version`). Every report needs it.
- Have a .NET 5 or later process of your own to look at: a service, a test environment, anything whose memory you would like to
  understand. HeapScope's own sample works too.

## What to try

Roughly in this order; stop wherever it stops being useful to you, and tell us where that was.

1. **Learn on the sample.** In the app, the **Learn** page: choose a lesson, press **Start lesson** and follow it. How long did it
   take you to find the planted problem, and where did you get stuck?
2. **Your own process.** On **Processes**, choose it and press **Capture dump** (or a gcdump). Then the **Inspections** tab: does a
   finding point at something real in your code? Is one wrong, or missing something you know is there?
3. **Why something is alive.** Pick a type you expected to be gone: **Retainers**, and the paths from the GC roots. Did the path
   explain it?
4. **What grew.** Capture again after your process has run a while, then **Compare** the two (or `heapscope-cli diff`). Does
   the growth match what you see in your process's own metrics?
5. **Over time.** The **Live** page: watch the process, set a rule that captures a dump when the heap grows, and record
   allocations. Did the rule fire when it should have?
6. **From elsewhere.** A dump taken on another machine, a server or a container. If HeapScope asks for a DAC, did the message and
   the Settings page's DAC folder get you through ([A missing DAC](../INSTALL.md#a-missing-dac))?
7. **On Linux.** Another user's process with `sudo` ([INSTALL.md](../INSTALL.md), Linux): did the dump end up yours to open?
8. **In a script.** Any command with `--format json` ([the JSON output](cli-output.md)): could you build the check you wanted?

## How to report

Open an issue on [HeapScope's issues](https://github.com/klundt-org/HeapScope/issues) and choose the form that fits: a bug, a
result that looks wrong, or a question. [SUPPORT.md](../SUPPORT.md) says what to include. In short:

- the details **Copy details** puts on the clipboard (version, system, the id of the last unexpected failure);
- what you did, what you expected and what happened, in words, with the message HeapScope showed;
- the kind of snapshot and the .NET version of the process it came from.

Issues are public. A log helps with almost anything, so paste the lines around the message, after you have read them and replaced what
you do not want public: a log holds file paths with your user name and the names from your code (SUPPORT.md says what else). If
HeapScope closed on its own, it offers a crash report at its next start; paste its error and first lines of stack. If more is
needed, say so in the issue and it can be shared privately. **Never attach a dump, a gcdump, a trace or an index cache**: they hold
your process's data.

What we most want to hear:
- a result that is wrong (a count, a size, a path, a finding), even if you are not sure;
- a text that confused you, or a message that left you without a next step;
- how long it took you to get a first useful answer, and what slowed you down.

## Known not to work yet

- **macOS** is built but untested: the packages have never been run, and they are not signed or notarized.
- **Nothing is signed.** Windows SmartScreen may warn before the first start, and the download may need unblocking (INSTALL.md
  says how). Check a download against the release's `SHA256SUMS` or its attestation instead.
- **x64 only on Windows and Linux.** There are no Arm64 builds for them.
- **.NET 5 and later only.** Older runtimes are refused with a note. Answers are checked against known results on .NET 8, 9 and 10;
  a dump of another version opens with a note that its answers are not checked.
- **A .NET 8 heap dump taken on Windows** (`dotnet-dump collect --type Heap`) lacks the static fields, so what statics hold can look
  unreachable; HeapScope says so. Take a full dump instead.
- **Capturing another user's process** needs HeapScope run as that user, or as administrator or root; some Linux systems block it
  altogether (`kernel.yama.ptrace_scope`). HeapScope says what to change and changes nothing itself.
- **Large dumps need memory**: a first load takes about half the dump's size plus 0.7 GB, and HeapScope warns before a load that may
  not fit. Later opens use the saved index and much less.
- **A full dump runs native code** when it is read (its runtime's DAC): open dumps from other people as your own user, never
  elevated ([Dumps you did not make](../INSTALL.md#dumps-you-did-not-make)).
- **Live sessions and snapshots share one worker process**: if a damaged dump ends the worker, a live session ends with it. Restart
  worker, at the bottom of the window, starts a new one.
- **The app on Linux** needs an X11 or XWayland session.
- **Outside contributions are not accepted yet**, issues are: see [CONTRIBUTING.md](../CONTRIBUTING.md).
