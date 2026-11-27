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
using HeapScope.Contracts;
using HeapScope.Core;

namespace HeapScope.Application.Logging;

/// <summary>
/// A report of a crash of the app, written where it ends and offered at its next start (sprint 4.8, peer review 4.5 M13): the
/// versions, the system, where it failed and the error with its stack, so it can be attached to an issue. It is a log file of its
/// own in the log folder (<c>heapscope-&lt;date&gt;-&lt;app&gt;-&lt;pid&gt;-crash-&lt;time&gt;.log</c>), so it holds what a log may
/// hold, is kept 14 days like the logs, and Purge logs deletes it. Nothing is sent anywhere.
/// </summary>
public static class CrashReport
{
    private const string Marker = "-crash-";
    private const string Offered = "-offered";

    /// <summary>Writes a report of <paramref name="exception"/> into <paramref name="folder"/>; its path, or null when it could not be written.</summary>
    /// <param name="where">Where it was caught, for example "unhandled, HeapScope closed".</param>
    public static string? Write(string folder, string app, Exception exception, string where, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var path = Path.Combine(
            folder,
            string.Create(CultureInfo.InvariantCulture, $"heapscope-{now:yyyyMMdd}-{app}-{Environment.ProcessId}{Marker}{now:HHmmssfff}.log"));
        try
        {
            PrivateFiles.CreateDirectory(folder);
            using var writer = new StreamWriter(PrivateFiles.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            writer.WriteLine($"# HeapScope crash report: {RuntimeEnvironment.Summary}; {app} pid {Environment.ProcessId}; {now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture)}");
            writer.WriteLine($"# Where: {where}. To report it, read this file, then paste the error and its first lines of stack into an issue at {HelpLinks.Issues}; issues are public, so replace what you do not want published (SUPPORT.md).");
            writer.WriteLine(exception.ToString());
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The reports in <paramref name="folder"/> not offered yet, oldest first.</summary>
    public static IReadOnlyList<string> NotOffered(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(folder, $"heapscope-*{Marker}*.log")
                .Where(static f => !Path.GetFileNameWithoutExtension(f).EndsWith(Offered, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Marks a report offered, so the next start does not offer it again; its new path, or the old one when it could not.</summary>
    public static string MarkOffered(string report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var offered = Path.Combine(Path.GetDirectoryName(report)!, Path.GetFileNameWithoutExtension(report) + Offered + ".log");
        try
        {
            File.Move(report, offered);
            return offered;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return report;
        }
    }
}
