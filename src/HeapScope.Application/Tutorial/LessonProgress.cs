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
using HeapScope.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeapScope.Application.Tutorial;

/// <summary>
/// The lessons a user finished (sprint 3.13): their ids in <c>learn-progress.json</c> in the data folder, written owner-only and
/// whole, under a lock (<see cref="PrivateJsonFile"/>). A damaged file counts as no progress, is logged and kept beside as
/// <c>.bak</c> before it is replaced (review M7); one that cannot be read now (held by another process) is never written over, so
/// no progress is lost (peer review 4.5 M6). Nothing in it comes from a dump.
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
    /// <exception cref="IOException">The file could not be read now, or not written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be written.</exception>
    public void MarkDone(string lessonId)
    {
        using var fileLock = PrivateJsonFile.Lock(FilePath);
        var done = new HashSet<string>(Read(out var outcome), StringComparer.Ordinal) { lessonId };
        if (outcome == JsonFileRead.Unreadable)
        {
            throw new IOException($"The lesson progress {FilePath} cannot be read now, so it is not written over. Close other HeapScope windows and try again.");
        }

        if (outcome == JsonFileRead.Damaged)
        {
            File.Copy(FilePath, FilePath + ".bak", overwrite: true);
            InvokingUser.Give(FilePath + ".bak");
            _logger.LogWarning("The damaged lesson progress {Path} was kept as {Backup}", FilePath, FilePath + ".bak");
        }

        PrivateJsonFile.Write(FilePath, done.Order(StringComparer.Ordinal));
        _logger.LogInformation("Lesson {Lesson} done; {Count} lessons done in {Path}", lessonId, done.Count, FilePath);
    }

    private HashSet<string> Read(out JsonFileRead outcome)
    {
        var (value, read, error) = PrivateJsonFile.Read<HashSet<string>>(FilePath);
        outcome = read;
        if (error is not null)
        {
            _logger.LogWarning("The lesson progress {Path} could not be read ({Outcome}), so no lesson counts as done: {Error}", FilePath, read, error.Message);
        }

        return value ?? [];
    }
}
