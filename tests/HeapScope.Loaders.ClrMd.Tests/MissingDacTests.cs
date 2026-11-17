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

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using HeapScope.Core;
using Microsoft.Diagnostics.Runtime;

namespace HeapScope.Loaders.ClrMd.Tests;

/// <summary>
/// A DAC that is not on this machine, on a real dump (sprint 3.12, the review of task 9): the open's check names it missing,
/// a download that fails is missing too (so an older index cache can open instead), and a download that arrives but cannot
/// be loaded points at the dump, not at a missing DAC. Runs when <c>HEAPSCOPE_FIXTURE_DUMP</c> names a dump of the
/// LeakyConsole fixture, as <see cref="PlantedDacTests"/> does; skipped otherwise, failed when <c>HEAPSCOPE_REQUIRE_FIXTURE</c> is 1.
/// </summary>
public sealed class MissingDacTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("heapscope-missing-dac-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ClrMdLoaderOptions NoLocalDac() => new()
    {
        InstalledRuntimeRoots = [Directory.CreateDirectory(Path.Combine(_work, "no-runtimes")).FullName],
        SearchDumpedRuntimeDirectory = false,
        OwnRuntimeDirectory = null,
    };

    [Fact]
    public void The_open_check_names_a_missing_DAC_and_the_process_name_needs_none()
    {
        var dump = FixtureDump();

        var missing = ClrMdDumpLoader.CheckDac(dump, NoLocalDac()).ShouldBeOfType<RuntimeComponentMissingException>();
        missing.RuntimeVersion.ShouldNotBeNullOrEmpty();
        Should.NotThrow(() => ClrMdDumpLoader.CheckDacTrust(dump, NoLocalDac()), "only a DAC that needs trust is the open's question");
        ClrMdDumpLoader.CheckDac(dump, NoLocalDac() with { AllowMicrosoftSymbolServer = true }).ShouldBeNull("the symbol server may still give it");
        var (name, source) = ClrMdDumpLoader.ReadProcessName(dump, NoLocalDac());
        name.ShouldNotBeNullOrEmpty("LeakyConsole, or dotnet when the fixture ran through the dotnet host");
        source.ShouldNotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("throws")]
    [InlineData("finds nothing")]
    public void A_download_that_fails_is_a_missing_DAC(string server)
    {
        var dump = FixtureDump();
        var options = NoLocalDac() with { AllowMicrosoftSymbolServer = true, SymbolServerLocatorForTests = new Locator(server == "throws" ? null : "", Throws: server == "throws") };

        var missing = Should.Throw<RuntimeComponentMissingException>(() => new ClrMdDumpLoader(options).Load(dump, cancellationToken: Ct));

        missing.Message.ShouldContain("was not downloaded from Microsoft's symbol server");
        if (server == "throws")
        {
            missing.InnerException.ShouldBeOfType<HttpRequestException>();
        }
    }

    [Fact]
    public void The_download_asks_for_this_systems_DAC_the_way_ClrMD_does()
    {
        // A DAC indexed by time stamp and size is asked for by them; one indexed by build id is asked for under the binary it
        // is archived under, whose platform is the dump's (3.12 re-review: a Linux dump's Windows DAC is archived under Linux).
        // A dump of this system cannot tell the dump's platform from the DAC's: that case needs a dump of another system,
        // which no CI job has (Backlog).
        var dump = FixtureDump();
        var locator = new Locator("");
        var options = NoLocalDac() with { AllowMicrosoftSymbolServer = true, SymbolServerLocatorForTests = locator };

        Should.Throw<RuntimeComponentMissingException>(() => new ClrMdDumpLoader(options).Load(dump, cancellationToken: Ct));

        var asked = locator.Asked;
        asked.ShouldNotBeEmpty();
        var dumpPlatform = OperatingSystem.IsWindows() ? OSPlatform.Windows : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux;
        asked.ShouldAllBe(a => a.ByBuildId ? a.OriginalPlatform == dumpPlatform : a.Name.Length > 0);
        if (OperatingSystem.IsWindows())
        {
            asked.ShouldContain(a => !a.ByBuildId && a.Name.EndsWith("mscordaccore.dll", StringComparison.OrdinalIgnoreCase), "a Windows DAC is indexed by time stamp and size");
        }
    }

    [Fact]
    public void A_downloaded_DAC_that_cannot_be_loaded_is_not_called_missing()
    {
        var dump = FixtureDump();
        var garbage = Path.Combine(_work, "mscordaccore.dll");
        File.WriteAllBytes(garbage, new byte[4096]);
        var options = NoLocalDac() with { AllowMicrosoftSymbolServer = true, SymbolServerLocatorForTests = new Locator(garbage) };

        var failed = Should.Throw<SnapshotFormatException>(() => new ClrMdDumpLoader(options).Load(dump, cancellationToken: Ct));

        failed.Message.ShouldContain(garbage);
    }

    public void Dispose() => Directory.Delete(_work, recursive: true);

    /// <summary>The fixture dump, copied alone into a folder of this test: a capture keeps the target's DAC beside the dump.</summary>
    private string FixtureDump()
    {
        var dump = Environment.GetEnvironmentVariable("HEAPSCOPE_FIXTURE_DUMP");
        var reason = "HEAPSCOPE_FIXTURE_DUMP names no dump (set by CI after it takes one).";
        if (string.IsNullOrEmpty(dump) || !File.Exists(dump))
        {
            Assert.False(Environment.GetEnvironmentVariable("HEAPSCOPE_REQUIRE_FIXTURE") == "1", reason);
            Assert.Skip(reason);
        }

        var copy = Path.Combine(_work, "alone", "leaky.dmp");
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(dump!, copy);
        return copy;
    }

    /// <summary>
    /// Stands in for the symbol server (ClrMD 4.2's <see cref="IFileLocator"/> has these two lookups): gives
    /// <paramref name="Found"/> ("" for nothing), or fails as a network would, and records what it was asked for.
    /// </summary>
    private sealed class Locator(string? Found, bool Throws = false) : IFileLocator
    {
        private readonly List<(string Name, bool ByBuildId, OSPlatform OriginalPlatform)> _asked = [];

        public IReadOnlyList<(string Name, bool ByBuildId, OSPlatform OriginalPlatform)> Asked => _asked;

        public string? FindPEImage(string fileName, int buildTimeStamp, int imageSize, bool checkProperties)
        {
            _asked.Add((fileName, false, default));
            return Find();
        }

        public string? FindPEImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> buildIdOrUUID, OSPlatform originalPlatform, bool checkProperties)
        {
            _asked.Add((fileName, true, originalPlatform));
            return Find();
        }

        private string? Find() => Throws ? throw new HttpRequestException("The symbol server could not be reached.") : string.IsNullOrEmpty(Found) ? null : Found;
    }
}
