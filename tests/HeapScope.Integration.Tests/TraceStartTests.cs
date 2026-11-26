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

using System.Diagnostics;
using HeapScope.Capture;
using HeapScope.Integration.Tests.Infrastructure;

namespace HeapScope.Integration.Tests;

/// <summary>
/// A recording of an idle process has bytes on disk as soon as its session streams (sprint 4.7). Its file had a 64 KB write buffer,
/// so an idle target's trace stayed empty for minutes: the "start that took over 60 s" of CI since 2026-11-20, which was never a
/// slow start. The test waits for bytes with no load at all, as the allocation trace fixture does before it starts its load.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TraceStartTests
{
    [Fact]
    public async Task An_idle_targets_recording_has_bytes_on_disk_within_seconds()
    {
        using var folder = new TempFolder();
        using var fixture = await FixtureProcess.StartConsoleAsync("--leak", "StaticCache", "--count", "10");
        var path = folder.File("idle.nettrace");
        using var stop = new CancellationTokenSource();
        var recording = TraceRecorder.RecordAsync(fixture.ProcessId, path, TraceProfile.Allocations, TimeSpan.FromMinutes(2), stop.Token);

        var watch = Stopwatch.StartNew();
        while (!Directory.EnumerateFiles(folder.Path, TraceRecorder.PartialPattern(path)).Any(static f => new FileInfo(f).Length > 0))
        {
            recording.IsCompleted.ShouldBeFalse("the recording ended before its file had bytes");
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30), "an idle target's recording had no bytes on disk");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        await stop.CancelAsync();
        var result = await recording;
        result.Bytes.ShouldBeGreaterThan(0);
    }
}
