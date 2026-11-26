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
/// The user's settings, shared by every HeapScope process on this machine (sprint 3.12): <c>settings.json</c> in the data
/// folder's root (<see cref="HeapScopeFolders.Root"/>). The app writes it from its Settings page; the app, the worker and the
/// command line read it. A missing file means the defaults, and a member a newer build wrote is kept on save; a file that cannot
/// be read is the defaults for <see cref="Read"/>, and <see cref="TryRead"/> says so.
/// </summary>
/// <param name="IncludeFilterTextInLogs">
/// Whether the text typed in filters and searches is written into the logs, to troubleshoot a result (on by default: the logs
/// stay on this machine; owner, 2026-11-12). Off, the logs keep a filter's shape only: its length and its terms.
/// </param>
/// <param name="IncludeDumpValuesInLogs">
/// Whether values read from a dump (field values, string text) are written into the logs (sprint 3.13.2; owner, 2026-11-19):
/// off by default, turned on only after the user confirms. Off, the logs keep a value's length only.
/// </param>
public sealed record HeapScopeSettings(bool IncludeFilterTextInLogs = true, bool IncludeDumpValuesInLogs = false)
{
    /// <summary>The file's name in the data folder's root.</summary>
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    /// <summary>Members a newer build wrote, kept as they are when this build saves.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>The settings in <paramref name="folders"/>, or the defaults when there are none or they cannot be read.</summary>
    public static HeapScopeSettings Read(HeapScopeFolders folders) => TryRead(folders, out var settings) ? settings : new HeapScopeSettings();

    /// <summary>
    /// Reads the settings in <paramref name="folders"/>: true with them, or with the defaults when there is no file; false when
    /// a file is there and cannot be read now (in use, damaged, not this user's), with the defaults in <paramref name="settings"/>.
    /// A caller that must not fail open on a setting (the logs' privacy, sprint 3.12 review H5) tells the two apart.
    /// </summary>
    public static bool TryRead(HeapScopeFolders folders, out HeapScopeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(folders);
        // Through the one helper (review of 4.6): a file held for a moment is read again.
        var (read, outcome, _) = PrivateJsonFile.Read<HeapScopeSettings>(PathIn(folders), s_json);
        settings = read ?? new HeapScopeSettings();
        return outcome is JsonFileRead.Read or JsonFileRead.Missing;
    }

    /// <summary>The file's full path under <paramref name="folders"/>.</summary>
    public static string PathIn(HeapScopeFolders folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        return Path.Combine(folders.Root, FileName);
    }

    /// <summary>Writes the settings to <paramref name="folders"/>, replacing the file in one step, readable by this user only.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    public void Save(HeapScopeFolders folders)
    {
        var path = PathIn(folders);
        PrivateFiles.CreateDirectory(folders.Root);
        PrivateJsonFile.Write(path, this, s_json);
    }
}
