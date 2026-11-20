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

using System.Diagnostics;
using System.Globalization;

namespace HeapScope.Capture;

/// <summary>
/// A process's facts on Linux, read from <c>/proc/&lt;pid&gt;</c> (sprint 4.1) and parsed by <see cref="ProcText"/>. Another
/// user's <c>exe</c> and <c>maps</c> are readable only by that user or root, so those reads return null for it.
/// </summary>
internal sealed class LinuxProcessFacts : IProcessFacts
{
    public static LinuxProcessFacts Instance { get; } = new();

    public string? ImagePath(int processId) => Read(() => ProcText.ExecutablePath(new FileInfo(ProcFile(processId, "exe")).LinkTarget));

    public string? CommandLine(int processId) => Read(() => ProcText.CommandLine(File.ReadAllText(ProcFile(processId, "cmdline"))));

    public RuntimeFacts? Runtime(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var runtime = Read(() => ProcText.Runtime(File.ReadAllText(ProcFile(process.Id, "maps"))));

        // The library's own file version, as on Windows and as ClrMD reads it from a dump; the folder's name otherwise.
        return runtime?.Directory is { } folder && FileVersion(folder + "/libcoreclr.so") is { } version ? runtime with { Version = version } : runtime;
    }

    /// <summary>A runtime library's embedded file version, read once per file and write time (a list reads every process's).</summary>
    internal static string? FileVersion(string library)
    {
        // Review H5: the path comes from the target's maps; as root HeapScope reads only a regular file of a sane size.
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

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Path, DateTime Written), string?> s_versions = new();

    /// <summary>The user the process acts as (its effective user id), read from <c>status</c>, which anyone may read.</summary>
    public static int? EffectiveUserId(int processId)
    {
        try
        {
            return ProcText.EffectiveUserId(File.ReadAllText(ProcFile(processId, "status")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ProcFile(int processId, string name) => string.Create(CultureInfo.InvariantCulture, $"/proc/{processId}/{name}");

    private static T? Read<T>(Func<T?> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // Exited, or another user's.
        }
    }
}
