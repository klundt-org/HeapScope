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

namespace HeapScope.Capture;

/// <summary>
/// What a target's environment says about its diagnostics socket, on Linux and macOS: whether it is turned off, and the
/// temp folder it is in. The only value that leaves here is the temp folder; no other variable is kept or logged.
/// </summary>
internal sealed class TargetEnvironment
{
    /// <summary>The variables that turn a process's diagnostics port off (DOTNET_ and the older COMPlus_ spellings).</summary>
    private static readonly string[] s_diagnosticsOff =
        ["DOTNET_EnableDiagnostics", "COMPlus_EnableDiagnostics", "DOTNET_EnableDiagnostics_IPC", "COMPlus_EnableDiagnostics_IPC"];

    /// <summary>Reads <paramref name="entries"/> (<c>NAME=value</c>); the first of a name counts.</summary>
    public TargetEnvironment(IEnumerable<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var equals = entry.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                variables.TryAdd(entry[..equals], entry[(equals + 1)..]);
            }
        }

        DiagnosticsOff = Array.Find(s_diagnosticsOff, name => variables.TryGetValue(name, out var value) && value.Trim() == "0");
        TempFolderSet = variables.TryGetValue("TMPDIR", out var tmp) && tmp.Length > 0;
        TempFolder = Folder(TempFolderSet ? tmp! : "/tmp");
    }

    /// <summary>The variable that turns the diagnostics port off, or null.</summary>
    public string? DiagnosticsOff { get; }

    /// <summary>The folder its socket is in: its <c>TMPDIR</c>, or <c>/tmp</c>.</summary>
    public string TempFolder { get; }

    /// <summary>Whether <see cref="TempFolder"/> is its <c>TMPDIR</c>, not the default.</summary>
    public bool TempFolderSet { get; }

    /// <summary><paramref name="path"/> without its trailing slashes.</summary>
    public static string Folder(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }
}
