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

using System.Globalization;
using System.Text;

namespace HeapScope.Capture;

/// <summary>
/// Parsers of the text Linux publishes under <c>/proc/&lt;pid&gt;</c> (sprint 4.1). They take the text, not the files, so they
/// run and are tested on every system; <see cref="LinuxProcessFacts"/> reads the files. Paths are Linux paths, so they are
/// split on '/' here, never with <see cref="Path"/>, which on Windows also splits on '\'.
/// </summary>
internal static class ProcText
{
    private const string Deleted = " (deleted)";

    private const string RuntimeLibrary = "/libcoreclr.so";

    private const string SharedFramework = "/shared/Microsoft.NETCore.App";

    /// <summary>
    /// The command line from <c>cmdline</c>: its arguments end in NUL; they are joined with spaces, and one that is empty or
    /// holds a space or a tab is quoted, as Windows shows a command line. Null when there is none (a kernel thread, or a
    /// process that has exited and not yet been reaped).
    /// </summary>
    public static string? CommandLine(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var arguments = raw.Split('\0');
        var count = arguments.Length > 0 && arguments[^1].Length == 0 ? arguments.Length - 1 : arguments.Length;
        if (count == 0)
        {
            return null;
        }

        var line = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                line.Append(' ');
            }

            var argument = arguments[i];
            if (argument.Length == 0 || argument.Contains(' ', StringComparison.Ordinal) || argument.Contains('\t', StringComparison.Ordinal))
            {
                line.Append('"').Append(argument.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
            }
            else
            {
                line.Append(argument);
            }
        }

        return line.ToString();
    }

    /// <summary>
    /// The executable from <c>exe</c>'s link target. A file deleted since the process started (an update replaced it)
    /// ends in " (deleted)", which is cut: the path is still the one the process ran.
    /// </summary>
    public static string? ExecutablePath(string? target) =>
        string.IsNullOrEmpty(target) ? null
        : target.EndsWith(Deleted, StringComparison.Ordinal) ? target[..^Deleted.Length]
        : target;

    /// <summary>
    /// The runtime from <c>maps</c>: the folder of the mapped <c>libcoreclr.so</c>, and its version from the shared
    /// framework's folder name (<c>.../shared/Microsoft.NETCore.App/10.0.1/libcoreclr.so</c>). A self-contained app carries
    /// the runtime in its own folder, which names no version. Null when no <c>libcoreclr.so</c> is mapped: a single-file
    /// app links the runtime into its executable.
    /// </summary>
    public static RuntimeFacts? Runtime(string maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        foreach (var line in maps.Split('\n'))
        {
            if (MappedPath(line) is not { } path)
            {
                continue;
            }

            path = ExecutablePath(path)!;
            if (!path.EndsWith(RuntimeLibrary, StringComparison.Ordinal))
            {
                continue;
            }

            var folder = path[..^RuntimeLibrary.Length];
            var cut = folder.LastIndexOf('/');
            var version = cut > 0 && folder[..cut].EndsWith(SharedFramework, StringComparison.Ordinal) ? folder[(cut + 1)..] : null;
            return new RuntimeFacts(string.IsNullOrEmpty(version) ? null : version, folder.Length == 0 ? "/" : folder);
        }

        return null;
    }

    /// <summary>
    /// The effective user id from <c>status</c> (its <c>Uid:</c> line holds the real, effective, saved and file system
    /// ids): the user the process acts as, whose diagnostics socket it creates. Null when the line is missing.
    /// </summary>
    public static int? EffectiveUserId(string status)
    {
        ArgumentNullException.ThrowIfNull(status);
        foreach (var line in status.Split('\n'))
        {
            if (!line.StartsWith("Uid:", StringComparison.Ordinal))
            {
                continue;
            }

            var ids = line[4..].Split((char[])['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            return ids.Length > 1 && int.TryParse(ids[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
        }

        return null;
    }

    /// <summary>
    /// The bytes of every readable mapping in <c>maps</c> (permissions starting with <c>r</c>), the pages a full dump
    /// written by the runtime's createdump holds: private memory, loaded libraries and mapped files. Reserved but
    /// uncommitted address space is mapped with no access (<c>---p</c>) and is left out. Errs high: a mapped file's pages
    /// count whole, read or not.
    /// </summary>
    public static long ReadableBytes(string maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        long total = 0;
        foreach (var line in maps.Split('\n'))
        {
            var dash = line.IndexOf('-', StringComparison.Ordinal);
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            if (dash <= 0 || space <= dash || space + 1 >= line.Length || line[space + 1] != 'r')
            {
                continue;
            }

            if (ulong.TryParse(line.AsSpan(0, dash), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var start)
                && ulong.TryParse(line.AsSpan(dash + 1, space - dash - 1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var end)
                && end > start)
            {
                total += (long)(end - start);
            }
        }

        return total;
    }

    /// <summary>
    /// The path of one <c>maps</c> line (<c>address perms offset dev inode path</c>), or null for an anonymous mapping. The
    /// path is everything after the fifth field, so a path holding spaces stays whole.
    /// </summary>
    internal static string? MappedPath(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        line = line.TrimEnd('\r');
        var i = 0;
        for (var field = 0; field < 5; field++)
        {
            while (i < line.Length && line[i] == ' ')
            {
                i++;
            }

            while (i < line.Length && line[i] != ' ')
            {
                i++;
            }
        }

        while (i < line.Length && line[i] == ' ')
        {
            i++;
        }

        return i < line.Length && line[i] == '/' ? line[i..] : null;
    }
}
