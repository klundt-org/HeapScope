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

using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using HeapScope.Core;
using HeapScope.Core.Graph;
using HeapScope.Core.Loading;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.DataReaders.Implementation;

namespace HeapScope.Loaders.ClrMd;

/// <summary>
/// Loads a full dump into the compact graph with ClrMD (ARCHITECTURE.md 6.1 and 6.3): an object pass per segment
/// (types, sizes, heap kinds, finalizer flags), dependent handles as edges, a reference pass in address order,
/// then roots (GC roots, weak handles and static fields). Synchronous and CPU bound; callers run it on a worker
/// thread. Cancellation is checked at least every 64K objects.
/// </summary>
public sealed class ClrMdDumpLoader(ClrMdLoaderOptions? options = null) : ISnapshotLoader
{
    private const int CheckInterval = 1 << 16;

    /// <summary>The detail of the strong handle that holds the static fields; PathRoots has its wording.</summary>
    private const string StaticStorage = "static storage";

    private readonly ClrMdLoaderOptions _options = options ?? new ClrMdLoaderOptions();

    /// <summary>
    /// 2: truncated dumps lose CompleteGraph and ExactCounts, finalizer registrations are a fact. 3:
    /// core files get their own truncation check and the runtime version ClrMD reads as 0.0 is named by the
    /// runtime's folder. 4: the process name is the executable's, not the first module the reader lists. 5:
    /// the GC handles of a Server GC process whose heap count changed (<see cref="HandleTables"/>), and the
    /// LoaderAllocator references for caches written before them. 6: struct statics with references are named by
    /// their field, not only as the runtime's static storage.
    /// 7: the static storage of .NET 8, which it reports only as strong handles, is named as such,
    /// and thread statics are named by their field, "(thread static)". 8 since its review: the statics are checked
    /// (SnapshotFacts.StaticsChecked), whose bit builds of 7 used with the other meaning.
    /// Caches written before are rebuilt. On a bump, an older cache may still open in place of a rebuild that cannot reach the
    /// DAC (ADR-041): reword <c>SnapshotOpener.OlderCacheNote</c> and its process name step for what the new version changed,
    /// or raise the full dump kind's <c>OldestFallbackLoaderVersion</c> (a test stops the build until one is done).
    /// </summary>
    public const int CurrentVersion = 8;

    /// <summary>
    /// Checks, without loading it, that the DAC the dump needs may be loaded, for a dump opened from
    /// its index cache: its field reader loads the DAC later, where nobody can be asked.
    /// </summary>
    /// <exception cref="UntrustedDacException">The only matching DAC lies next to the dump and is not trusted.</exception>
    public static void CheckDacTrust(string dumpPath, ClrMdLoaderOptions options) => ClrMdDump.CheckDacTrust(dumpPath, options);

    /// <inheritdoc cref="ClrMdDump.CheckDac"/>
    public static Exception? CheckDac(string dumpPath, ClrMdLoaderOptions options) => ClrMdDump.CheckDac(dumpPath, options);

    /// <summary>
    /// The process name this loader records for the dump and where it came from: an older index cache opened in
    /// place of a rebuild takes the current name, the one thing loader version 4 reads differently. Reads no DAC.
    /// </summary>
    /// <exception cref="SnapshotFormatException">The file is not a readable memory dump.</exception>
    public static (string? Name, string Source) ReadProcessName(string dumpPath, ClrMdLoaderOptions options)
    {
        using var target = ClrMdDump.OpenDataTarget(dumpPath, options);
        var choice = ProcessName(target);
        return (choice.Name, choice.Source);
    }

    /// <summary>The ClrMD library's version, for the worker's versions line.</summary>
    public static string LibraryVersion { get; } =
        System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Microsoft.Diagnostics.Runtime.ClrRuntime).Assembly)?.InformationalVersion
        ?? typeof(Microsoft.Diagnostics.Runtime.ClrRuntime).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public SnapshotKind Kind => SnapshotKind.FullDump;

    public int Version => CurrentVersion;

    /// <summary>Loads <paramref name="dumpPath"/>; the caller owns the returned graph.</summary>
    /// <exception cref="RuntimeComponentMissingException">The matching DAC is not on this machine.</exception>
    /// <exception cref="SnapshotFormatException">The file is not a readable .NET dump.</exception>
    public LoadResult Load(string dumpPath, IProgress<LoadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dumpPath);
        var fullPath = Path.GetFullPath(dumpPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"The dump {fullPath} does not exist.", fullPath);
        }

        var meter = new LoadMeter();
        progress?.Report(new LoadProgress("Opening dump", 0, null));
        // A Linux or macOS core file has no minidump header: its capture time is the file's write time (a
        // load note says so), and its truncation check reads its own headers (CoreFileHeader).
        var minidump = IsMinidump(fullPath);
        MinidumpHeader? header = minidump ? MinidumpHeader.Read(fullPath) : null;
        using var target = ClrMdDump.OpenDataTarget(fullPath, _options);
        var clr = ClrMdDump.FirstRuntime(target, fullPath);
        using var runtime = ClrMdDump.CreateRuntime(clr, fullPath, _options, out var dac);
        ClrHeap heap;
        try
        {
            heap = runtime.Heap;
        }
        catch (InvalidDataException ex)
        {
            // ClrMD cannot create the heap when the runtime's GC data is missing from the file, for example a dump cut
            // short while it was written or copied.
            throw new SnapshotFormatException("The managed heap could not be read from this dump: the file looks truncated or damaged. Capture another dump.", ex);
        }

        if (!heap.CanWalkHeap)
        {
            throw new SnapshotFormatException("The dump was taken while a garbage collection was running, so its heap cannot be walked. Capture another dump.");
        }

        var open = meter.End("open");
        cancellationToken.ThrowIfCancellationRequested();

        var keptInMemory = _options.EdgesKeptInMemory;
        string? fallback = null;
        using var builder = new HeapGraphBuilder(edgeFolder: _options.EdgeFolder, edgesKeptInMemory: reason =>
        {
            fallback = reason;
            keptInMemory?.Invoke(fullPath, reason);
        });
        var (unreadable, incompleteSegments, freeBytes, clrTypes) = AddObjects(heap, builder, (_options.FallBackAtObjectForTests, _options.OverrunAtObjectForTests), progress, cancellationToken);
        var objects = meter.End("objects");

        builder.FreezeObjects(cancellationToken);

        // Freezing copied the chunked object buffers into the final columns; about 20 bytes per object of chunks are
        // garbage now. Without an aggressive collection the GC keeps that memory committed through the reference pass,
        // which is where the load peaks (measured: about 1.2 GB on the Medium dump). A phase of its own.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        var freeze = meter.End("freeze and collect");
        var handles = HandleTables.Read(runtime, dac.Path, minidump && !_options.IgnoreProcessorsForTests ? MinidumpHeader.Processors(fullPath, cancellationToken) : null, !_options.SecondHandleWalk, progress, cancellationToken);
        var weakHandles = AddDependentHandlesAndCollectWeak(runtime, builder, cancellationToken);
        foreach (var handle in handles.LeftOut)
        {
            Add(handle, builder, weakHandles);
        }

        var handleTime = meter.End("handles");
        AddReferences(heap, builder, clrTypes, progress, cancellationToken);
        var references = handleTime + meter.End("references");

        progress?.Report(new LoadProgress("Reading roots", 0, null));
        // .NET 8 reports its static storage only as strong handles: a handle's array that holds a static field's slot is that
        // storage, so a static ClrMD cannot name (a generic type's) is "static storage", not a handle's.
        var staticSlots = StaticSlots(runtime, cancellationToken);
        AddGcRoots(heap, target.DataReader, builder, staticSlots.Slots, [.. handles.LeftOut.Where(static h => h.Kind == ClrHandleKind.Strong && h.Object != 0).Select(static h => h.Object)], cancellationToken);
        foreach (var address in weakHandles)
        {
            builder.AddRoot(address, RootKind.WeakHandle, RootFlags.None);
        }

        var staticRoots = AddStaticFieldRoots(runtime, builder, cancellationToken);
        var debuggable = FindDebuggableModules(runtime);
        var roots = meter.End("roots");

        progress?.Report(new LoadProgress("Building graph", 0, null));
        var graph = builder.Build(cancellationToken);
        try
        {
            var build = meter.End("build");

            var processName = ProcessName(target);
            var info = new SnapshotInfo(
                SnapshotKind.FullDump,
                fullPath,
                processName.Name,
                ProcessId(target),
                DacLocator.RuntimeVersion(clr.Version, Path.GetDirectoryName(clr.ModuleInfo.FileName)),
                target.DataReader.PointerSize,
                header is { } h ? h.CapturedAtUtc : new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero),
                freeBytes,
                SnapshotFacts.FreeSpace | SnapshotFacts.ReferenceArrayMarks | SnapshotFacts.FinalizerRegistrations
                    | (header is null ? SnapshotFacts.CaptureTimeFromFile : SnapshotFacts.None)
                    | (handles.Walk.Outcome == HandleWalkOutcome.Unknown ? SnapshotFacts.None : SnapshotFacts.HandlesChecked)
                    | (staticSlots.CoreInitialized > 0 ? SnapshotFacts.StaticsChecked : SnapshotFacts.None));

            // A heap that could not be read completely must not claim complete, exact answers (ARCHITECTURE.md 5.2).
            var capabilities = unreadable > 0 || incompleteSegments > 0 || (minidump ? MinidumpHeader.IsTruncated(fullPath, cancellationToken) : CoreFileHeader.IsTruncated(fullPath))
                ? SnapshotCapabilities.FullDump & ~(SnapshotCapabilities.CompleteGraph | SnapshotCapabilities.ExactCounts)
                : SnapshotCapabilities.FullDump;
            return new LoadResult(
                graph,
                info,
                capabilities,
                new LoadDiagnostics(debuggable, unreadable, staticRoots, incompleteSegments, dac, processName.Source, handles.Walk, staticSlots.Describe(), builder.EdgesKept, fallback),
                new LoadTimings(open, objects, references, roots, build, freeze, meter.Costs));
        }
        catch
        {
            // The builder no longer owns a built graph: a failure here (a cancel while the truncation check reads the
            // dump) would leak it, with its references' mapped file, until the worker exits.
            graph.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A segment walk is incomplete when the objects walked (free objects included) miss more than this share of its
    /// object range and more than <see cref="IncompleteSegmentBytes"/>. Unfilled allocation contexts, which ClrMD skips,
    /// make up small gaps (measured on the Roslyn dump: 15 KB of a 24 KB gen0 region, 1 to 3 percent elsewhere).
    /// </summary>
    internal const double IncompleteSegmentShare = 0.05;

    /// <summary>The absolute part of the incomplete-segment test.</summary>
    internal const ulong IncompleteSegmentBytes = 1024 * 1024;

    /// <summary>
    /// The objects: each object's
    /// method table is read from the dump and mapped through a table filled once per type, so ClrMD's type factory runs once
    /// per type rather than once per object (8.6 s of a 54 s Medium load), and its size is computed once from the type's base
    /// and component sizes, as ClrMD computes it. Segments are walked in address order, so objects arrive sorted and the
    /// finalizable list is merged rather than looked up. An object this walk cannot read (no type for its method table) hands
    /// the rest of its segment to ClrMD's own walk, which counts unreadable objects as before.
    /// </summary>
    private static (long Unreadable, int IncompleteSegments, long[] FreeBytes, ClrType?[] ClrTypes) AddObjects(ClrHeap heap, HeapGraphBuilder builder, (long? FallBackAt, long? OverrunAt) forTests, IProgress<LoadProgress>? progress, CancellationToken cancellationToken)
    {
        // Free objects are the gaps the GC left between live ones (fragmentation), per heap kind.
        var free = new long[Enum.GetValues<HeapKind>().Length];
        var finalizable = FinalizableObjects(heap, cancellationToken);
        var reader = heap.Runtime.DataTarget.DataReader;
        var walk = new ObjectWalk(heap, reader);
        var types = new Dictionary<ulong, WalkedType>();
        var clrTypes = new List<ClrType?>();
        var segments = heap.Segments.OrderBy(s => s.ObjectRange.Start).ToList();
        long total = segments.Sum(s => (long)s.ObjectRange.Length);
        long done = 0, unreadable = 0, count = 0, objects = 0;
        var incomplete = 0;
        var nextFinalizable = 0;

        // The type of an object, through the table filled once per method table; null when the dump gives none.
        WalkedType? TypeOf(ulong address, ulong methodTable)
        {
            if (types.TryGetValue(methodTable, out var known))
            {
                return known;
            }

            var type = heap.GetObjectType(address);
            if (type is null || type.MethodTable != methodTable)
            {
                return null;
            }

            var typeIndex = -1;
            if (!type.IsFree)
            {
                typeIndex = builder.Types.GetOrAdd(
                    type.Name ?? $"<unknown type 0x{type.MethodTable:x}>",
                    type.MethodTable,
                    (uint)type.StaticSize,
                    (uint)type.ComponentSize,
                    type.Module?.Name is { } module ? RecordedPath.FileName(module) : null,
                    KindOf(type));
                while (clrTypes.Count <= typeIndex)
                {
                    clrTypes.Add(null);
                }

                clrTypes[typeIndex] = type;
            }

            var walked = new WalkedType(type, typeIndex, (ulong)type.StaticSize, (ulong)type.ComponentSize, type.IsFree, type.IsString);
            types.Add(methodTable, walked);
            return walked;
        }

        void Add(ClrSegment segment, ulong address, WalkedType type, ulong size)
        {
            var kind = FixedHeapKind(segment.Kind) ?? GenerationKind(segment.GetGeneration(address));
            if (type.IsFree)
            {
                free[(int)kind] += (long)size;
                return;
            }

            // The finalizable objects in address order, merged with the walk: segments are walked in address order and objects
            // within each in order, ClrMD's fallback included, so one cursor serves the whole walk.
            while (nextFinalizable < finalizable.Length && finalizable[nextFinalizable] < address)
            {
                nextFinalizable++;
            }

            var registered = nextFinalizable < finalizable.Length && finalizable[nextFinalizable] == address;
            var flags = registered && !type.Clr.IsFinalizeSuppressed(address) ? ObjectFlags.HasFinalizer : ObjectFlags.None;
            builder.AddObject(address, type.TypeIndex, size, kind, flags);
        }

        foreach (var segment in segments)
        {
            ulong walked = 0;
            var end = segment.ObjectRange.End;
            var address = segment.FirstObjectAddress;
            var overran = false;
            while (address >= segment.ObjectRange.Start && address < end)
            {
                if (++count % CheckInterval == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new LoadProgress("Reading objects", done + (long)(address - segment.ObjectRange.Start), total));
                }

                if (walk.AllocationContextEnd(address) is { } skipTo)
                {
                    address = skipTo;
                    continue;
                }

                if (++objects == forTests.FallBackAt
                    || walk.MethodTable(address) is not { } methodTable || TypeOf(address, methodTable) is not { } type || walk.Size(address, type) is not { } size)
                {
                    // What this walk cannot read, ClrMD's own walk reads as it always did, from here to the segment's end.
                    var (rest, restWalked, restUnreadable, restOverran) = WalkWithClrMd(segment, address, TypeOf, Add, cancellationToken);
                    walked += restWalked;
                    unreadable += restUnreadable;
                    count += rest;
                    overran |= restOverran;
                    break;
                }

                if (objects == forTests.OverrunAt)
                {
                    size = end - address + 1;
                }

                if (size > end - address)
                {
                    // A length that runs past the segment: a damaged object, not one this big. Counted as unreadable,
                    // and the segment as incomplete, since nothing after it can be found.
                    unreadable++;
                    overran = true;
                    break;
                }

                walked += size;
                Add(segment, address, type, size);
                address = walk.Next(segment, address, size);
            }

            done += (long)segment.ObjectRange.Length;
            var missing = segment.ObjectRange.Length > walked ? segment.ObjectRange.Length - walked : 0;
            if (overran
                || (missing > IncompleteSegmentBytes && missing > segment.ObjectRange.Length * IncompleteSegmentShare)
                || !IsInDump(reader, segment.ObjectRange))
            {
                incomplete++;
            }
        }

        progress?.Report(new LoadProgress("Reading objects", total, total));
        return (unreadable, incomplete, free, [.. clrTypes]);
    }

    /// <summary>
    /// ClrMD's own walk of a segment from <paramref name="from"/> (the first loop): for what the method table walk cannot
    /// read. An object without a readable type has no size either, so it is counted as unreadable; one whose size
    /// runs past the segment ends the walk as unreadable. Objects before <paramref name="from"/>, which the method
    /// table walk added, are not added again.
    /// </summary>
    private static (long Count, ulong Walked, long Unreadable, bool Overran) WalkWithClrMd(
        ClrSegment segment,
        ulong from,
        Func<ulong, ulong, WalkedType?> typeOf,
        Action<ClrSegment, ulong, WalkedType, ulong> add,
        CancellationToken cancellationToken)
    {
        long count = 0, unreadable = 0;
        ulong walked = 0;
        foreach (var obj in segment.EnumerateObjects(new MemoryRange(from, segment.ObjectRange.End), carefully: false))
        {
            if (++count % CheckInterval == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (obj.Address < from)
            {
                continue;
            }

            if (obj.Type is not { } clrType || typeOf(obj.Address, clrType.MethodTable) is not { } type)
            {
                unreadable++;
                continue;
            }

            var size = obj.Size;
            if (size > segment.ObjectRange.End - obj.Address)
            {
                return (count, walked, unreadable + 1, true);
            }

            walked += size;
            add(segment, obj.Address, type, size);
        }

        return (count, walked, unreadable, false);
    }

    /// <summary>A type as the object walk keeps it: ClrMD's, its index in the graph (-1 for free space) and its sizes.</summary>
    private sealed record WalkedType(ClrType Clr, int TypeIndex, ulong BaseSize, ulong ComponentSize, bool IsFree, bool IsString);

    /// <summary>
    /// Reading objects from the dump by their method tables: the header word, the component count of arrays and
    /// strings, the allocation contexts to skip and the alignment, as the runtime lays objects out and ClrMD reads them.
    /// </summary>
    private sealed class ObjectWalk
    {
        private readonly IDataReader _reader;
        private readonly int _pointerSize;
        private readonly ulong _minObjectSize;
        private readonly Dictionary<ulong, ulong> _allocationContexts;

        public ObjectWalk(ClrHeap heap, IDataReader reader)
        {
            _reader = reader;
            _pointerSize = reader.PointerSize;
            _minObjectSize = (ulong)_pointerSize * 3;

            // An allocation context is a thread's unused allocation buffer: no objects in it, and a free object's room after it.
            _allocationContexts = new Dictionary<ulong, ulong>();
            foreach (var context in heap.EnumerateAllocationContexts())
            {
                if (context.Start != 0 && context.End > context.Start)
                {
                    _allocationContexts.TryAdd(context.Start, context.End + Align(_minObjectSize, (ulong)_pointerSize));
                }
            }
        }

        public ulong? AllocationContextEnd(ulong address) => _allocationContexts.TryGetValue(address, out var end) ? end : null;

        /// <summary>The object's method table (its header word without the GC's mark bits), or null when it cannot be read.</summary>
        public ulong? MethodTable(ulong address)
        {
            var methodTable = _reader.ReadPointer(address) & ~3UL;
            return methodTable == 0 ? null : methodTable;
        }

        /// <summary>The object's size: its base size plus, for arrays and strings, its component count times the component size.</summary>
        public ulong? Size(ulong address, WalkedType type)
        {
            if (type.ComponentSize == 0)
            {
                return type.BaseSize;
            }

            if (!_reader.Read(address + (ulong)_pointerSize, out uint count))
            {
                return null;
            }

            // A string's size counts its terminating character, and no object is smaller than the minimum: as ClrMD sizes them.
            var components = type.IsString ? (ulong)count + 1 : count;
            return Math.Max(type.BaseSize + (components * type.ComponentSize), _minObjectSize);
        }

        /// <summary>The next object's address: past this one, aligned as its segment aligns objects, at least a minimum object on.</summary>
        public ulong Next(ClrSegment segment, ulong address, ulong size)
        {
            var alignment = segment.Kind is GCSegmentKind.Large or GCSegmentKind.Pinned ? 8UL : (ulong)_pointerSize;
            return address + Math.Max(Align(size, alignment), _minObjectSize);
        }

        private static ulong Align(ulong size, ulong alignment) => (size + alignment - 1) & ~(alignment - 1);
    }

    /// <summary>The page granularity of the memory probe below.</summary>
    internal const ulong ProbeStride = 64 * 1024;

    /// <summary>
    /// True when the dump holds the memory of <paramref name="range"/>, probed one byte every
    /// <see cref="ProbeStride"/> and at its last byte. A truncated dump loses whole memory ranges: the walk
    /// can then stop early by less than the incomplete-segment margin, or find no objects at all, and still look complete.
    /// </summary>
    private static bool IsInDump(IDataReader reader, MemoryRange range)
    {
        if (range.Length == 0)
        {
            return true;
        }

        Span<byte> probe = stackalloc byte[1];
        for (var address = range.Start; address < range.End; address += ProbeStride)
        {
            if (reader.Read(address, probe) != 1)
            {
                return false;
            }
        }

        return reader.Read(range.End - 1, probe) == 1;
    }

    /// <summary>The objects registered for finalization, ascending (merged with the object walk).</summary>
    private static ulong[] FinalizableObjects(ClrHeap heap, CancellationToken cancellationToken)
    {
        var result = new List<ulong>();
        foreach (var obj in heap.EnumerateFinalizableObjects())
        {
            if (result.Count % CheckInterval == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            result.Add(obj.Address);
        }

        result.Sort();
        return [.. result.Distinct()];
    }

    /// <summary>Adds a handle the runtime's own walk left out, as the walks below add theirs.</summary>
    private static void Add(LeftOutHandle handle, HeapGraphBuilder builder, List<ulong> weak)
    {
        if (handle.Object == 0)
        {
            return;
        }

        switch (handle.Kind)
        {
            case ClrHandleKind.Dependent:
                builder.AddDependentHandle(handle.Object, handle.Dependent);
                break;
            case ClrHandleKind.WeakShort or ClrHandleKind.WeakLong or ClrHandleKind.WeakWinRT:
                weak.Add(handle.Object);
                break;
            case ClrHandleKind.Pinned:
                builder.AddRoot(handle.Object, RootKind.PinnedHandle, RootFlags.Retaining | RootFlags.Pinned);
                break;
            case ClrHandleKind.AsyncPinned:
                builder.AddRoot(handle.Object, RootKind.AsyncPinnedHandle, RootFlags.Retaining | RootFlags.Pinned);
                break;
            case ClrHandleKind.RefCounted:
                if (handle.IsStrong)
                {
                    builder.AddRoot(handle.Object, RootKind.RefCountedHandle, RootFlags.Retaining);
                }

                break;
            case ClrHandleKind.SizedRef:
                builder.AddRoot(handle.Object, RootKind.Other, RootFlags.Retaining, nameof(ClrRootKind.SizedRefHandle));
                break;
            case ClrHandleKind.Strong:
                // With the others, where the static storage among them is told apart.
                break;
        }
    }

    private static List<ulong> AddDependentHandlesAndCollectWeak(ClrRuntime runtime, HeapGraphBuilder builder, CancellationToken cancellationToken)
    {
        var weak = new List<ulong>();
        var count = 0;
        foreach (var handle in runtime.EnumerateHandles())
        {
            if (++count % CheckInterval == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            switch (handle.HandleKind)
            {
                case ClrHandleKind.Dependent:
                    if (!handle.Object.IsNull)
                    {
                        builder.AddDependentHandle(handle.Object.Address, handle.Dependent.Address);
                    }

                    break;
                case ClrHandleKind.WeakShort or ClrHandleKind.WeakLong or ClrHandleKind.WeakWinRT:
                    if (!handle.Object.IsNull)
                    {
                        weak.Add(handle.Object.Address);
                    }

                    break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return weak;
    }

    /// <summary>
    /// Each object's references, read with its type known from the object pass, and none read for a type that
    /// holds no references (strings, arrays of numbers). An object of a collectible type also references its
    /// type's LoaderAllocator, as the GC marks it: ClrMD yields no such reference, so the loader adds it.
    /// </summary>
    private static void AddReferences(ClrHeap heap, HeapGraphBuilder builder, ClrType?[] clrTypes, IProgress<LoadProgress>? progress, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<ulong>.Shared.Rent(1024);
        var reader = heap.Runtime.DataTarget.DataReader;
        var allocators = new Dictionary<int, ulong>();

        // The LoaderAllocator object of a collectible type, read once per type through its handle; 0 when the dump has none.
        ulong LoaderAllocatorOf(ClrType type, int typeIndex)
        {
            if (!allocators.TryGetValue(typeIndex, out var allocator))
            {
                allocator = type.LoaderAllocatorHandle is not 0 and var handle ? reader.ReadPointer(handle) : 0;
                allocators.Add(typeIndex, allocator);
            }

            return allocator;
        }

        try
        {
            var count = builder.ObjectCount;
            var addresses = builder.FrozenAddresses;
            var typeIndices = builder.FrozenTypeIndices;
            for (var i = 0; i < count; i++)
            {
                if (i % CheckInterval == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new LoadProgress("Reading references", i, count));
                }

                var type = (uint)typeIndices[i] < (uint)clrTypes.Length ? clrTypes[typeIndices[i]] : null;
                if (type is { ContainsPointers: false, IsCollectible: false })
                {
                    continue;
                }

                var used = 0;
                if (type is not { ContainsPointers: false })
                {
                    var targets = type is null
                        ? heap.GetObject(addresses[i]).EnumerateReferenceAddresses(carefully: false, considerDependantHandles: false)
                        : heap.GetObject(addresses[i], type).EnumerateReferenceAddresses(carefully: false, considerDependantHandles: false);
                    foreach (var target in targets)
                    {
                        Append(target);
                    }
                }

                // The LoaderAllocator that keeps an unloadable context's types and statics alive.
                if (type is { IsCollectible: true } && LoaderAllocatorOf(type, typeIndices[i]) is not 0 and var allocator
                    && !buffer.AsSpan(0, used).Contains(allocator))
                {
                    Append(allocator);
                }

                void Append(ulong target)
                {
                    if (used == buffer.Length)
                    {
                        var larger = ArrayPool<ulong>.Shared.Rent(buffer.Length * 2);
                        buffer.AsSpan().CopyTo(larger);
                        ArrayPool<ulong>.Shared.Return(buffer);
                        buffer = larger;
                    }

                    buffer[used++] = target;
                }

                if (used > 0)
                {
                    builder.AddReferences(i, buffer.AsSpan(0, used));
                }
            }

            progress?.Report(new LoadProgress("Reading references", count, count));
        }
        finally
        {
            ArrayPool<ulong>.Shared.Return(buffer);
        }
    }

    private static void AddGcRoots(ClrHeap heap, IDataReader reader, HeapGraphBuilder builder, ulong[] staticSlots, IReadOnlyList<ulong> leftOut, CancellationToken cancellationToken)
    {
        // The static storage bucket is reported both as a strong handle and (on .NET 9 and 10) as a StaticVar root;
        // it is kept once.
        var strongHandles = new List<(ulong Address, RootFlags Flags)>(leftOut.Select(static a => (a, RootFlags.Retaining)));
        var staticStorage = new HashSet<ulong>();
        var count = 0;
        foreach (var root in heap.EnumerateRoots())
        {
            if (++count % CheckInterval == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (root.Object.IsNull)
            {
                continue;
            }

            var address = root.Object.Address;
            var flags = RootFlags.Retaining;
            if (root.IsPinned)
            {
                flags |= RootFlags.Pinned;
            }

            if (root.IsInterior)
            {
                // The raw pointer in the stack slot, resolved by the builder's floor search with its bounds check, so a
                // byref into a free gap or outside the heap is dropped instead of rooting the object before it.
                flags |= RootFlags.Interior;
                if (root is ClrStackRoot && root.Address != 0 && reader.ReadPointer(root.Address, out var slot) && slot != 0)
                {
                    address = slot;
                }
            }

            switch (root.RootKind)
            {
                case ClrRootKind.StaticVar:
                    staticStorage.Add(address);
                    break;
                case ClrRootKind.StrongHandle:
                    strongHandles.Add((address, flags));
                    break;
                case ClrRootKind.PinnedHandle:
                    builder.AddRoot(address, RootKind.PinnedHandle, flags | RootFlags.Pinned);
                    break;
                case ClrRootKind.AsyncPinnedHandle:
                    builder.AddRoot(address, RootKind.AsyncPinnedHandle, flags | RootFlags.Pinned);
                    break;
                case ClrRootKind.RefCountedHandle:
                    builder.AddRoot(address, RootKind.RefCountedHandle, flags);
                    break;
                case ClrRootKind.FinalizerQueue:
                    builder.AddRoot(address, RootKind.FinalizerQueue, flags);
                    break;
                case ClrRootKind.Stack:
                    builder.AddRoot(address, RootKind.ThreadStack, flags, ThreadDetail(root));
                    break;
                case ClrRootKind.ThreadStaticVar:
                    builder.AddRoot(address, RootKind.Other, flags, "thread static" + ThreadSuffix(root));
                    break;
                default:
                    builder.AddRoot(address, RootKind.Other, flags, root.RootKind.ToString());
                    break;
            }
        }

        foreach (var (address, flags) in strongHandles)
        {
            var storage = staticStorage.Remove(address) || HoldsStaticSlot(heap, address, staticSlots);
            builder.AddRoot(address, RootKind.StrongHandle, flags, storage ? StaticStorage : null);
        }

        foreach (var address in staticStorage)
        {
            builder.AddRoot(address, RootKind.StrongHandle, RootFlags.Retaining, StaticStorage);
        }
    }

    private static string? ThreadDetail(ClrRoot root) =>
        root is ClrStackRoot { StackFrame.Thread: { } thread }
            ? string.Create(CultureInfo.InvariantCulture, $"thread {thread.ManagedThreadId} (OS 0x{thread.OSThreadId:x})")
            : null;

    private static string ThreadSuffix(ClrRoot root) => ThreadDetail(root) is { } detail ? ", " + detail : string.Empty;

    /// <summary>
    /// The box of a struct static whose data lies at <paramref name="data"/>: the object one method table before it, or the one the slot
    /// points to, of the field's own type only; none otherwise.
    /// </summary>
    private static ClrObject BoxOf(ClrHeap heap, ulong data, ClrType type)
    {
        if (data == 0)
        {
            return default;
        }

        var before = heap.GetObject(data - (ulong)heap.Runtime.DataTarget.DataReader.PointerSize);
        if (before.IsValid && before.Type?.MethodTable == type.MethodTable)
        {
            return before;
        }

        var pointed = heap.Runtime.DataTarget.DataReader.ReadPointer(data) is var address and not 0 ? heap.GetObject(address) : default;
        return pointed.IsValid && pointed.Type?.MethodTable == type.MethodTable ? pointed : default;
    }

    // The addresses of the slots of every static that holds a reference (sorted), a thread static's on each live thread: where the
    // runtime keeps them. And whether the statics could be read at all: every process has System.Private.CoreLib's reference statics
    // initialized by the time it can be dumped, so none of them means their memory is not in the dump (a heap dump of .NET 8 written
    // on Windows leaves it out: 0 of 191 there, 106 in a full dump of the same process). CoreLib is the runtime's own module, not a file
    // name, which a single-file app may not give.
    private static StaticSlotsFound StaticSlots(ClrRuntime runtime, CancellationToken cancellationToken)
    {
        var slots = new List<ulong>();
        var (coreFields, coreInitialized) = (0, 0);
        var coreLibrary = runtime.BaseClassLibrary?.Address ?? 0;
        var threads = runtime.Threads.Where(static t => t.IsAlive).ToArray();
        var seen = new HashSet<(ulong Module, ulong Domain)>();
        foreach (var domain in runtime.AppDomains)
        {
            foreach (var module in domain.Modules)
            {
                if (!seen.Add((module.Address, domain.Address)))
                {
                    continue;
                }

                var core = coreLibrary != 0 && module.Address == coreLibrary;
                foreach (var type in module.EnumerateTypesWithStaticFields())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var field in type.ThreadStaticFields)
                    {
                        if (!field.IsObjectReference && field.Type is not { ContainsPointers: true })
                        {
                            continue;
                        }

                        foreach (var thread in threads)
                        {
                            if (field.GetAddress(thread) is var threadSlot and not 0)
                            {
                                slots.Add(threadSlot);
                            }
                        }
                    }

                    foreach (var field in type.StaticFields)
                    {
                        if (!field.IsObjectReference)
                        {
                            continue;
                        }

                        coreFields += core ? 1 : 0;
                        var initialized = field.IsInitialized(domain);
                        coreInitialized += core && initialized ? 1 : 0;

                        // Every slot, initialized or not: an array that holds only a generic type's or a not yet set static's is storage too.
                        if (field.GetAddress(domain) is var slot and not 0)
                        {
                            slots.Add(slot);
                        }
                    }
                }
            }
        }

        slots.Sort();
        return new StaticSlotsFound([.. slots], coreFields, coreInitialized, coreLibrary != 0);
    }

    // What the statics' check found, for the load's log line: the verdict rests on these counts.
    private sealed record StaticSlotsFound(ulong[] Slots, int CoreFields, int CoreInitialized, bool CoreFound)
    {
        public string Describe() => CoreFound
            ? string.Create(CultureInfo.InvariantCulture, $"System.Private.CoreLib's reference statics {CoreInitialized} of {CoreFields} initialized, {Slots.Length} slots")
            : string.Create(CultureInfo.InvariantCulture, $"System.Private.CoreLib not found, {Slots.Length} slots");
    }

    // Whether the array at address (an object[]) holds one of the slots: the runtime's static storage of .NET 8.
    private static bool HoldsStaticSlot(ClrHeap heap, ulong address, ulong[] slots)
    {
        if (slots.Length == 0 || heap.GetObject(address) is not { IsValid: true, IsArray: true } array || array.Type?.ComponentType is not { IsObjectReference: true })
        {
            return false;
        }

        var end = address + array.Size;
        var index = Array.BinarySearch(slots, address);
        index = index >= 0 ? index : ~index;
        return index < slots.Length && slots[index] < end;
    }

    private static int AddStaticFieldRoots(ClrRuntime runtime, HeapGraphBuilder builder, CancellationToken cancellationToken)
    {
        var added = 0;
        var threads = runtime.Threads.Where(static t => t.IsAlive).ToArray();
        var checks = 0;
        var seen = new HashSet<(ulong Module, ulong Domain)>();
        foreach (var domain in runtime.AppDomains)
        {
            foreach (var module in domain.Modules)
            {
                if (!seen.Add((module.Address, domain.Address)))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                foreach (var type in module.EnumerateTypesWithStaticFields())
                {
                    if (++checks % 1024 == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    // Statics of collectible types live as long as their loader allocator, not forever: the root is
                    // shown, but does not retain (ARCHITECTURE.md 6.1).
                    var flags = type.IsCollectible ? RootFlags.None : RootFlags.Retaining;
                    foreach (var field in type.StaticFields)
                    {
                        if (!field.IsInitialized(domain))
                        {
                            continue;
                        }

                        // A struct with references is boxed in the runtime's static storage: its box is the root.
                        var value = field.IsObjectReference ? field.ReadObject(domain)
                            : field.IsValueType && field.Type is { ContainsPointers: true } structType ? BoxOf(runtime.Heap, field.GetAddress(domain), structType)
                            : default;
                        if (value.IsNull || !value.IsValid)
                        {
                            continue;
                        }

                        var before = builder.DroppedRootCount;
                        builder.AddRoot(value.Address, RootKind.StaticField, flags, $"{type.Name}.{field.Name}");
                        if (builder.DroppedRootCount == before)
                        {
                            added++;
                        }
                    }

                    // A thread static is named by its field too, once per live thread that set it: .NET 8, and
                    // .NET 10 on Linux, report no thread static root, and .NET 9 and 10 on Windows only the runtime's arrays. A struct
                    // with references is boxed per thread, as a struct static is; a dead thread's statics are freed, never read.
                    foreach (var field in type.ThreadStaticFields)
                    {
                        if (!field.IsObjectReference && !(field.IsValueType && field.Type is { ContainsPointers: true }))
                        {
                            continue;
                        }

                        foreach (var thread in threads)
                        {
                            var value = field.IsObjectReference ? field.ReadObject(thread) : BoxOf(runtime.Heap, field.GetAddress(thread), field.Type!);
                            if (value.IsNull || !value.IsValid)
                            {
                                continue;
                            }

                            var before = builder.DroppedRootCount;
                            builder.AddRoot(value.Address, RootKind.StaticField, flags, $"{type.Name}.{field.Name} (thread static)");
                            if (builder.DroppedRootCount == before)
                            {
                                added++;
                            }
                        }
                    }
                }
            }
        }

        return added;
    }

    private static List<string> FindDebuggableModules(ClrRuntime runtime)
    {
        var result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in runtime.EnumerateModules())
        {
            if ((module.DebuggingMode & DebuggableAttribute.DebuggingModes.DisableOptimizations) != 0 && !module.IsDynamic)
            {
                result.Add(RecordedPath.FileName(module.Name ?? module.AssemblyName ?? "?"));
            }
        }

        return [.. result];
    }

    private static HeapKind? FixedHeapKind(GCSegmentKind kind) => kind switch
    {
        GCSegmentKind.Generation0 => HeapKind.Gen0,
        GCSegmentKind.Generation1 => HeapKind.Gen1,
        GCSegmentKind.Generation2 => HeapKind.Gen2,
        GCSegmentKind.Large => HeapKind.LargeObjectHeap,
        GCSegmentKind.Pinned => HeapKind.PinnedObjectHeap,
        GCSegmentKind.Frozen => HeapKind.Frozen,
        _ => null,
    };

    private static HeapKind GenerationKind(Generation generation) => generation switch
    {
        Generation.Generation0 => HeapKind.Gen0,
        Generation.Generation1 => HeapKind.Gen1,
        Generation.Generation2 => HeapKind.Gen2,
        Generation.Large => HeapKind.LargeObjectHeap,
        Generation.Pinned => HeapKind.PinnedObjectHeap,
        Generation.Frozen => HeapKind.Frozen,
        _ => HeapKind.Unknown,
    };

    private static TypeKind KindOf(ClrType type)
    {
        var kind = TypeKind.None;
        if (type.IsArray)
        {
            kind |= TypeKind.Array;
            if (type.ComponentType?.IsObjectReference == true)
            {
                kind |= TypeKind.ReferenceArray;
            }
        }

        if (type.IsString)
        {
            kind |= TypeKind.String;
        }

        if (type.IsValueType)
        {
            kind |= TypeKind.ValueTypeBox;
        }

        if (type.IsException)
        {
            kind |= TypeKind.Exception;
        }

        if (type.BaseType?.Name is "System.MulticastDelegate" or "System.Delegate")
        {
            kind |= TypeKind.Delegate;
        }

        return kind;
    }

    /// <summary>Whether the file is a Windows minidump rather than a Linux or macOS core file.</summary>
    private static bool IsMinidump(string path)
    {
        Span<byte> start = stackalloc byte[4];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return stream.ReadAtLeast(start, 4, throwOnEndOfStream: false) == 4 && SnapshotFileSignature.IsMinidump(start);
    }

    private static ProcessNameChoice ProcessName(DataTarget target)
    {
        var (imagePath, whyNoPath) = ImagePath(target.DataReader);
        return ProcessNameOf(
            target.EnumerateModules().Select(static module => module.FileName ?? string.Empty),
            imagePath,
            target.DataReader.TargetPlatform == OSPlatform.Windows,
            whyNoPath);
    }

    /// <summary>The executable's path as the dump records it, or why there is none. Not every reader has it, and a name must not fail a load.</summary>
    private static (string? Path, string WhyNot) ImagePath(IDataReader reader)
    {
        if (reader is not IProcessInfoProvider provider)
        {
            return (null, "this kind of dump records no path");
        }

        try
        {
            return (provider.GetProcessInfo().ImagePath, "the dump records no path");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or IndexOutOfRangeException or OverflowException or ClrDiagnosticsException)
        {
            return (null, "reading the recorded path failed: " + ex.GetType().Name);
        }
    }

    /// <summary>A dump's process name and which rule chose it, for the log.</summary>
    internal readonly record struct ProcessNameChoice(string? Name, string Source);

    /// <summary>
    /// The process's name. The executable's path the dump records settles it (Windows dumps and Linux core files record one).
    /// Without it the modules are all there is, and the reader sorts them by base address, not load order, so the first may be
    /// any library. A Windows dump's executable is its .exe (the first, if the process mapped another). A core file's modules
    /// have no such mark: an executable usually sits below its libraries, so the name is the first module not named like a
    /// library, with its dots kept (Orders.Api). That is a judgment, not proof: under `ulimit -s unlimited` a mapped data file
    /// could sort first, and the log states the rule used. With no such module the name is unknown, not a library's.
    /// </summary>
    internal static ProcessNameChoice ProcessNameOf(IEnumerable<string> modules, string? imagePath = null, bool windows = true, string whyNoPath = "the dump records no path")
    {
        if (!string.IsNullOrEmpty(imagePath) && RecordedPath.FileName(imagePath) is { Length: > 0 } image)
        {
            return new ProcessNameChoice(
                windows && image.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? image[..^".exe".Length] : image,
                "the executable's path the dump records: " + imagePath);
        }

        var named = 0;
        var executables = 0;
        string? executable = null;
        string? candidate = null;
        foreach (var module in modules)
        {
            var name = RecordedPath.FileName(module);

            // A core file marks a file replaced or removed since it was mapped.
            if (name.EndsWith(" (deleted)", StringComparison.Ordinal))
            {
                name = name[..^" (deleted)".Length];
            }

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            named++;
            var isExe = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            if (isExe && windows)
            {
                executables++;
                executable ??= name;
            }
            else if (candidate is null && !isExe && !LooksLikeLibrary(name))
            {
                candidate = name;
            }
        }

        var counted = named.ToString(CultureInfo.InvariantCulture);
        return executable is not null
            ? new ProcessNameChoice(
                executable[..^".exe".Length],
                executables == 1
                    ? $"the only .exe of {counted} modules; {whyNoPath}"
                    : $"the first of {executables.ToString(CultureInfo.InvariantCulture)} .exe modules of {counted}; {whyNoPath}")
            : candidate is not null
                ? new ProcessNameChoice(candidate, windows
                    ? $"the first of {counted} modules not named like a library (no .exe); {whyNoPath}"
                    : $"the first of {counted} modules not named like a library (.exe modules are not considered on this system); {whyNoPath}")
                : new ProcessNameChoice(null, windows
                    ? $"unknown: no .exe or executable among {counted} modules; {whyNoPath}"
                    : $"unknown: no executable among {counted} modules; {whyNoPath}");
    }

    private static bool LooksLikeLibrary(string name) =>
        name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".so", StringComparison.Ordinal)
        || name.Contains(".so.", StringComparison.Ordinal)
        || name.EndsWith(".dylib", StringComparison.Ordinal);

    private static int? ProcessId(DataTarget target) =>
        target.DataReader.ProcessId is var id and > 0 ? id : null;
}
