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
/// A process's facts on Linux, read from <c>/proc/&lt;pid&gt;</c> and parsed by <see cref="ProcText"/>. Another
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
        return RuntimeLibrary.WithFileVersion(Read(() => ProcText.Runtime(File.ReadAllText(ProcFile(process.Id, "maps")))), ProcText.RuntimeLibraryFile);
    }

    public long? PrivateBytes(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            return process.PrivateMemorySize64;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    public long? ReadableBytes(int processId)
    {
        try
        {
            return ProcText.ReadableBytes(File.ReadAllText(ProcFile(processId, "maps")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // Exited, or another user's.
        }
    }

    /// <summary>The user the process acts as (its effective user id), read from <c>status</c>, which anyone may read.</summary>
    public int? EffectiveUserId(int processId)
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
