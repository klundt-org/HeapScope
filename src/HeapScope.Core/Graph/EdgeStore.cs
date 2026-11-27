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
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace HeapScope.Core.Graph;

/// <summary>
/// The references of a graph being built, in row order. Given a folder, they are written to a private temporary file there
/// as they are added and the file is mapped read-only as the edges column, so they are never held twice in memory and their
/// pages are the file's, not the process's (graph-format.md 6.3). The mapping belongs to the graph and the file goes with it:
/// opened delete on close on Windows, so the system deletes it if the process dies; elsewhere unnamed as soon as it is open.
/// Without a folder, or when the folder lacks the space, the file cannot be created, written or mapped, the references are
/// kept in memory; what the file held by then is read straight into the final column, so a fallback holds them once, plus
/// the chunks added after it. Each fallback is reported through the callback when it happens and kept in <see cref="Where"/>.
/// </summary>
internal sealed class EdgeStore : IDisposable
{
    /// <summary>References written to the file at a time (4 MB).</summary>
    internal const int WriteChunk = 1 << 20;

    /// <summary>References per object the free space check counts on (DenseHeap has 3.0, the fixture heaps 1.1 to 1.2).</summary>
    internal const int ReferencesPerObjectForSpace = 4;

    /// <summary>Free space left over beyond the file's estimate, for the index cache and the system.</summary>
    internal const long SpareBytes = 1L << 30;

    /// <summary>References read back from the file at a time (64 MB): a span of more than 2 GiB cannot be read as bytes at once.</summary>
    internal const int ReadSlice = 1 << 24;

    private readonly int[] _pending;
    private readonly Action<string>? _fellBack;
    private readonly EdgeStoreHooks? _hooks;
    private int _pendingCount;

    // In memory: every reference (no file), or those added after the file failed (the file holds the first _fileBytes).
    private ChunkedBuffer<int>? _memory;
    private FileStream? _file;
    private bool _writing;
    private long _fileBytes;
    private readonly string _path = string.Empty;

    /// <param name="folder">Where the temporary file goes; null keeps the references in memory.</param>
    /// <param name="objectCount">The graph's objects, for the free space check.</param>
    /// <param name="fellBack">Told why, when the references go to memory instead of the folder asked.</param>
    /// <param name="hooks">Tests: failures as on a full disk or a refused mapping.</param>
    public EdgeStore(string? folder, int objectCount, Action<string>? fellBack = null, EdgeStoreHooks? hooks = null)
    {
        _fellBack = fellBack;
        _hooks = hooks;
        if (folder is null)
        {
            _memory = new ChunkedBuffer<int>();
            _pending = [];
            Where = "in memory";
            return;
        }

        _pending = GC.AllocateUninitializedArray<int>(WriteChunk);
        try
        {
            // The folder first: off Windows the free space is read from the folder itself, which must exist.
            PrivateFiles.CreatePrivateDirectory(folder);
            var needed = ((long)objectCount * ReferencesPerObjectForSpace * sizeof(int)) + SpareBytes;
            if ((hooks?.FreeBytes ?? FreeBytes(folder)) is { } free && free < needed)
            {
                KeepInMemory(
                    string.Create(CultureInfo.InvariantCulture, $"in memory: {Mb(free)} free in {folder}, and the temporary file needs up to {Mb(needed - SpareBytes)} beside {Mb(SpareBytes)} kept free"),
                    FreeSpace);
                return;
            }

            _file = Open(folder, out _path);
            _writing = true;
            Where = "written to a temporary file";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            KeepInMemory(
                $"in memory: the temporary file in {folder} could not be created ({ex.GetType().Name}: {ex.Message})",
                "Check that the folder is the user's own and can be written; the next load tries it again");
        }
    }

    /// <summary>References added so far.</summary>
    public long Count { get; private set; }

    /// <summary>Where the references are kept and, when not in the folder asked, why (for the load's log).</summary>
    public string Where { get; private set; } = string.Empty;

    /// <summary>Whether the references still go to the temporary file.</summary>
    public bool Writing => _writing;

    public void Add(int value)
    {
        if (!_writing)
        {
            _memory!.Add(value);
        }
        else
        {
            _pending[_pendingCount++] = value;
            if (_pendingCount == _pending.Length)
            {
                WritePending();
            }
        }

        Count++;
    }

    /// <summary>
    /// Ends the store: the references as one column, and what owns its memory (the mapping, which deletes the file when
    /// disposed) or null. The store holds nothing afterwards.
    /// </summary>
    public Memory<int> Finish(IColumnAllocator allocator, out IDisposable? owner)
    {
        owner = null;
        var count = checked((int)Count);

        // The last write can fail too, which moves the rest to memory.
        WritePending();
        if (_writing)
        {
            if (count == 0)
            {
                CloseFile();
                Where = "none (no references)";
                return Memory<int>.Empty;
            }

            try
            {
                if (_hooks?.FailMapping == true)
                {
                    throw new IOException("Not enough memory resources are available to process this command (simulated).");
                }

                var mapping = new EdgeMapping(_file!, count);
                _file = null;
                _writing = false;
                owner = mapping;
                Where = string.Create(CultureInfo.InvariantCulture, $"written to a temporary file and mapped ({Mb(_fileBytes)}, {count:N0} references, {Named()})");
                return mapping.Memory;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _writing = false;
                _memory = new ChunkedBuffer<int>();
                Report(
                    string.Create(CultureInfo.InvariantCulture, $"in memory: the temporary file of {Mb(_fileBytes)} in {Folder()} could not be mapped ({ex.GetType().Name}: {ex.Message})"),
                    "Nothing to do unless it happens again; then report it with this log");
            }
        }

        // In memory: what the file holds (after a fallback) first, read straight into the column, then the chunks.
        var column = allocator.Allocate<int>("Edges", count);
        var prefix = (int)(_fileBytes / sizeof(int));
        if (_file is not null)
        {
            try
            {
                ReadBack(_file, column.Span[..prefix], _hooks?.ReadSlice ?? ReadSlice);
            }
            catch (IOException ex)
            {
                throw new IOException(string.Create(CultureInfo.InvariantCulture, $"The references written to the temporary file in {Folder()} could not be read back ({ex.Message}). They were being read back because the references were kept {Where}. Free space on that drive and open the dump again."), ex);
            }
            finally
            {
                CloseFile();
            }
        }

        _memory!.CopyTo(column.Span[prefix..]);
        _memory.Clear();
        _memory = null;
        return column;
    }

    public void Dispose()
    {
        CloseFile();
        _memory?.Clear();
        _memory = null;
    }

    /// <summary>Reads <paramref name="destination"/> from the file's start, <paramref name="slice"/> references at a time.</summary>
    internal static void ReadBack(FileStream file, Span<int> destination, int slice)
    {
        file.Position = 0;
        for (var offset = 0; offset < destination.Length; offset += slice)
        {
            file.ReadExactly(MemoryMarshal.AsBytes(destination.Slice(offset, Math.Min(slice, destination.Length - offset))));
        }
    }

    private static long? FreeBytes(string folder)
    {
        try
        {
            // DriveInfo takes the drive of a path on Windows and the file system of any path elsewhere (statfs).
            return new DriveInfo(Path.GetFullPath(folder)).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string Mb(long bytes) => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):N1} MB");

    private static FileStream Open(string folder, out string path)
    {
        path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"edges-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp"));
        var stream = PrivateFiles.Open(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, bufferSize: 0, OperatingSystem.IsWindows() ? FileOptions.DeleteOnClose : FileOptions.None);
        if (!OperatingSystem.IsWindows())
        {
            // An open file outlives its name here: unnamed now, the file goes when the process does, however it ends.
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stream.Dispose();
                throw new IOException($"The temporary file {path} was created but could not be unnamed, so it is left there ({ex.Message}).", ex);
            }
        }

        return stream;
    }

    private string Folder() => Path.GetDirectoryName(_path) ?? _path;

    private string Named() => OperatingSystem.IsWindows() ? _path : "unnamed in " + Folder();

    private const string FreeSpace = "Free space on that drive for the next load";

    private void KeepInMemory(string reason, string nextStep)
    {
        _memory = new ChunkedBuffer<int>();
        Report(reason, nextStep);
    }

    /// <summary>Keeps the reason for the load's line and tells the callback at once, with what to do next (by its cause).</summary>
    private void Report(string reason, string nextStep)
    {
        Where = reason;
        _fellBack?.Invoke(reason + ". " + nextStep);
    }

    private void CloseFile()
    {
        _file?.Dispose();
        _file = null;
    }

    private void WritePending()
    {
        if (!_writing || _pendingCount == 0)
        {
            return;
        }

        var bytes = MemoryMarshal.AsBytes(_pending.AsSpan(0, _pendingCount));
        try
        {
            if (_hooks?.FailWritesAfterBytes is { } limit && _fileBytes + bytes.Length > limit)
            {
                throw new IOException("There is not enough space on the disk (simulated).");
            }

            _file!.Write(bytes);
            _fileBytes += bytes.Length;
        }
        catch (IOException ex)
        {
            // The file keeps what it holds (read back into the column at the end); this chunk and the rest go to memory.
            _writing = false;
            _memory = new ChunkedBuffer<int>();
            foreach (var value in _pending.AsSpan(0, _pendingCount))
            {
                _memory.Add(value);
            }

            Report(string.Create(CultureInfo.InvariantCulture, $"in memory from reference {_fileBytes / sizeof(int):N0}: writing the temporary file in {Folder()} failed after {Mb(_fileBytes)} ({ex.GetType().Name}: {ex.Message})"), FreeSpace);
        }

        _pendingCount = 0;
    }

    /// <summary>
    /// A read-only mapping of the temporary file, which then owns the file: disposing it unmaps the view and closes the file,
    /// which deletes it (Windows). Spans taken from it must not outlive the graph's last lease, as for a mapped cache.
    /// </summary>
    private sealed unsafe class EdgeMapping : MemoryManager<int>, IFileBackedMemory
    {
        private readonly FileStream _stream;
        private readonly MemoryMappedFile _mapped;
        private readonly MemoryMappedViewAccessor _view;
        private readonly int* _pointer;
        private readonly int _length;
        private int _disposed;

        /// <summary>Maps <paramref name="stream"/>; on failure the stream is left to the caller, open.</summary>
        public EdgeMapping(FileStream stream, int length)
        {
            _mapped = MemoryMappedFile.CreateFromFile(stream, mapName: null, capacity: 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: true);
            try
            {
                _view = _mapped.CreateViewAccessor(0, (long)length * sizeof(int), MemoryMappedFileAccess.Read);
                byte* pointer = null;
                _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
                _pointer = (int*)(pointer + _view.PointerOffset);
                _length = length;
                _stream = stream;
            }
            catch
            {
                _view?.Dispose();
                _mapped.Dispose();
                throw;
            }
        }

        public override Span<int> GetSpan()
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            return new(_pointer, _length);
        }

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            return new(_pointer + elementIndex);
        }

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _view.Dispose();
            _mapped.Dispose();
            _stream.Dispose();
        }
    }
}

/// <summary>Tests: an edge store's file failing as on a full disk (writes past a size), a refused mapping, the free space seen, the read back's slice.</summary>
internal sealed record EdgeStoreHooks(long? FailWritesAfterBytes = null, bool FailMapping = false, long? FreeBytes = null, int? ReadSlice = null);
