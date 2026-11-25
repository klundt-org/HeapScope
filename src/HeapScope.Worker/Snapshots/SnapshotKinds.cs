// HeapScope
// Copyright (C) 2026 Ray Klundt
// https://github.com/klundt-org/HeapScope
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, version 3 only, with additional terms
// under Section 7 of that license.
//
// Additional terms, including attribution requirements, apply.
// See the ADDITIONAL-TERMS.md file distributed with this program.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program (see the LICENSE file). If not, see
// <https://www.gnu.org/licenses/>.
//
// Commercial licenses are available. See COMMERCIAL.md.

using System.Collections.Concurrent;
using HeapScope.Core;
using HeapScope.Core.Graph;
using HeapScope.Core.Loading;
using HeapScope.Loaders.ClrMd;
using HeapScope.Loaders.GcDump;
using HeapScope.Storage;

namespace HeapScope.Worker.Snapshots;

/// <summary>
/// What the worker does differently per snapshot kind (sprint 1.9): the loader and its version, how an opened graph
/// becomes an <see cref="ISnapshot"/> (with or without a reader of the original file), the kind's data quality notes,
/// why a loader fact is missing, and how its type names are matched against another kind's. The worker asks these
/// handlers instead of checking the kind itself. Contracts' <c>CapabilityDescriptions</c> also words explanations per
/// kind, for front ends (ADR-016).
/// </summary>
internal abstract class SnapshotKindHandler
{
    public abstract SnapshotKind Kind { get; }

    /// <summary>The current version of the kind's loader; index caches from an older one are rebuilt.</summary>
    public abstract int LoaderVersion { get; }

    /// <summary>
    /// The oldest loader version whose index cache opens in place of a rebuild that cannot reach the native code it needs
    /// (sprint 3.12, the 3.10 review M14 and M30); a cache older than this is rebuilt or the open fails. A kind that loads no
    /// native code never falls back, so by default only the current version qualifies.
    /// </summary>
    public virtual int OldestFallbackLoaderVersion => LoaderVersion;

    public abstract ISnapshotLoader CreateLoader();

    /// <summary>
    /// The snapshot over an opened graph. <paramref name="ensureUnchanged"/> checks the file before a reader uses it. With
    /// <paramref name="refused"/>, the snapshot reads no values from the file and says why (sprint 3.12).
    /// </summary>
    public abstract ISnapshot Open(SnapshotInfo info, SnapshotCapabilities capabilities, HeapGraph graph, string path, Action ensureUnchanged, ValueReadsRefused? refused = null);

    /// <summary>Notes on what this kind of snapshot can and cannot answer, given what its loader recorded.</summary>
    public abstract void AddNotes(IndexCacheMetadata metadata, List<string> notes);

    /// <summary>Why a feature named <paramref name="title"/> lacks a loader fact (<see cref="SnapshotFacts"/>) here.</summary>
    public abstract string ExplainMissingFact(string title);

    /// <summary>
    /// Why this kind holds no garbage (objects no root reaches that the process had not collected yet), or null when it
    /// holds every object, garbage included.
    /// </summary>
    public abstract string? WhyNoGarbage { get; }

    /// <summary>Whether this kind holds every object, garbage not yet collected included.</summary>
    public bool HoldsGarbage => WhyNoGarbage is null;

    /// <summary>
    /// Whether the GC handles of a snapshot of this kind are all there, as far as its loader can tell
    /// (<see cref="SnapshotFacts.HandlesChecked"/>): otherwise an object held only by one can look unreachable.
    /// </summary>
    public virtual bool HandlesChecked(SnapshotInfo info) => true;

    /// <summary>What "no root reaches it" means for an object of a snapshot of this kind (review M3).</summary>
    public abstract string UnreachableNote(SnapshotInfo info);

    /// <summary>
    /// Why objects of this kind cannot be matched by address with another snapshot's (ADR-032), or null when they keep
    /// the addresses they had in the process.
    /// </summary>
    public abstract string? WhyNoObjectIdentity { get; }

    /// <summary>
    /// How this kind's type names are mapped before they are matched with a snapshot of <paramref name="other"/>, or null
    /// when they are matched as they are (a diff across kinds, sprint 1.8).
    /// </summary>
    public abstract Func<string, string>? NamesWhenComparedWith(SnapshotKind other);

    /// <summary>
    /// Why a snapshot opened from its index cache could not read field values later (sprint 3.6 review H1, sprint 3.12): the
    /// full dump's DAC lies untrusted next to the dump (<see cref="UntrustedDacException"/>) or is missing
    /// (<see cref="RuntimeComponentMissingException"/>). Null when the reads can go ahead, and for kinds that load no native code.
    /// </summary>
    public virtual Exception? CheckDac(string path) => null;

    /// <summary>
    /// The process name the current loader reads from <paramref name="path"/> and its rule, or null for a kind whose name never
    /// changed between loader versions (sprint 3.12: an older full dump cache opened in place of a rebuild).
    /// </summary>
    /// <exception cref="SnapshotFormatException">The file cannot be read for it.</exception>
    public virtual (string? Name, string Source)? ReadProcessName(string path) => null;
}

/// <summary>The handlers of every kind, chosen by kind or by a file's first bytes.</summary>
/// <param name="dacChosen">Told which DAC each full dump's runtime was created with (sprint 2.3.7).</param>
internal sealed class SnapshotKinds
{
    // For what does not depend on the worker's options (texts, name forms, versions): explanations and diffs.
    private static readonly SnapshotKinds s_descriptions = new(new WorkerOptions(string.Empty, null));

    // DACs the user trusts to load from next to a dump, by SHA-256, as open requests named them (sprint 3.6, peer review
    // P2). Kept for the worker's lifetime: the field reader opens the dump again later with the same trust.
    private readonly ConcurrentDictionary<string, byte> _trustedDacs = new(StringComparer.Ordinal);

    private readonly SnapshotKindHandler[] _handlers;

    public SnapshotKinds(WorkerOptions options, Action<DacChoice>? dacChosen = null)
    {
        _handlers = [new FullDumpKind(options, dacChosen, _trustedDacs), new GcDumpKind()];
    }

    /// <summary>Adds the DACs an open request trusts.</summary>
    public void Trust(IReadOnlyList<string>? sha256s)
    {
        foreach (var sha256 in sha256s ?? [])
        {
            if (sha256 is { Length: 64 } && sha256.All(char.IsAsciiHexDigitLower))
            {
                _trustedDacs.TryAdd(sha256, 0);
            }
        }
    }

    /// <summary>The handler of <paramref name="kind"/> for texts, name forms and versions (not for loading).</summary>
    public static SnapshotKindHandler Describe(SnapshotKind kind) => s_descriptions.For(kind);

    public SnapshotKindHandler For(SnapshotKind kind) =>
        _handlers.FirstOrDefault(h => h.Kind == kind) ?? throw new ArgumentOutOfRangeException(nameof(kind), $"No handler for snapshot kind {kind}.");

    /// <summary>The handler for the file at <paramref name="path"/>, by its content, never its extension (sprint 1.8).</summary>
    /// <exception cref="SnapshotFormatException">The file is neither kind.</exception>
    public SnapshotKindHandler ForFile(string path) =>
        SnapshotFileSignature.Detect(path) is { } kind ? For(kind) : throw new SnapshotFormatException(SnapshotFileSignature.DescribeUnknown(path));

    private sealed class FullDumpKind(WorkerOptions options, Action<DacChoice>? dacChosen, ConcurrentDictionary<string, byte> trustedDacs) : SnapshotKindHandler
    {
        public override SnapshotKind Kind => SnapshotKind.FullDump;

        public override int LoaderVersion => ClrMdDumpLoader.CurrentVersion;

        // The loader of v3.9.0, the first public release (owner, 2026-11-11): every cache a user can have, and none from the
        // development builds before it, whose answers are known to be wrong in places.
        public override int OldestFallbackLoaderVersion => 3;

        public override ISnapshotLoader CreateLoader() => new ClrMdDumpLoader(LoaderOptions());

        public override Exception? CheckDac(string path) => ClrMdDumpLoader.CheckDac(path, LoaderOptions());

        public override (string? Name, string Source)? ReadProcessName(string path) => ClrMdDumpLoader.ReadProcessName(path, LoaderOptions());

        // Field values and strings are read from the dump on demand (sprint 1.5); the reader opens the dump only when
        // something is inspected, after checking it is still the file the graph was built from.
        public override ISnapshot Open(SnapshotInfo info, SnapshotCapabilities capabilities, HeapGraph graph, string path, Action ensureUnchanged, ValueReadsRefused? refused = null) =>
            refused is null
                ? new DumpSnapshot(info, capabilities, graph, new ClrMdObjectReader(path, LoaderOptions(), ensureUnchanged))
                : new DumpSnapshot(info, capabilities, graph, refused);

        public override void AddNotes(IndexCacheMetadata metadata, List<string> notes)
        {
            if (!metadata.Capabilities.HasFlag(SnapshotCapabilities.CompleteGraph))
            {
                notes.Add("Parts of the managed heap could not be read from this dump, so some objects may be missing and counts are not exact. The dump may be truncated or damaged; capture another full dump if results look wrong.");
            }

            // An older cache, which recorded no walk, has its own note (SnapshotOpener.OlderCacheNote).
            if (metadata.Handles is not null && !HandlesChecked(metadata.Info))
            {
                notes.Add("HeapScope could not check all GC handles of this Server GC process. Objects held only by a handle, and what they hold, can look unreachable, and leak detectors can miss them. To see what is alive, capture a gcdump while the process runs. To check again, open the dump with Rebuild.");
            }

            if (metadata.Info.Facts.HasFlag(SnapshotFacts.CaptureTimeFromFile))
            {
                notes.Add("This core dump does not record when it was taken; its capture time is the file's time, which a copy of the file can change.");
            }
        }

        public override string ExplainMissingFact(string title) =>
            $"{title} needs data newer versions of HeapScope record, and this snapshot's index cache is older. Reopen the dump with Rebuild.";

        public override string? WhyNoGarbage => null;

        public override bool HandlesChecked(SnapshotInfo info) => info.Facts.HasFlag(SnapshotFacts.HandlesChecked);

        public override string UnreachableNote(SnapshotInfo info) => HandlesChecked(info)
            ? "No root reaches it: it is garbage not yet collected, or held only weakly (weak handles, weak references)."
            : "No root HeapScope could read reaches it. Not all GC handles could be checked for this snapshot (see its notes), so it may still be alive.";

        public override string? WhyNoObjectIdentity => null;

        // A gcdump cannot name a nested type with its declaring type, so a full dump's names take the gcdump's form.
        public override Func<string, string>? NamesWhenComparedWith(SnapshotKind other) =>
            other == SnapshotKind.GcDump ? TypeNameForms.AsGcDumpName : null;

        private ClrMdLoaderOptions LoaderOptions() => new()
        {
            DacFolder = options.DacFolder,
            AllowMicrosoftSymbolServer = options.AllowMicrosoftSymbolServer,
            SecondHandleWalk = options.SecondHandleWalk,
            DacChosen = dacChosen,
            IsTrustedDac = trustedDacs.ContainsKey,
            TrustedDacCopyFolder = options.Folders.DacCopies,

            // A machine without the dump's runtime, for tests (WorkerOptions.HideInstalledDacsSwitch).
            InstalledRuntimeRoots = options.HideInstalledDacs ? [] : null,
            SearchDumpedRuntimeDirectory = !options.HideInstalledDacs,
            OwnRuntimeDirectory = options.HideInstalledDacs ? null : new ClrMdLoaderOptions().OwnRuntimeDirectory,
        };
    }

    private sealed class GcDumpKind : SnapshotKindHandler
    {
        public override SnapshotKind Kind => SnapshotKind.GcDump;

        public override int LoaderVersion => GcDumpLoader.CurrentVersion;

        public override ISnapshotLoader CreateLoader() => new GcDumpLoader();

        // A gcdump holds no field values or string text: the graph is all there is.
        public override ISnapshot Open(SnapshotInfo info, SnapshotCapabilities capabilities, HeapGraph graph, string path, Action ensureUnchanged, ValueReadsRefused? refused = null) =>
            new GraphSnapshot(info, capabilities, graph);

        public override void AddNotes(IndexCacheMetadata metadata, List<string> notes)
        {
            notes.Add("A gcdump holds the objects that survived the full garbage collection its capture ran, with their types, sizes and references. It has no generations, field values or string text; capture a full dump for those.");
            if (!metadata.Capabilities.HasFlag(SnapshotCapabilities.CompleteGraph))
            {
                notes.Add("This gcdump is sampled or incomplete (its writer kept only part of a very large heap, or the runtime's events were lost while it was captured), so some objects or types are missing and counts are not exact. Capture another gcdump, or a full dump, if results look wrong.");
            }
        }

        public override string ExplainMissingFact(string title) =>
            $"{title} needs data a gcdump does not record. Capture a full dump for it.";

        public override string? WhyNoGarbage =>
            "A gcdump holds only what survived its collection, so the garbage of the process is not in it. Open a full dump (.dmp) to see the garbage it still holds.";

        // Without weak handles in a gcdump, an unreached survivor points to a root the gcdump did not record.
        public override string UnreachableNote(SnapshotInfo info) =>
            "No recorded root reaches it. A gcdump holds only objects that survived its collection, so a root that holds it was not recorded (the gcdump may be sampled or incomplete).";

        public override string? WhyNoObjectIdentity =>
            "New and dead objects need two full dumps: a gcdump records objects after a collection it forced, which moves them, and says no process id.";

        public override Func<string, string>? NamesWhenComparedWith(SnapshotKind other) => null;
    }
}
