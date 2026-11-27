# Installing HeapScope

HeapScope ships on [GitHub Releases](https://github.com/klundt-org/HeapScope/releases) for Windows and Linux (x64), plus untested macOS packages. Each
release has:

| File | What it is |
|---|---|
| `HeapScope-vX.Y.Z-win-x64.zip` | The app (`HeapScope.exe`) and the CLI (`heapscope-cli.exe`) for Windows. No .NET install needed. |
| `HeapScope-vX.Y.Z-linux-x64.tar.gz` | The app (`HeapScope`) and the CLI (`heapscope-cli`) for Linux. No .NET install needed. |
| `HeapScope-vX.Y.Z-osx-arm64.tar.gz`, `HeapScope-vX.Y.Z-osx-x64.tar.gz` | The app and the CLI for macOS (Apple silicon, Intel). **Untested: built on Linux, not on a Mac.** See [macOS](#macos-untested). |
| `HeapScope.Cli.X.Y.Z.nupkg` | The CLI alone as a `dotnet tool`, for systems with the .NET 10 SDK. |
| `HeapScope-vX.Y.Z-sbom-*.cdx.json` | Software bills of materials (CycloneDX) of the app, the CLI, the worker and the tutorial's sample. |
| `SHA256SUMS` | The SHA-256 checksum of every file above. |

Capturing a snapshot of a running process works on Windows and Linux. On macOS it is built but untested; HeapScope also
opens dumps taken with `dotnet-dump` (see [Your first snapshot](#your-first-snapshot)).

## Check the download

HeapScope's files are not signed. Check them against the release's checksums and its build provenance instead.

- Linux: in the download folder, `sha256sum --check --ignore-missing SHA256SUMS`.
- macOS: `shasum -a 256 HeapScope-vX.Y.Z-osx-arm64.tar.gz`, and compare the hash with its line in `SHA256SUMS`.
- Windows (PowerShell): `Get-FileHash .\HeapScope-vX.Y.Z-win-x64.zip`, and compare the hash with its line in
  `SHA256SUMS` (PowerShell prints it in capitals).
- Provenance, with the [GitHub CLI](https://cli.github.com/): `gh attestation verify <file> --repo klundt-org/HeapScope`
  confirms that GitHub Actions built the file from this repository.

## Windows

1. Download `HeapScope-vX.Y.Z-win-x64.zip`.
2. Extract it where you keep programs, for example `%LOCALAPPDATA%\Programs`. It holds one folder,
   `HeapScope-vX.Y.Z-win-x64`. Keep the folder whole: the `worker` folder in it does the analysis, and the `sample` folder holds the Learn page's sample.
3. Run `HeapScope.exe`. The first time, Windows SmartScreen may say "Windows protected your PC", because the files are
   not signed: choose **More info**, then **Run anyway**. To skip that for every file at once, unblock the zip before
   extracting it (right-click it, **Properties**, tick **Unblock**), or in PowerShell: `Unblock-File .\HeapScope-vX.Y.Z-win-x64.zip`.
4. For the CLI, run `heapscope-cli.exe` from that folder, or add the folder to your `PATH`.

## Linux

1. Install the two libraries HeapScope needs, if your system does not have them: ICU and fontconfig.
   - Debian and Ubuntu: `sudo apt install libicu74 libfontconfig1` (the ICU package's number follows the release, for
     example `libicu70` on Ubuntu 22.04; `apt search libicu` shows yours).
   - Fedora: `sudo dnf install libicu fontconfig`.

   The app also needs an X11 or XWayland desktop session, which desktop systems have.
2. Download `HeapScope-vX.Y.Z-linux-x64.tar.gz` and extract it where you keep programs:

   ```bash
   mkdir -p ~/.local/opt && tar -xzf HeapScope-vX.Y.Z-linux-x64.tar.gz -C ~/.local/opt
   ```

3. Put the commands on your path (keep the folder whole: the `worker` folder in it does the analysis, and the `sample` folder holds the Learn page's sample):

   ```bash
   mkdir -p ~/.local/bin
   ln -sf ~/.local/opt/HeapScope-vX.Y.Z-linux-x64/HeapScope ~/.local/bin/heapscope
   ln -sf ~/.local/opt/HeapScope-vX.Y.Z-linux-x64/heapscope-cli ~/.local/bin/heapscope-cli
   ```

4. Run `heapscope` for the app, `heapscope-cli --help` for the CLI.

## macOS (untested)

**These packages are built on Linux, not on a Mac, and have not been run.** They are not signed or notarized, so macOS may
refuse to open them. The steps below are what should work; nobody has confirmed them yet.

1. Download `HeapScope-vX.Y.Z-osx-arm64.tar.gz` (Apple silicon) or `HeapScope-vX.Y.Z-osx-x64.tar.gz` (Intel).
2. Extract it in Terminal: `tar -xzf HeapScope-vX.Y.Z-osx-arm64.tar.gz`. Keep the folder whole: the `worker` folder in it
   does the analysis.
3. Run `./HeapScope` from that folder, or `./heapscope-cli` for the CLI.

Capturing is built for macOS since 4.2.0, and untested too. HeapScope captures the processes that run as your user. macOS may
refuse a full dump of an application that is not signed to allow it; a gcdump does not need that. If capturing fails, take
a dump with `dotnet-dump` and open the file in the app or the CLI (see [Your first snapshot](#your-first-snapshot)).

### If macOS refuses it

Also unconfirmed. Both steps turn off a macOS check for this folder, so [check the download](#check-the-download) first.

- It says the app "cannot be opened" or "cannot be verified": remove the download mark with
  `xattr -dr com.apple.quarantine <folder>`, or allow it under System Settings, Privacy & Security, Open Anyway.
- Terminal says `killed` on Apple silicon: give the files a local signature, the `worker` folder included:
  `find <folder> -type f \( -perm +111 -o -name '*.dylib' \) -exec codesign --force --preserve-metadata=entitlements --sign - {} \;`

If you try a macOS package, please say how it went in an [issue](https://github.com/klundt-org/HeapScope/issues): nobody has
reported yet. In a clone of the repository, `bash build/capture-check.sh <folder>/heapscope-cli` tries every kind of capture
on the sample that comes with HeapScope and says what failed. Its output shows no other process, but it does show your
folders: read it, then put it in the issue. When something failed it also prints where HeapScope's log is. The log names
your processes, so leave it out of the issue and share it only with someone you trust.

To build on your Mac instead, with the .NET 10 SDK, run `bash build/publish.sh osx-arm64 <folder>` (or `osx-x64`) in a clone
of the repository. That build is untested too.

## The CLI as a dotnet tool

If you have the .NET 10 SDK, the CLI also installs as a `dotnet tool` on Windows and Linux (x64; it reads dumps of its
own architecture). Installing needs the SDK (`dotnet tool` is an SDK command); running it needs the .NET 10 runtime,
which the SDK includes. Without the .NET 10 runtime, `heapscope-cli` stops with .NET's own "You must install or update
.NET" message and a download link. Without the SDK, use the archive for your system instead: it needs no .NET.

Install it by name from nuget.org (since 3.13.1), then run `heapscope-cli --help`:

```bash
dotnet tool install --global HeapScope.Cli
```

Its page, https://www.nuget.org/packages/HeapScope.Cli, links back to this repository. To update: `dotnet tool update
--global HeapScope.Cli`. To remove it: `dotnet tool uninstall --global HeapScope.Cli`. The tool works like the CLI in
the archives (capture on Windows and Linux, and untested on macOS).

Or install it from the release's file, without nuget.org:

1. Make a folder of its own, for example `heapscope-packages` in your home folder, and put `HeapScope.Cli.X.Y.Z.nupkg`
   in it.
2. In that folder, save this as `nuget.config`. It tells `dotnet` to use this folder and no other package source:

   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <configuration>
     <packageSources>
       <clear />
       <add key="heapscope" value="." />
     </packageSources>
   </configuration>
   ```

3. Install it, naming the version and that file (Linux shown; in PowerShell use `$HOME\heapscope-packages\nuget.config`):

   ```bash
   dotnet tool install --global HeapScope.Cli --version X.Y.Z --configfile ~/heapscope-packages/nuget.config
   ```

4. Run `heapscope-cli --help`.

To update, put the new version's file in that folder and run `dotnet tool update --global HeapScope.Cli --version X.Y.Z
--configfile ~/heapscope-packages/nuget.config` with the new version (in PowerShell,
`$HOME\heapscope-packages\nuget.config`).

## Your first snapshot

- Windows, in the app: the Processes page lists the .NET processes you can capture; pick one and capture a full dump
  or a gcdump. The CLI does the same: `heapscope-cli list`, then `heapscope-cli capture --pid <pid> --out app.dmp`.
  Then `heapscope-cli summary app.dmp` shows what is on its heap. Capturing a process that runs as administrator needs
  HeapScope run as administrator too.
- Linux: the same, in the app or with `heapscope-cli`. HeapScope captures the processes that run as your user; another
  user's process needs HeapScope run as that user, or as root (`sudo`). sudo does not keep your `PATH`, so give it the full
  path, as in `sudo "$(command -v heapscope-cli)" capture --pid <pid> --out /tmp/app.dmp` (the archive's link in
  `~/.local/bin`, or the dotnet tool). The dotnet tool also needs the .NET it runs on: if you
  installed .NET in your home folder (`~/.dotnet`), add `DOTNET_ROOT="$HOME/.dotnet"` after `sudo`. Run with sudo, HeapScope keeps
  your data folder (your sessions, logs and settings, not root's) and gives you what it writes, the dump included, so it is
  yours to open and delete afterwards. As root, a full dump of another user's process goes to a folder that user can write
  (`--out`, such as `/tmp`): the runtime writes it as that user, and HeapScope then makes it yours. Some systems stop one process from examining
  another (`kernel.yama.ptrace_scope` of 2 or 3, or a container without `SYS_PTRACE`); HeapScope then says what to
  change, and changes nothing itself. A process that keeps its diagnostics socket in another temp folder (its `TMPDIR`)
  is found when HeapScope runs with the same `TMPDIR`.
- macOS (untested): in the app or with `heapscope-cli`, for the processes that run as your user. If that fails, take the dump
  with `dotnet-dump collect --process-id <pid> --type Full` (`dotnet tool install --global dotnet-dump` installs it) and open
  the file.

HeapScope analyzes dumps of .NET 5 and later. Its answers are checked on every change against dumps of .NET 8, 9 and 10; a
full dump of another version opens with a note that its answers are not checked, and one older than .NET 5 with a note that
HeapScope does not analyze it. A heap dump of .NET 8 taken on Windows (`dotnet-dump collect --type Heap`) leaves out the static
fields: take a full dump (`--type Full`, or HeapScope's own capture).

### A missing DAC

To open a dump, HeapScope needs the dumped runtime's data access component (DAC): `mscordaccore.dll` on Windows,
`libmscordaccore.so` on Linux, `libmscordaccore.dylib` on macOS. It must be the DAC of the exact .NET version the dumped
process ran on, for the same system and processor as the dump. HeapScope looks for it, in this order:

1. in the .NET runtimes installed on this computer (`shared/Microsoft.NETCore.App/<version>` under the dotnet folder);
2. in the DAC folder: the one on the Settings page, or `--dac-folder <folder>` for one command line run (a `--dac-folder`,
   or `Symbols:DacFolder` in `appsettings.json`, wins over the Settings page);
3. in HeapScope's own runtime, when its version matches;
4. next to the dump, in its folder or in `dac/<version>` beside it, where HeapScope's own capture puts the target's DAC. A DAC
   HeapScope's capture copied is trusted. Any other DAC there came with the dump and is native code from whoever made it: the
   app asks before using it and remembers a yes, and the CLI uses it only with `--trust-dac <sha256>`, which its refusal names;
5. on Microsoft's symbol server, only when you turned on **Download missing DACs from Microsoft** on the Settings page (or
   `heapscope-cli settings --symbol-server on --yes`, or `--allow-symbol-server` for one run). Off by default: a download
   sends the runtime module's name, time stamp and size to Microsoft, never the dump's contents.

When none matches, the message names the version it needs and the folders it searched. The simplest fix is to install that
.NET runtime. Otherwise copy the DAC from the machine the dump came from into a folder of your own, and choose that folder
on the Settings page:

- From an installed runtime: the DAC is in `shared/Microsoft.NETCore.App/<version>/` under the dotnet folder, for example
  `C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.11\mscordaccore.dll` on Windows or
  `/usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.11/libmscordaccore.so` on Linux (`/usr/lib/dotnet` for some
  distributions' packages). On Linux and macOS copy `libcoreclr.so` (`libcoreclr.dylib`) from the same folder too:
  HeapScope checks the two came from the same build.
- From a self-contained application: the same files are in the application's own folder.
- From a container: `docker cp <container>:/usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.11/libmscordaccore.so .`, and
  `libcoreclr.so` the same way.

The folder must be on this computer: HeapScope never searches a network folder for a DAC.

## Where HeapScope keeps its data

Sessions and logs go to `%LOCALAPPDATA%\HeapScope` on Windows, `~/.local/share/HeapScope` on Linux (under `$XDG_DATA_HOME` when it is set) and
`~/Library/Application Support/HeapScope` on macOS, or to the folder the `HEAPSCOPE_DATA_FOLDER` environment variable names. Sessions hold
full dumps, which can contain anything the dumped process held (passwords and keys included). A snapshot's index cache
goes next to its dump, or into that folder when the dump's folder cannot be written. The folder also holds `settings.json`,
which the Settings page and `heapscope-cli settings` write. Logs are described in the [user guide](docs/USER-GUIDE.md#logs-and-getting-help),
under "Logs and getting help".

## Settings

The Settings page (and `heapscope-cli settings`) sets what every HeapScope process on the machine shares: the DAC folder and
whether a missing DAC may be downloaded from Microsoft (off by default; turning it on asks you to confirm, or needs `--yes` on
the command line), whether the text typed in filters is written into the logs (on by default), and whether values read from
dumps are (off by default; turning it on asks you to confirm, or needs `--yes`). It is saved in `settings.json` in the data
folder.

HeapScope reads its other settings from an `appsettings.json` file you create beside `HeapScope` and `heapscope-cli`, and from
environment variables that name the setting with `__` between its parts. For example, a DAC folder for every user of this
installation, which wins over the Settings page:

```json
{ "Symbols": { "DacFolder": "/srv/dacs" } }
```

On Windows, write each \ of a path twice in JSON:

```json
{ "Symbols": { "DacFolder": "C:\\dacs" } }
```

or `Symbols__DacFolder=/srv/dacs` in the environment. A file HeapScope cannot read is named with its line when a command
starts. An `appsettings.json` stays behind when you extract a new
release, so copy it across; for the dotnet tool, use environment variables, since an update replaces the tool's folder.

## If HeapScope does not start

- Run it from a terminal to see its message, and look in the `logs` folder of the data folder above. For a more
  detailed log of one run, set `Logging__File__MinimumLevel=Debug` (the app also takes
  `--Logging:File:MinimumLevel Debug`).
- Windows SmartScreen: see the Windows steps above. Windows 11's Smart App Control blocks programs that are not signed
  and has no "Run anyway"; HeapScope's builds are not signed, so they cannot run where it is on.
- macOS: see [If macOS refuses it](#if-macos-refuses-it).
- Linux, a message about ICU ("Couldn't find a valid ICU package"): install ICU (see the Linux steps above).
- Linux, the app stops while starting its display (SkiaSharp cannot load `libSkiaSharp`): install fontconfig.
- The dotnet tool, "You must install or update .NET": install the .NET 10 runtime, or use the archive.

## Getting help

If something here does not work, open an issue on [HeapScope's issues](https://github.com/klundt-org/HeapScope/issues) with the
details the About page copies (Copy details) or `heapscope-cli --version`. [SUPPORT.md](SUPPORT.md) says what to include and how to
check the log before you attach it.

## Updating and removing

There is no installer and nothing in the registry. To update, extract the new release next to the old one (and point
the Linux links at it, and copy any `appsettings.json`); to remove HeapScope, delete its folder and the links. Your
data folder, with its dumps, stays until you delete it (Purge all on the Settings page, or `heapscope-cli purge all --yes`,
deletes its logs, sessions and bookmarks first); on Windows, trace conversions may also leave files in
`%TEMP%\HeapScope`. The [releases page](https://github.com/klundt-org/HeapScope/releases) lists every version; watch the repository's
releases on GitHub to hear of new ones.
