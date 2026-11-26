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


using System.Text.Json;

namespace HeapScope.Core;

/// <summary>
/// HeapScope's own small JSON files, one way (peer review 4.5 M6): a lock file around a read and its write, reads that share the
/// file and try again while it is held, writes to a new file flushed to the disk that then replaces the old one (tried again while a
/// reader holds it), and a file that cannot be read now is never taken as empty.
/// </summary>
public static class PrivateJsonFile
{
    /// <summary>How long a lock or a held file is waited for.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    /// <summary>The lock of <paramref name="path"/> for a read and its write, across processes; disposed to let go.</summary>
    /// <exception cref="IOException">Another process held it for longer than <see cref="Wait"/>.</exception>
    public static FileStream Lock(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        PrivateFiles.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var deadline = DateTime.UtcNow + Wait;
        while (true)
        {
            try
            {
                // The lock file stays: deleting it on close would let two processes lock two files of one name on Linux and macOS.
                return PrivateFiles.Open(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
            catch (IOException ex)
            {
                throw new IOException($"{Path.GetFileName(path)} is being changed by another HeapScope window or process; try again in a moment.", ex);
            }
        }
    }

    /// <summary>Reads <paramref name="path"/>, trying again while another process holds it.</summary>
    public static (T? Value, JsonFileRead Outcome, Exception? Error) Read<T>(string path, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var deadline = DateTime.UtcNow + Wait;
        while (true)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return (JsonSerializer.Deserialize<T>(stream, options), JsonFileRead.Read, null);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return (default, JsonFileRead.Missing, null);
            }
            catch (JsonException ex)
            {
                return (default, JsonFileRead.Damaged, ex);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (default, JsonFileRead.Unreadable, ex);
            }
        }
    }

    /// <summary>Writes <paramref name="value"/> as the whole of <paramref name="path"/>: the old file or the new one is there, never a part.</summary>
    /// <exception cref="IOException">It could not be written, or not put in place while another process held the old file.</exception>
    /// <exception cref="UnauthorizedAccessException">It could not be written.</exception>
    public static void Write<T>(string path, T value, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        PrivateFiles.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = PrivateFiles.Open(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, options);

                // On the disk before it replaces the file: a power loss must not leave an empty file in its place.
                stream.Flush(flushToDisk: true);
            }

            var deadline = DateTime.UtcNow + Wait;
            while (true)
            {
                try
                {
                    File.Move(temporary, path, overwrite: true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && DateTime.UtcNow < deadline)
                {
                    // A reader in another process holds it for a moment (Windows refuses the replace then).
                    Thread.Sleep(25);
                }
            }
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left behind: a .tmp file nothing reads.
            }
        }
    }
}
