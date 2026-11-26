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
using HeapScope.Core.Graph;
using HeapScope.Integration.Tests.Infrastructure;
using HeapScope.Loaders.ClrMd;
using Microsoft.Diagnostics.NETCore.Client;

namespace HeapScope.Integration.Tests;

/// <summary>
/// A heap dump of .NET 8 leaves out the memory its statics are kept in (sprint 4.7, found running the known answers on .NET 8): the
/// loader says so, as <see cref="SnapshotFacts.StaticsUnread"/>, instead of showing what statics hold as unreachable without a word.
/// From .NET 9 on, a heap dump has them, and on Linux a heap dump of .NET 8 does too.
/// </summary>
[Trait("Category", "Integration")]
public sealed class HeapDumpStaticsTests
{
    [Theory]
    [InlineData("net8.0", DumpType.WithHeap, true)]
    [InlineData("net8.0", DumpType.Full, false)]
    [InlineData("net10.0", DumpType.WithHeap, false)]
    public async Task A_dump_without_its_statics_says_so(string runtime, DumpType type, bool unread)
    {
        // Linux's createdump keeps the loader heaps in a heap dump; the runtime's own writer on Windows does not.
        unread &= OperatingSystem.IsWindows();
        using var folder = new TempFolder();
        using (var fixture = await FixtureProcess.StartConsoleOnAsync(runtime, "--leak", "StaticCache", "--count", "100"))
        {
            fixture.CaptureDump(folder.File("app.dmp"), type);
        }

        var result = new ClrMdDumpLoader().Load(folder.File("app.dmp"), cancellationToken: TestContext.Current.CancellationToken);

        using var graph = result.Graph;
        result.Info.Facts.HasFlag(SnapshotFacts.StaticsUnread).ShouldBe(unread);
        var named = Enumerable.Range(0, result.Graph.ObjectCount)
            .SelectMany(i => result.Graph.Roots.GetRoots(i).ToArray())
            .Any(r => r.Kind == RootKind.StaticField && result.Graph.Strings[r.DetailId] == "Fixtures.Leaks.StaticCacheLeak.s_cache");
        named.ShouldBe(!unread);
    }

    // Peer review 4.5 H5 (sprint 4.7): the worker reserves a first load's estimate and logs it against what the load used.
    [Fact]
    public async Task A_first_load_is_reserved_and_logged_against_its_estimate()
    {
        using var folder = new TempFolder();
        using (var fixture = await FixtureProcess.StartConsoleAsync("--leak", "StaticCache", "--count", "100"))
        {
            fixture.CaptureDump(folder.File("app.dmp"));
        }

        var logs = folder.File("logs");
        using var open = ChildProcess.Start(TestPaths.CliExe, new Dictionary<string, string> { ["Logging__File__Directory"] = logs }, "open", folder.File("app.dmp"));
        (await open.WaitForExitAsync(TimeSpan.FromSeconds(120))).ShouldBeTrue(open.Output);
        open.Process.ExitCode.ShouldBe(0, open.Output);

        var log = string.Join(Environment.NewLine, Directory.GetFiles(logs, "*.log").Select(File.ReadAllText));
        log.ShouldContain("First load of " + folder.File("app.dmp"));
        log.ShouldContain("(reserved)");
    }
}
