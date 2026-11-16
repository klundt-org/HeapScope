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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeapScope.Application.Tutorial;

/// <summary>
/// The lessons a user finished (sprint 3.13): their ids in <c>learn-progress.json</c> in the data folder, written owner-only and
/// whole (to a new file, then moved over the old one). A file that cannot be read counts as no progress, is logged and kept
/// beside as <c>.bak</c> before it is replaced, so nothing is lost silently (review M7); nothing in it comes from a dump.
/// </summary>
public sealed class LessonProgress
{
    private readonly string _root;
    private readonly ILogger _logger;

    /// <param name="root">The data folder; null for HeapScope's (<c>HEAPSCOPE_DATA_FOLDER</c> or the default).</param>
    public LessonProgress(string? root = null, ILogger<LessonProgress>? logger = null)
    {
        _root = root ?? HeapScopeFolders.Default.Root;
        _logger = logger ?? NullLogger<LessonProgress>.Instance;
    }

    private string FilePath => Path.Combine(_root, "learn-progress.json");

    /// <summary>The ids of the lessons finished.</summary>
    public IReadOnlySet<string> Done => Read(out _);

    /// <summary>Records <paramref name="lessonId"/> as finished.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be written.</exception>
    public void MarkDone(string lessonId)
    {
        var done = new HashSet<string>(Read(out var damaged), StringComparer.Ordinal) { lessonId };
        PrivateFiles.CreateDirectory(_root);
        if (damaged)
        {
            File.Copy(FilePath, FilePath + ".bak", overwrite: true);
            _logger.LogWarning("The damaged lesson progress {Path} was kept as {Backup}", FilePath, FilePath + ".bak");
        }

        var temporary = FilePath + ".tmp";
        using (var stream = PrivateFiles.Open(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, done.Order(StringComparer.Ordinal));
        }

        File.Move(temporary, FilePath, overwrite: true);
        _logger.LogInformation("Lesson {Lesson} done; {Count} lessons done in {Path}", lessonId, done.Count, FilePath);
    }

    private HashSet<string> Read(out bool damaged)
    {
        damaged = false;
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(FilePath)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            damaged = ex is JsonException;
            _logger.LogWarning("The lesson progress {Path} could not be read, so no lesson counts as done: {Error}", FilePath, ex.Message);
            return [];
        }
    }
}
