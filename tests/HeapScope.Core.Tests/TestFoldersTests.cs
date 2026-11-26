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

using HeapScope.Tests.Shared;

namespace HeapScope.Core.Tests;

/// <summary>The tests' temp folders are deleted, or said and swept by a later run (sprint 4.7).</summary>
public sealed class TestFoldersTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("hs-sweep-").FullName;

    [Fact]
    public void A_stale_leftover_is_swept_and_a_fresh_folder_or_one_named_by_hand_is_kept()
    {
        var old = DateTime.UtcNow - TimeSpan.FromDays(3);
        var staleDumps = Folder(Path.Combine("HeapScopeTests", Guid.NewGuid().ToString("N")), old);
        var freshDumps = Folder(Path.Combine("HeapScopeTests", Guid.NewGuid().ToString("N")), DateTime.UtcNow);
        var shared = Folder(Path.Combine("HeapScopeTests", "data"), old);
        var staleTest = Folder("heapscope-rebuild-0ia20aie.xw4", old);
        var byHand = Folder("heapscope-nuget", old);
        var appOwn = Folder("HeapScope", old);
        File.WriteAllText(Path.Combine(staleDumps, "app.dmp"), "dump");
        Directory.SetLastWriteTimeUtc(staleDumps, old);

        TestFolders.SweepStale(_temp, DateTime.UtcNow - TestFolders.StaleAfter).ShouldBe(2);

        Directory.Exists(staleDumps).ShouldBeFalse();
        Directory.Exists(staleTest).ShouldBeFalse();
        new[] { freshDumps, shared, byHand, appOwn }.ShouldAllBe(f => Directory.Exists(f));
    }

    [Fact]
    public void A_folder_is_deleted_with_what_it_holds()
    {
        var folder = Folder("heapscope-delete-abcdefgh.ijk", DateTime.UtcNow);
        File.WriteAllText(Path.Combine(folder, "app.dmp"), "dump");

        TestFolders.Delete(folder, TimeSpan.Zero).ShouldBeTrue();
        Directory.Exists(folder).ShouldBeFalse();
        TestFolders.Delete(folder, TimeSpan.Zero).ShouldBeTrue();
    }

    [Fact]
    public void A_folder_a_process_holds_is_said_not_swallowed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to delete a file that is open.");
        var folder = Folder("heapscope-held-abcdefgh.ijk", DateTime.UtcNow);
        var error = new StringWriter();
        var before = Console.Error;
        Console.SetError(error);
        try
        {
            using (File.Create(Path.Combine(folder, "app.dmp")))
            {
                TestFolders.Delete(folder, TimeSpan.Zero).ShouldBeFalse();
            }
        }
        finally
        {
            Console.SetError(before);
        }

        error.ToString().ShouldContain(folder);
        error.ToString().ShouldContain("could not be deleted");
    }

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private string Folder(string name, DateTime writeTimeUtc)
    {
        var path = Path.Combine(_temp, name);
        Directory.CreateDirectory(path);
        Directory.SetLastWriteTimeUtc(path, writeTimeUtc);
        return path;
    }
}
