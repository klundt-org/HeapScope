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
using System.Runtime.InteropServices;

namespace HeapScope.Core;

/// <summary>
/// The user who ran HeapScope with sudo, on Linux: HeapScope run as root to reach another
/// user's process keeps that user's data folder (sessions, logs, settings) instead of root's, and gives what it writes (its
/// folders and files, and a dump a capture wrote for it) to that user, so the dump of a service is the user's to read and
/// delete afterwards. Known from <c>SUDO_UID</c>, <c>SUDO_GID</c> and the user's home in <c>/etc/passwd</c>; null when HeapScope
/// does not run as root, was not started by sudo, or sudo ran it for root itself. Never on Windows or macOS (macOS texts never
/// offer sudo, ADR-025).
/// </summary>
/// <param name="UserId">The invoking user's id.</param>
/// <param name="GroupId">The invoking user's primary group.</param>
/// <param name="Name">The user's name, for the start lines.</param>
/// <param name="Home">The user's home folder.</param>
public sealed partial record InvokingUser(int UserId, int GroupId, string Name, string Home)
{
    private static readonly string? s_passwd = OperatingSystem.IsLinux() ? ReadPasswd() : null;

    /// <summary>The user who ran this process with sudo, or null (read once at the start).</summary>
    public static InvokingUser? Current { get; } = OperatingSystem.IsLinux() && Environment.IsPrivilegedProcess
        ? From(Environment.GetEnvironmentVariable, s_passwd)
        : null;

    /// <summary>
    /// The folder of what a run with sudo must not take from the invoking user: the trusted DACs and their
    /// private copies, which decide what native code the root worker loads. Root's own HeapScope data folder, from the password
    /// file's entry for root; null when HeapScope does not run with sudo.
    /// </summary>
    public static string? TrustFolder { get; } = Current is null ? null : Path.Combine(RootHome(s_passwd), ".local", "share", "HeapScope");

    // Root's home, under which nothing is ever given to the invoking user: the trust folder and the
    // folders above it that HeapScope creates stay root's.
    private static readonly string? s_rootHome = Current is null ? null : RootHome(s_passwd);

    /// <summary>Root's home in the password file's text, or /root.</summary>
    internal static string RootHome(string? passwd)
    {
        foreach (var line in (passwd ?? string.Empty).Split('\n'))
        {
            var fields = line.TrimEnd('\r').Split(':');
            if (fields.Length >= 7 && fields[2] == "0" && fields[5].StartsWith('/'))
            {
                return fields[5];
            }
        }

        return "/root";
    }

    /// <summary>The user's HeapScope data folder, as the user's own runs name it (<c>~/.local/share/HeapScope</c>).</summary>
    public string DataFolder => Path.Combine(Home, ".local", "share", "HeapScope");

    /// <summary>
    /// The user from sudo's variables and the password file's text; null when they do not name a user other than root with a
    /// home that is a full path (it starts with /).
    /// </summary>
    internal static InvokingUser? From(Func<string, string?> environment, string? passwd)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!int.TryParse(environment("SUDO_UID"), NumberStyles.None, CultureInfo.InvariantCulture, out var userId) || userId == 0
            || !int.TryParse(environment("SUDO_GID"), NumberStyles.None, CultureInfo.InvariantCulture, out var groupId))
        {
            return null;
        }

        var id = userId.ToString(CultureInfo.InvariantCulture);
        foreach (var line in (passwd ?? string.Empty).Split('\n'))
        {
            // name:password:uid:gid:gecos:home:shell
            var fields = line.TrimEnd('\r').Split(':');
            if (fields.Length >= 7 && fields[2] == id && fields[5].StartsWith('/'))
            {
                return new InvokingUser(userId, groupId, environment("SUDO_USER") is { Length: > 0 } name ? name : fields[0], fields[5]);
            }
        }

        return null;
    }

    /// <summary>
    /// Gives <paramref name="path"/> to the user who ran this process with sudo, when there is one. Never follows a link: the
    /// link itself changes owner, not what it points to. False when it could not (a file system that keeps root's files root's).
    /// </summary>
    public static bool Give(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Current is not { } user || IsRoots(path, s_rootHome) || IsTrusts(path, TrustFolder))
        {
            return false;
        }

        try
        {
            return LChown(path, (uint)user.UserId, (uint)user.GroupId) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="path"/> is root's home or under it: what decides which DAC root loads lives there,
    /// and a folder or file of it given to the user would let the user choose that code.
    /// </summary>
    internal static bool IsRoots(string path, string? rootHome)
    {
        if (string.IsNullOrEmpty(rootHome))
        {
            return false;
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var home = Path.TrimEndingDirectorySeparator(rootHome);

        // A home of "/" would make everything root's; the trust folder's own rule (IsTrusts) covers that case.
        return home != "/" && (full == home || full.StartsWith(home + "/", StringComparison.Ordinal));
    }

    /// <summary>Whether <paramref name="path"/> is the trust folder, under it, or a folder above it HeapScope may create on the way.</summary>
    internal static bool IsTrusts(string path, string? trustFolder)
    {
        if (string.IsNullOrEmpty(trustFolder))
        {
            return false;
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var trust = Path.TrimEndingDirectorySeparator(trustFolder);
        return full == trust || full.StartsWith(trust + "/", StringComparison.Ordinal) || trust.StartsWith(full + "/", StringComparison.Ordinal);
    }

    private static string? ReadPasswd()
    {
        try
        {
            return File.ReadAllText("/etc/passwd");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [LibraryImport("libc", EntryPoint = "lchown", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LChown(string path, uint owner, uint group);
}
