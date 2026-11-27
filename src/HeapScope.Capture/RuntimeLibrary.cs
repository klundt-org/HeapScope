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

namespace HeapScope.Capture;

/// <summary>A runtime library (<c>libcoreclr</c>) on Linux and macOS, or an executable that links it in (a single-file app).</summary>
internal static class RuntimeLibrary
{
    private const string SharedFramework = "/shared/Microsoft.NETCore.App";

    private static readonly ConcurrentDictionary<(string Path, DateTime Written), string?> s_versions = new();

    /// <summary>A runtime library's version string, also in a single-file app's executable, which links the runtime in.</summary>
    private static readonly byte[] s_marker = "@(#)Version "u8.ToArray();

    /// <summary>
    /// The runtime from the path of a mapped file, when it is <paramref name="library"/> (<c>/libcoreclr.so</c>): its folder,
    /// and its version from the shared framework's folder name (<c>.../shared/Microsoft.NETCore.App/10.0.1/</c>), a fallback
    /// for <see cref="FileVersion"/>. A self-contained app's folder names no version. Null when it is another file.
    /// </summary>
    public static RuntimeFacts? FromPath(string path, string library)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(library);
        if (!path.EndsWith(library, StringComparison.Ordinal))
        {
            return null;
        }

        var folder = path[..^library.Length];
        var cut = folder.LastIndexOf('/');
        var version = cut > 0 && folder[..cut].EndsWith(SharedFramework, StringComparison.Ordinal) ? folder[(cut + 1)..] : null;
        return new RuntimeFacts(string.IsNullOrEmpty(version) ? null : version, folder.Length == 0 ? "/" : folder);
    }

    /// <summary>
    /// <paramref name="runtime"/> with the library's own file version, as on Windows and as ClrMD reads it from a dump; the
    /// folder's name stays when the library has none.
    /// </summary>
    public static RuntimeFacts? WithFileVersion(RuntimeFacts? runtime, string library) =>
        runtime?.Directory is { } folder && FileVersion(folder + library) is { } version ? runtime with { Version = version } : runtime;

    /// <summary>A runtime library's embedded file version, read once per file and write time (a list reads every process's).</summary>
    public static string? FileVersion(string library)
    {
        // The path comes from the target's mappings; as root HeapScope reads only a regular file of a sane size.
        if (!TargetFiles.IsReadable(library))
        {
            return null;
        }

        try
        {
            var key = (library, File.GetLastWriteTimeUtc(library));
            return s_versions.GetOrAdd(key, static k => ProcText.EmbeddedVersion(File.ReadAllBytes(k.Path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Whether <paramref name="executable"/> holds the runtime's version string, read in pieces (it can be large).</summary>
    public static bool? IsCarriedBy(string executable)
    {
        try
        {
            using var stream = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, FileOptions.SequentialScan);
            return Contains(stream, s_marker);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Whether <paramref name="stream"/> holds <paramref name="marker"/>, searched in 64 KB pieces that overlap by its length.</summary>
    internal static bool Contains(Stream stream, ReadOnlySpan<byte> marker)
    {
        var buffer = new byte[(64 * 1024) + marker.Length];
        var kept = 0;
        int read;
        while ((read = stream.Read(buffer, kept, buffer.Length - kept)) > 0)
        {
            var filled = kept + read;
            if (buffer.AsSpan(0, filled).IndexOf(marker) >= 0)
            {
                return true;
            }

            kept = Math.Min(marker.Length - 1, filled);
            buffer.AsSpan(filled - kept, kept).CopyTo(buffer);
        }

        return false;
    }
}
