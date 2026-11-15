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
using HeapScope.Core;

namespace HeapScope.Application.Tutorial;

/// <summary>
/// The lessons a user finished (sprint 3.13): their ids in <c>learn-progress.json</c> in the data folder, written owner-only.
/// A file that cannot be read counts as no progress; nothing in it comes from a dump.
/// </summary>
public sealed class LessonProgress
{
    private readonly HeapScopeFolders _folders;

    /// <summary>The progress in HeapScope's data folder.</summary>
    public LessonProgress()
        : this(null)
    {
    }

    /// <summary>The progress in <paramref name="folders"/> (null: HeapScope's data folder).</summary>
    public LessonProgress(HeapScopeFolders? folders) => _folders = folders ?? HeapScopeFolders.Default;

    private string FilePath => Path.Combine(_folders.Root, "learn-progress.json");

    /// <summary>The ids of the lessons finished.</summary>
    public IReadOnlySet<string> Done
    {
        get
        {
            try
            {
                return File.Exists(FilePath) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(FilePath)) ?? [] : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return new HashSet<string>();
            }
        }
    }

    /// <summary>Records <paramref name="lessonId"/> as finished.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    public void MarkDone(string lessonId)
    {
        var done = new HashSet<string>(Done, StringComparer.Ordinal) { lessonId };
        PrivateFiles.CreateDirectory(_folders.Root);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(done.Order(StringComparer.Ordinal)));
    }
}
