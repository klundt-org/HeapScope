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

using System.Globalization;
using System.Text.RegularExpressions;

namespace HeapScope.Tests.Shared;

/// <summary>
/// The tests' own folders in the temp folder (sprint 4.7): deleted with retries, a folder that cannot be deleted is said on
/// the error stream rather than swallowed, and what a killed or failed run left behind is swept by the next run. A run once left
/// 17 GB of dumps there, each folder's delete having failed silently on a file a process still held.
/// </summary>
internal static partial class TestFolders
{
    /// <summary>Where the integration tier's dump folders and the shared data and sessions folders live.</summary>
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "HeapScopeTests");

    /// <summary>A folder older than this is a leftover: no test run takes a day.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(1);

    private const int Attempts = 10;

    /// <summary>Deletes <paramref name="path"/> and all it holds; true when it is gone. A failure after the retries is written to the error stream.</summary>
    public static bool Delete(string path) => Delete(path, TimeSpan.FromMilliseconds(200));

    internal static bool Delete(string path, TimeSpan pause)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == Attempts)
                {
                    Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"A test folder could not be deleted after {Attempts} tries; the next run removes it after a day: {path} ({ex.GetType().Name}: {ex.Message})"));
                    return false;
                }

                Thread.Sleep(pause);
            }
        }
    }

    /// <summary>
    /// Removes the folders a run left more than <see cref="StaleAfter"/> ago: the dump folders in <see cref="Root"/> (named by a
    /// GUID) and the tests' <c>heapscope-*</c> folders in the temp folder. Fresh folders are another run's, and are kept. Returns
    /// how many were removed.
    /// </summary>
    public static int SweepStale() => SweepStale(Path.GetTempPath(), DateTime.UtcNow - StaleAfter);

    internal static int SweepStale(string temp, DateTime before)
    {
        var removed = 0;
        foreach (var folder in Leftovers(temp))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(folder) < before)
                {
                    Directory.Delete(folder, recursive: true);
                    removed++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Held by a process still running, or another run's sweep took it first: said, and the next run tries again.
                Console.Error.WriteLine($"A stale test folder could not be swept; the next run tries again: {folder} ({ex.GetType().Name}: {ex.Message})");
            }
        }

        return removed;
    }

    private static IEnumerable<string> Leftovers(string temp)
    {
        IEnumerable<string> Folders(string folder, string pattern)
        {
            try
            {
                return Directory.Exists(folder) ? Directory.GetDirectories(folder, pattern) : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        // Only names tests make: a GUID in HeapScopeTests (the shared data and sessions folders are kept), and heapscope-<name>-<random>
        // in the temp folder, as Directory.CreateTempSubdirectory names it (a random 8.3 name after the prefix). HeapScope's own temp folder
        // (HeapScope, for its trace caches) and anything named by hand (such as heapscope-nuget) do not match.
        return Folders(Path.Combine(temp, "HeapScopeTests"), "*").Where(f => Guid().IsMatch(Path.GetFileName(f)))
            .Concat(Folders(temp, "heapscope-*").Where(f => Prefixed().IsMatch(Path.GetFileName(f))));
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex Guid();

    // Windows: a random 8.3 name (abcdefgh.ijk); Unix: mkdtemp's six letters and digits, mixed case.
    [GeneratedRegex(@"^heapscope-[a-z0-9-]+-([a-z0-9]{8}\.[a-z0-9]{3}|[A-Za-z0-9]{6})$")]
    private static partial Regex Prefixed();
}
