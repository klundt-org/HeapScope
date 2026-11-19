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

using HeapScope.Application.Tests;
using HeapScope.Core;
using Microsoft.Extensions.Logging;

namespace HeapScope.Worker.Tests;

/// <summary>Sprint 3.13.2: a worker's log says when it starts to hold values read from a dump, as a warning.</summary>
public sealed class PrivacyLogTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hs-privacy-").FullName;

    [Fact]
    public void A_change_is_logged_with_both_settings_and_a_warning_when_dump_values_turn_on()
    {
        var folders = new HeapScopeFolders(_root);
        var privacy = new LogPrivacy(folders, checkEvery: TimeSpan.Zero);
        var logger = new RecordingLogger();
        privacy.Read();
        PrivacyLog.Attach(privacy, logger);

        new HeapScopeSettings(IncludeDumpValuesInLogs: true).Save(folders);
        privacy.Read();
        new HeapScopeSettings().Save(folders);
        File.SetLastWriteTimeUtc(HeapScopeSettings.PathIn(folders), DateTime.UtcNow.AddSeconds(5));
        privacy.Read();

        logger.Entries.Count.ShouldBe(2);
        logger.Entries[0].Level.ShouldBe(LogLevel.Warning);
        logger.Entries[0].Line.ShouldBe($"The logs now hold filter text on, dump values on ({HeapScopeSettings.PathIn(folders)}); from here on this log holds values read from dumps, so do not share it");
        logger.Entries[1].Level.ShouldBe(LogLevel.Information);
        logger.Entries[1].Line.ShouldBe($"The logs now hold filter text on, dump values off ({HeapScopeSettings.PathIn(folders)})");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
