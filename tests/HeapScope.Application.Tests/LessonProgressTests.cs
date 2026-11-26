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


using HeapScope.Application.Tutorial;

namespace HeapScope.Application.Tests;

/// <summary>Lesson progress keeps every lesson done (peer review 4.5 M6): a file held by another process is never taken as empty.</summary>
public sealed class LessonProgressTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("heapscope-progress-").FullName;

    private string FilePath => Path.Combine(_folder, "learn-progress.json");

    [Fact]
    public void A_file_another_process_holds_is_not_written_over()
    {
        var progress = new LessonProgress(_folder);
        progress.MarkDone("first");
        var before = File.ReadAllText(FilePath);

        using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Should.Throw<IOException>(() => progress.MarkDone("second")).Message.ShouldContain("is not written over");
        }

        File.ReadAllText(FilePath).ShouldBe(before);
        progress.MarkDone("second");
        progress.Done.ShouldBe(["first", "second"], ignoreOrder: true);
    }

    [Fact]
    public async Task Two_writers_at_once_keep_both_lessons()
    {
        var one = new LessonProgress(_folder);
        var two = new LessonProgress(_folder);

        await Task.WhenAll(
            Task.Run(() => Enumerable.Range(0, 20).ToList().ForEach(i => one.MarkDone($"a{i}")), TestContext.Current.CancellationToken),
            Task.Run(() => Enumerable.Range(0, 20).ToList().ForEach(i => two.MarkDone($"b{i}")), TestContext.Current.CancellationToken));

        new LessonProgress(_folder).Done.Count.ShouldBe(40);
        Directory.EnumerateFiles(_folder, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public void A_damaged_file_is_kept_aside_and_started_again()
    {
        File.WriteAllText(FilePath, "[ \"first\", ");
        var progress = new LessonProgress(_folder);

        progress.Done.ShouldBeEmpty();
        progress.MarkDone("second");

        progress.Done.ShouldBe(["second"]);
        File.ReadAllText(FilePath + ".bak").ShouldBe("[ \"first\", ");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
