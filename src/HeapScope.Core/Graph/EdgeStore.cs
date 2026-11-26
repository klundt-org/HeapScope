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
/// The references of a graph being built, in row order (sprint 4.7, task 10). Kept in chunks in memory, they were copied into
/// one array at the end while the chunks were still held, so the load peaked at twice their size (1.3 GB more on a 106M object
/// dump). Given a folder, they are written to a temporary file as they are read instead, and the file is mapped as the edges
/// column: its pages belong to the file, not to the process, and nothing is copied. The file is deleted when the graph is
/// disposed, or by the system if the process dies (Windows), or is never named at all once open (elsewhere). When the file
/// cannot be created or written (a full disk), what was written is read back and the rest is kept in memory, as before.
/// </summary>
internal sealed class EdgeStore : IDisposable
{
    /// <summary>References written to the file at a time (4 MB).</summary>
    internal const int WriteChunk = 1 << 20;

    private readonly int[] _pending;
    private int _pendingCount;
    private ChunkedBuffer<int>? _memory;
    private FileStream? _file;
    private long _fileBytes;
    private readonly long? _failWritesAfterBytes;

    /// <param name="folder">Where the temporary file goes; null keeps the references in memory.</param>
    /// <param name="failWritesAfterBytes">Tests: a write past this many bytes fails as on a full disk.</param>
    public EdgeStore(string? folder, long? failWritesAfterBytes = null)
    {
        _failWritesAfterBytes = failWritesAfterBytes;
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
            _file = Open(folder);
            Where = "written to a temporary file";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _memory = new ChunkedBuffer<int>();
            Where = $"in memory: the temporary file in {folder} could not be created ({ex.GetType().Name}: {ex.Message})";
        }
    }

    /// <summary>References added so far.</summary>
    public long Count { get; private set; }

    /// <summary>Where the references are kept and, if the file failed, why not there (for the load's log).</summary>
    public string Where { get; private set; }

    /// <summary>Whether the references are in a temporary file.</summary>
    public bool OnDisk => _file is not null;

    public void Add(int value)
    {
        if (_memory is not null)
        {
            _memory.Add(value);
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
    /// Ends the store: the references as one column, and what owns its memory (the mapping) or null. The store holds
    /// nothing afterwards; the owner deletes the file when disposed.
    /// </summary>
    public Memory<int> Finish(IColumnAllocator allocator, out IDisposable? owner)
    {
        owner = null;
        var count = checked((int)Count);
        if (_file is not null)
        {
            WritePending();
        }

        if (_file is not null)
        {
            var file = _file;
            _file = null;
            if (count == 0)
            {
                file.Dispose();
                return Memory<int>.Empty;
            }

            var mapping = new EdgeMapping(file, count);
            owner = mapping;
            Where = string.Create(CultureInfo.InvariantCulture, $"written to a temporary file and mapped ({_fileBytes / (1024.0 * 1024.0):N0} MB)");
            return mapping.Memory;
        }

        var column = allocator.Allocate<int>("Edges", count);
        _memory!.CopyTo(column.Span);
        _memory.Clear();
        _memory = null;
        return column;
    }

    public void Dispose()
    {
        _file?.Dispose();
        _file = null;
        _memory?.Clear();
        _memory = null;
    }

    private static FileStream Open(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"edges-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp"));
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = 0,
            Options = OperatingSystem.IsWindows() ? FileOptions.DeleteOnClose : FileOptions.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        var stream = new FileStream(path, options);
        if (!OperatingSystem.IsWindows())
        {
            // Open files outlive their name here: unnamed now, the file goes when the process does, however it ends.
            try
            {
                File.Delete(path);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        return stream;
    }

    private void WritePending()
    {
        if (_pendingCount == 0)
        {
            return;
        }

        var bytes = MemoryMarshal.AsBytes(_pending.AsSpan(0, _pendingCount));
        try
        {
            if (_fileBytes + bytes.Length > _failWritesAfterBytes)
            {
                throw new IOException("There is not enough space on the disk (simulated).");
            }

            _file!.Write(bytes);
            _fileBytes += bytes.Length;
            _pendingCount = 0;
        }
        catch (IOException ex)
        {
            FallBackToMemory(ex);
        }
    }

    /// <summary>Reads back what the file holds, then keeps it and the pending references in memory.</summary>
    private void FallBackToMemory(IOException cause)
    {
        var file = _file!;
        _file = null;
        var memory = new ChunkedBuffer<int>();
        try
        {
            file.Position = 0;
            var buffer = GC.AllocateUninitializedArray<int>(WriteChunk);
            var remaining = _fileBytes;
            while (remaining > 0)
            {
                var want = (int)Math.Min(remaining, (long)buffer.Length * sizeof(int));
                var span = MemoryMarshal.AsBytes(buffer.AsSpan()).Slice(0, want);
                file.ReadExactly(span);
                foreach (var value in buffer.AsSpan(0, want / sizeof(int)))
                {
                    memory.Add(value);
                }

                remaining -= want;
            }
        }
        finally
        {
            file.Dispose();
        }

        foreach (var value in _pending.AsSpan(0, _pendingCount))
        {
            memory.Add(value);
        }

        _pendingCount = 0;
        _memory = memory;
        Where = string.Create(CultureInfo.InvariantCulture, $"in memory: writing the temporary file failed after {_fileBytes / (1024.0 * 1024.0):N0} MB ({cause.GetType().Name}: {cause.Message})");
    }

    /// <summary>A read-only mapping of the temporary file, which owns the file: disposing it deletes the file (Windows).</summary>
    private sealed unsafe class EdgeMapping : MemoryManager<int>
    {
        private readonly FileStream _stream;
        private readonly MemoryMappedFile _mapped;
        private readonly MemoryMappedViewAccessor _view;
        private readonly int* _pointer;
        private readonly int _length;
        private int _disposed;

        public EdgeMapping(FileStream stream, int length)
        {
            _stream = stream;
            try
            {
                _mapped = MemoryMappedFile.CreateFromFile(stream, mapName: null, capacity: 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: true);
                _view = _mapped.CreateViewAccessor(0, (long)length * sizeof(int), MemoryMappedFileAccess.Read);
                byte* pointer = null;
                _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
                _pointer = (int*)(pointer + _view.PointerOffset);
                _length = length;
            }
            catch
            {
                _view?.Dispose();
                _mapped?.Dispose();
                stream.Dispose();
                throw;
            }
        }

        public override Span<int> GetSpan() => new(_pointer, _length);

        public override MemoryHandle Pin(int elementIndex = 0) => new(_pointer + elementIndex);

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
