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

namespace HeapScope.Contracts;

/// <summary>
/// When two paths name the same file (sprint 3.12, B3): without regard to case on Windows, whose file systems ignore it, and
/// exactly elsewhere, where <c>app.dmp</c> and <c>App.dmp</c> are two files. One rule for every path comparison of the worker,
/// the app and the command line, so an open, a session's folder or a deletion never takes one file for another.
/// </summary>
public static class PathIdentity
{
    /// <summary>The comparison of this system (<paramref name="windows"/> overrides it, for tests).</summary>
    public static StringComparison Comparison(bool? windows = null) =>
        windows ?? OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The comparer of this system, for sets and dictionaries of paths.</summary>
    public static StringComparer Comparer(bool? windows = null) =>
        windows ?? OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> name the same file (two nulls do not).</summary>
    public static bool Same(string? a, string? b, bool? windows = null) =>
        a is not null && b is not null && string.Equals(a, b, Comparison(windows));

    /// <summary>
    /// Whether <paramref name="path"/> is inside <paramref name="folder"/>, by whole folder names: <c>sessions-old</c> is not
    /// inside <c>sessions</c>. Both are taken as given; pass full paths.
    /// </summary>
    public static bool IsUnder(string path, string folder, bool? windows = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(folder);
        var root = folder.EndsWith(Path.DirectorySeparatorChar) || folder.EndsWith(Path.AltDirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;
        return path.StartsWith(root, Comparison(windows));
    }
}
