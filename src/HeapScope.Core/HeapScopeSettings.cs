// HeapScope
// Copyright (C) 2026 Ray Klundt
// https://github.com/rklundt/HeapScope
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
/// command line read it. A missing or unreadable file means the defaults, and a member a newer build wrote is kept on save.
/// </summary>
/// <param name="IncludeFilterTextInLogs">
/// Whether the text typed in filters and searches is written into the logs, to troubleshoot a result (on by default: the logs
/// stay on this machine; owner, 2026-11-12). Off, the logs keep a filter's shape only: its length and its terms.
/// </param>
public sealed record HeapScopeSettings(bool IncludeFilterTextInLogs = true)
{
    /// <summary>The file's name in the data folder's root.</summary>
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    /// <summary>Members a newer build wrote, kept as they are when this build saves.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>The settings in <paramref name="folders"/>, or the defaults when there are none or they cannot be read.</summary>
    public static HeapScopeSettings Read(HeapScopeFolders folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        try
        {
            using var stream = new FileStream(PathIn(folders), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<HeapScopeSettings>(stream, s_json) ?? new HeapScopeSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new HeapScopeSettings();
        }
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
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = PrivateFiles.Open(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, this, s_json);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
