# HeapScope.Cli

**See what is filling your .NET heap, why it is still there, and what grew**, from the command line.

Powered by [HeapScope](https://github.com/klundt-org/HeapScope) by Ray Klundt.

HeapScope is a free, open source memory profiler for .NET 5 and later. This package is its command line tool,
`heapscope-cli`; the desktop app and self-contained downloads are on the
[releases page](https://github.com/klundt-org/HeapScope/releases). Your memory dumps never leave your machine.

## Install

```
dotnet tool install --global HeapScope.Cli
```

Installing needs the .NET 10 SDK; running needs the .NET 10 runtime, which the SDK includes. It runs on Windows and Linux
(x64); it is not tested on macOS.

## Use

- `heapscope-cli list`, then `heapscope-cli capture --pid <pid> --out app.dmp`: capture a full dump, or a gcdump with
  `--kind gcdump` (capture runs on Windows for now; on Linux, take a dump with `dotnet-dump`).
- `heapscope-cli summary app.dmp`: what is on the heap, by type, namespace or assembly.
- `heapscope-cli inspect app.dmp`: leak detectors and memory waste, each with its cause and fix.
- `heapscope-cli paths app.dmp --type <type>`: why objects are still alive, from their GC roots.
- `heapscope-cli diff before.dmp after.dmp` and `trend`: what grew between dumps.
- `heapscope-cli monitor`, `trace` and `allocations`: watch a running process and see what it allocates.
- `heapscope-cli sample start`: a sample process with memory problems planted on purpose, to learn on.

`heapscope-cli --help` lists every command. The
[user guide](https://github.com/klundt-org/HeapScope/blob/main/docs/USER-GUIDE.md) and
[install guide](https://github.com/klundt-org/HeapScope/blob/main/INSTALL.md) cover the rest.

## Private by design

A dump holds everything the process had in memory, passwords included. HeapScope never uploads a dump, and it never
reports anything about you or how you use it: no usage statistics, no crash reports. What it does create stays on your
machine (your captures, their indexes, sessions and logs), in `%LOCALAPPDATA%\HeapScope` on Windows and
`~/.local/share/HeapScope` on Linux, and `heapscope-cli purge all` deletes it (dumps you opened from your own folders are
kept). The logs never hold values read from a dump unless you turn that on (it is off by default, and asks you to
confirm), and like everything else they stay on your machine. The text you type in filters is logged unless you turn that
off (`heapscope-cli settings --filter-text-in-logs off`). The only request HeapScope makes over the network is for a runtime
component a dump needs, from Microsoft's symbol server, and only if you allow it; that request sends module identifiers,
never dump contents.

## License

HeapScope is licensed under the GNU Affero General Public License, version 3 only, with
[additional terms](https://github.com/klundt-org/HeapScope/blob/main/ADDITIONAL-TERMS.md), including attribution
requirements. A [commercial license](https://github.com/klundt-org/HeapScope/blob/main/COMMERCIAL.md) is also available.
The package's license file holds all of it.
