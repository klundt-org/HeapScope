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

using HeapScope.Core;
using Microsoft.Extensions.Logging;

namespace HeapScope.Worker;

/// <summary>
/// The worker's line for a change of what the logs may hold (sprint 3.12 review M19; sprint 3.13.2): a log that holds values
/// read from a dump says so from the line where it starts to, as a warning so a log kept at Warning says so too.
/// </summary>
internal static class PrivacyLog
{
    /// <summary>Logs every change <paramref name="privacy"/> reads from now on.</summary>
    public static void Attach(LogPrivacy privacy, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(privacy);
        ArgumentNullException.ThrowIfNull(logger);
        privacy.Changed += change => logger.Log(
            change.IncludeDumpValues ? LogLevel.Warning : LogLevel.Information,
            "The logs now hold {Privacy} ({File}){DumpValues}",
            change,
            privacy.SettingsFile,
            change.IncludeDumpValues ? "; from here on this log holds values read from dumps, so do not share it" : string.Empty);
    }
}
