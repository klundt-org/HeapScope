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

using System.Text.RegularExpressions;

namespace HeapScope.Application.Workers;

/// <summary>
/// The id of the last unexpected worker failure this process was told: the worker's message
/// gives it ("... with this id: <c>id</c>."), and the About page's Copy details carries it, so an issue names the log lines.
/// </summary>
public static partial class ErrorIds
{
    private static string? s_last;

    /// <summary>The last id, or null when no failure with an id happened in this process.</summary>
    public static string? Last => Volatile.Read(ref s_last);

    /// <summary>Keeps the id <paramref name="message"/> gives, if it gives one.</summary>
    public static void Note(string? message)
    {
        if (message is not null && IdInMessage().Match(message) is { Success: true } match)
        {
            Volatile.Write(ref s_last, match.Groups["id"].Value);
        }
    }

    [GeneratedRegex(@"with this id: (?<id>[0-9a-zA-Z\-]+)\.?\s*$")]
    private static partial Regex IdInMessage();
}
