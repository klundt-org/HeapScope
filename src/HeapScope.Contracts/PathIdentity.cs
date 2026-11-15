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

#if PATH_IDENTITY_INTERNAL
namespace HeapScope.Shared;
#else
namespace HeapScope.Contracts;
#endif

/// <summary>
/// When two paths name the same file (sprint 3.12, B3): without regard to case on Windows and macOS, whose file systems ignore
/// it by default (the review's M3), and exactly elsewhere, where <c>app.dmp</c> and <c>App.dmp</c> are two files. One rule for
/// every path comparison of the worker, the app, the command line, the loader and capture, so an open, a session's folder or
/// a deletion never takes one file for another. This one file is compiled into Contracts (public, for the front ends and the
/// worker) and into the projects on the Core side that cannot reference Contracts (internal, <c>HeapScope.Shared</c>).
/// </summary>
#if PATH_IDENTITY_INTERNAL
internal
#else
public
#endif
static class PathIdentity
{
    /// <summary>Whether this system's paths ignore case: Windows and macOS.</summary>
    public static bool IgnoresCase => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>The comparison of this system (<paramref name="ignoreCase"/> overrides it, for tests).</summary>
    public static StringComparison Comparison(bool? ignoreCase = null) =>
        ignoreCase ?? IgnoresCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The comparer of this system, for sets and dictionaries of paths.</summary>
    public static StringComparer Comparer(bool? ignoreCase = null) =>
        ignoreCase ?? IgnoresCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> name the same file (two nulls do not).</summary>
    public static bool Same(string? a, string? b, bool? ignoreCase = null) =>
        a is not null && b is not null && string.Equals(a, b, Comparison(ignoreCase));

    /// <summary>
    /// Whether <paramref name="path"/> is inside <paramref name="folder"/>, by whole folder names: <c>sessions-old</c> is not
    /// inside <c>sessions</c>. Both are taken as given; pass full paths.
    /// </summary>
    public static bool IsUnder(string path, string folder, bool? ignoreCase = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(folder);
        var root = folder.EndsWith(Path.DirectorySeparatorChar) || folder.EndsWith(Path.AltDirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;
        return path.StartsWith(root, Comparison(ignoreCase));
    }
}
