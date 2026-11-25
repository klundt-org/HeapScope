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

using HeapScope.Core;
using Microsoft.Win32.SafeHandles;

namespace HeapScope.Loaders.Trace;

/// <summary>
/// A trace file that still grows, as TraceEvent reads it (sprint 4.3, ADR-037): only whole objects that <see cref="TraceFileCheck"/>
/// has walked are handed out, waiting at the end of them for more, until the recording ends. What the file ends inside then, or a
/// tag that is not the format's, is replaced by the format's end tag: what came before is read.
/// </summary>
internal sealed class LiveTraceStream : Stream
{
    /// <summary>How long a reader at the end of the file waits before it looks again (the delay ADR-037 measured).</summary>
    private static readonly TimeSpan s_poll = TimeSpan.FromMilliseconds(20);

    private const byte EndOfTrace = 1;

    private readonly string _path;
    private readonly FileStream _file;
    private readonly SafeFileHandle _handle;
    private readonly TraceFileCheck.Walk _walk;
    private readonly Func<bool> _ended;
    private readonly CancellationToken _stop;
    private long _position;
    private long _checked;
    private bool _finished;
    private bool _endSent;

    public LiveTraceStream(string path, Func<bool> ended, CancellationToken stop)
    {
        _path = path;
        _ended = ended;
        _stop = stop;

        // Shared for everything: the reader never stands in the way of the recording or of a deletion.
        _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
        _handle = _file.SafeFileHandle;
        _walk = new TraceFileCheck.Walk(path);
    }

    /// <summary>What the check walked, once it stopped.</summary>
    public TraceCheckResult? Checked { get; private set; }

    /// <summary>Why the file was refused, if it was: the stream ended there.</summary>
    public SnapshotFormatException? Refusal { get; private set; }

    /// <summary>What the refusal rests on, for the log.</summary>
    public string? RefusalDetail { get; private set; }

    /// <summary>Whether the reader has been handed everything the file held when it last looked.</summary>
    public bool CaughtUp { get; private set; }

    /// <summary>Whether it ever waited at the end of what the file held.</summary>
    public bool HasCaughtUp { get; private set; }

    /// <summary>Bytes handed out.</summary>
    public long BytesRead => Interlocked.Read(ref _position);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        while (buffer.Length > 0)
        {
            if (_position < _checked)
            {
                CaughtUp = false;
                var n = RandomAccess.Read(_handle, buffer[..(int)Math.Min(buffer.Length, _checked - _position)], _position);
                if (n > 0)
                {
                    Interlocked.Add(ref _position, n);
                    return n;
                }

                // The file is shorter than it was: nothing to wait for.
                Shrunk(_file.Length);
                _finished = true;
                _position = _checked;
            }

            if (_finished || _stop.IsCancellationRequested)
            {
                if (_endSent || _position == 0)
                {
                    return 0;
                }

                _endSent = true;
                buffer[0] = EndOfTrace;
                return 1;
            }

            Advance();
        }

        return 0;
    }

    /// <summary>Walks what is new in the file; waits when there is nothing.</summary>
    private void Advance()
    {
        // Asked before the file is looked at: what it holds then is all there will be.
        var ended = _ended();
        try
        {
            var length = _file.Length;
            if (length < _checked)
            {
                Shrunk(length);
                _finished = true;
                return;
            }

            if (length >= TraceFileCheck.Walk.HeadBytes)
            {
                // At most a few megabytes before they are handed out: catching up with a long file shows as it goes.
                var until = _checked + (4 * 1024 * 1024);
                TraceCheckResult? stopped = null;
                while (stopped is null && _walk.Position < until)
                {
                    stopped = _walk.Next(_file, _stop);
                }

                var more = _walk.Position > _checked;
                _checked = _walk.Position;
                if (stopped is not null && stopped.End != TraceCheckResult.CutShort)
                {
                    Finish(stopped);
                    return;
                }

                if (more)
                {
                    return;
                }

                if (ended)
                {
                    Finish(stopped);
                    return;
                }
            }
            else if (ended)
            {
                Finish(null);
                return;
            }
        }
        catch (SnapshotFormatException ex)
        {
            // Everything before the object that was refused is sound and is read.
            _checked = Math.Max(_checked, _walk.Position);
            Refusal = ex;
            RefusalDetail = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"check {TraceFileCheck.Version} refused what starts at byte {_walk.Position} of {_file.Length} (its rule and byte are in the reason)");
            _finished = true;
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        (CaughtUp, HasCaughtUp) = (true, true);
        _stop.WaitHandle.WaitOne(s_poll);
    }

    private void Finish(TraceCheckResult? result)
    {
        Checked = result;
        CaughtUp = true;
        _finished = true;
    }

    private void Shrunk(long length)
    {
        Refusal ??= new SnapshotFormatException($"{_path} became shorter while it was read. Open the finished trace again.");
        RefusalDetail = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{length} bytes, {_checked} were checked and {_position} read");
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _file.Dispose();
        }

        base.Dispose(disposing);
    }
}
