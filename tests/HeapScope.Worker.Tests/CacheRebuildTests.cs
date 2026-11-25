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
using HeapScope.Application.Tests;
using HeapScope.Contracts;
using HeapScope.Core;
using HeapScope.Core.Loading;
using HeapScope.Core.Synthetic;
using HeapScope.Loaders.ClrMd;
using HeapScope.Storage;
using HeapScope.Worker.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeapScope.Worker.Tests;

/// <summary>What a cache that cannot be used leads to, and what the snapshot says about it (sprint 3.10 review H6, M39).</summary>
public sealed class CacheRebuildTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("heapscope-rebuild-").FullName;
    private readonly WorkerOptions _options;
    private readonly IndexCacheLocator _locator;
    private readonly string _dump;

    public CacheRebuildTests()
    {
        _options = new WorkerOptions("unused", null) { DataFolder = _folder };
        _locator = new IndexCacheLocator(_options.Folders.Cache);
        _dump = Path.Combine(_folder, "app.dmp");
        File.WriteAllBytes(_dump, [.. "MDMP"u8, 0x93, 0xA7, 0, 0, 1, 2, 3, 4]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly RecordingLogger<SnapshotStore> _log = new();

    private SnapshotStore Store(ISnapshotLoader? loader = null, Func<string, Exception?>? dacCheck = null, Func<string, (string?, string)?>? processName = null) =>
        new(_options, _locator, IndexCacheSigner.None, _log)
        {
            LoaderForTests = _ => loader ?? new NoDacLoader(),
            DacCheckForTests = dacCheck ?? (_ => null),
            ProcessNameForTests = processName ?? (_ => ("LeakyConsole", "the executable's path the dump records")),
        };

    private static UntrustedDacException Untrusted() => new(@"C:\dumps\mscordaccore.dll", new string('a', 64), "10.0.14");

    private static RuntimeComponentMissingException Missing() => new("10.0.14", "mscordaccore.dll", ["C:\\dac (not found)"]);

    private const string UntrustedReason =
        "Field values and strings are not read: the DAC (Data Access Component) next to the dump is not trusted. Trust it to read them (on the command line, add --trust-dac aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa).";

    private const string MissingReason =
        "Field values and strings are not read: the DAC (Data Access Component) of this dump's .NET 10.0.14 runtime was not found. Install that runtime or put its DAC in the DAC folder (Symbols:DacFolder, or --dac-folder on the command line), then close the snapshot and open the dump again.";

    private void WriteDamagedCache()
    {
        WriteCache(ClrMdDumpLoader.CurrentVersion);
        var cache = _locator.GetCachePath(_dump);
        var bytes = File.ReadAllBytes(cache);
        Array.Fill(bytes, (byte)0xFF, 16, 64);
        File.WriteAllBytes(cache, bytes);
    }

    private void WriteCache(int loaderVersion)
    {
        using var graph = SyntheticGraphs.Chain(10);
        var metadata = new IndexCacheMetadata(
            new SnapshotInfo(SnapshotKind.FullDump, _dump, ProcessName: "coreclr"),
            SnapshotCapabilities.ObjectGraph | SnapshotCapabilities.CompleteGraph,
            IndexCacheMetadata.CurrentWriterVersion,
            LoaderVersion: loaderVersion);
        IndexCacheWriter.Write(_locator.GetCachePath(_dump), IndexCacheSigner.None, graph, metadata, SnapshotFingerprint.Compute(_dump), cancellationToken: Ct);
    }

    // Sprint 3.12 (the 3.10 review M14, M29 to M31, M37): a rebuild that cannot reach the DAC opens the older cache, whose
    // graph answers everything but value reads; the snapshot, its feature list and the log say so.
    [Theory]
    [InlineData("missing", SnapshotMemoryMode.InMemory)]
    [InlineData("missing", SnapshotMemoryMode.Mapped)]
    [InlineData("untrusted", SnapshotMemoryMode.InMemory)]
    [InlineData("untrusted", SnapshotMemoryMode.Mapped)]
    public async Task An_older_cache_opens_when_its_rebuild_cannot_reach_the_DAC(string why, SnapshotMemoryMode mode)
    {
        WriteCache(ClrMdDumpLoader.CurrentVersion - 1);
        var cache = _locator.GetCachePath(_dump);
        var before = await File.ReadAllBytesAsync(cache, Ct);
        using var store = Store(new ThrowingLoader(why == "missing" ? Missing() : Untrusted()));
        var phases = new Phases();

        // An untrusted DAC reaches the rebuild only on an open that does not ask (the app asks first, 3.12 re-review).
        var opened = await store.OpenAsync(new OpenSnapshotRequest(_dump, Mode: mode, CheckDac: why == "missing"), phases, Ct);

        opened.FromCache.ShouldBeTrue();
        opened.Mode.ShouldBe(mode);
        opened.CachePath.ShouldBe(cache);
        opened.ObjectCount.ShouldBe(10);
        var cause = why == "missing"
            ? "the DAC (Data Access Component) of this dump's .NET 10.0.14 runtime was not found"
            : "the DAC (Data Access Component) next to the dump is not trusted";
        opened.Notes.ShouldContain(SnapshotOpener.OlderCacheNote(cause));
        opened.Notes.ShouldNotContain(static n => n.StartsWith("The index cache was rebuilt", StringComparison.Ordinal));
        opened.Notes.ShouldNotContain(static n => n.StartsWith("Field values and strings", StringComparison.Ordinal), "the older cache's note says why once");
        var fields = opened.Availability!.Single(a => a.Feature == SnapshotFeatureIds.FieldValues).Unavailable.ShouldNotBeNull();
        fields.ShouldBe(why == "missing" ? MissingReason : UntrustedReason);
        opened.Availability!.Single(a => a.Feature == SnapshotFeatureIds.StringValues).Unavailable.ShouldBe(fields);
        if (why == "untrusted")
        {
            opened.UntrustedDac.ShouldBe(new DacTrustDetails(@"C:\dumps\mscordaccore.dll", new string('a', 64), "10.0.14"));
        }
        else
        {
            opened.UntrustedDac.ShouldBeNull();
        }

        // Loader version 4 changed only the process name: the older cache's is replaced by the one read from the dump.
        opened.ProcessName.ShouldBe("LeakyConsole");
        (await File.ReadAllBytesAsync(cache, Ct)).ShouldBe(before, "the older cache is read, never written");
        var older = string.Create(CultureInfo.InvariantCulture, $"loader version {ClrMdDumpLoader.CurrentVersion - 1}");
        var current = string.Create(CultureInfo.InvariantCulture, $"current loader version {ClrMdDumpLoader.CurrentVersion}");
        var failure = why == "missing" ? nameof(RuntimeComponentMissingException) : nameof(UntrustedDacException);
        _log.Lines.ShouldContain(l => l.Contains("opened in place of a rebuild", StringComparison.Ordinal) && l.Contains(older, StringComparison.Ordinal)
            && l.Contains(current, StringComparison.Ordinal) && l.Contains(failure, StringComparison.Ordinal));
        _log.Lines.ShouldContain(l => l.Contains("opens instead if the rebuild cannot reach the DAC", StringComparison.Ordinal));
        _log.Lines.ShouldContain(l => l.Contains("Process name of", StringComparison.Ordinal) && l.Contains("coreclr", StringComparison.Ordinal) && l.Contains("LeakyConsole", StringComparison.Ordinal));
        _log.Lines.ShouldContain(l => l.Contains("from older index cache (loader version", StringComparison.Ordinal));
        _log.Lines.ShouldContain(l => l.Contains("recorded when the cache was written and not usable now", StringComparison.Ordinal));
        phases.Seen.ShouldContain("Opening the older index cache");
    }

    // A tripwire (3.12 re-review): the older cache's note and its process name step describe what loader version 4 changed.
    [Fact]
    public void The_older_cache_note_matches_the_current_loader_version()
    {
        ClrMdDumpLoader.CurrentVersion.ShouldBe(
            5,
            "a new loader version changes more than the note says: reword SnapshotOpener.OlderCacheNote and WithCurrentProcessName, or raise OldestFallbackLoaderVersion (ADR-041)");
    }

    [Fact]
    public void A_cache_an_older_loader_wrote_is_probed_as_needing_a_load_so_the_memory_warning_sees_it()
    {
        WriteCache(ClrMdDumpLoader.CurrentVersion - 1);
        using var store = Store();

        var probe = store.Probe(_dump);

        probe.CacheState.ShouldBe(SnapshotCacheState.Invalid);
        probe.CacheDetail.ShouldNotBeNull().ShouldContain("older version");

        WriteCache(ClrMdDumpLoader.CurrentVersion);
        store.Probe(_dump).CacheState.ShouldBe(SnapshotCacheState.Valid);
    }

    [Fact]
    public async Task A_process_name_that_cannot_be_read_again_keeps_the_older_caches_and_says_so()
    {
        WriteCache(ClrMdDumpLoader.CurrentVersion - 1);
        using var store = Store(new ThrowingLoader(Missing()), processName: _ => throw new SnapshotFormatException("The dump cannot be read here."));

        var opened = await store.OpenAsync(new OpenSnapshotRequest(_dump), null, Ct);

        opened.FromCache.ShouldBeTrue();
        opened.ProcessName.ShouldBe("coreclr");
        _log.Lines.ShouldContain(l => l.Contains("could not be read again from the dump (SnapshotFormatException: The dump cannot be read here.)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_cache_replaced_by_a_current_one_meanwhile_is_not_opened_as_the_older_one()
    {
        WriteCache(ClrMdDumpLoader.CurrentVersion - 1);
        using var store = Store(new ThrowingLoader(Missing(), during: () => WriteCache(ClrMdDumpLoader.CurrentVersion)));

        await Should.ThrowAsync<RuntimeComponentMissingException>(store.OpenAsync(new OpenSnapshotRequest(_dump), null, Ct));

        _log.Lines.ShouldContain(l => l.Contains("could not be opened in place of the failed rebuild: it now holds loader version " + ClrMdDumpLoader.CurrentVersion.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_DAC_of_another_system_refuses_values_with_the_reason_and_its_own_words()
    {
        WriteCache(ClrMdDumpLoader.CurrentVersion);
        using var store = Store(dacCheck: _ => new RuntimeComponentMissingException("This dump comes from Linux and HeapScope runs on Windows: open it with HeapScope on Linux."));

        var opened = await store.OpenAsync(new OpenSnapshotRequest(_dump), null, Ct);

        opened.WhyUnavailable(SnapshotFeatureIds.FieldValues).ShouldBe(
            "Field values and strings are not read: the dump's DAC (Data Access Component) cannot be used on this machine. This dump comes from Linux and HeapScope runs on Windows: open it with HeapScope on Linux.");
    }

    [Fact]
    public async Task The_failed_rebuild_is_reported_when_the_older_cache_cannot_be_opened_either()
    {
        WriteCache(ClrMdDumpLoader.CurrentVersion - 1);
        var cache = _locator.GetCachePath(_dump);
        using var store = Store(new ThrowingLoader(Missing(), during: () => File.Delete(cache)));

        var failed = await Should.ThrowAsync<RuntimeComponentMissingException>(store.OpenAsync(new OpenSnapshotRequest(_dump), null, Ct));

        failed.RuntimeVersion.ShouldBe("10.0.14");
        _log.Lines.ShouldContain(l => l.Contains("could not be opened in place of the failed rebuild", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_cancel_while_the_rebuild_fails_is_a_cancel_not_a_fallback()
    {
        WriteCache(ClrMdDumpLoader.CurrentVersion - 1);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        // Past the progress throttle's interval, so a fallback's phase would be reported at once.
        using var store = Store(new ThrowingLoader(Missing(), during: () =>
        {
            Thread.Sleep(250);
            cancel.Cancel();
        }));
        var phases = new Phases();

        await Should.ThrowAsync<OperationCanceledException>(store.OpenAsync(new OpenSnapshotRequest(_dump), phases, cancel.Token));

        phases.Seen.ShouldNotContain("Opening the older index cache", "the fallback never starts");
        _log.Lines.ShouldNotContain(l => l.Contains("opened in place of a rebuild", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_cache_older_than_the_first_release_never_opens_in_place_of_a_rebuild()
    {
        // Version 3 is the loader of v3.9.0, the first public release; anything older came from development builds.
        SnapshotKinds.Describe(SnapshotKind.FullDump).OldestFallbackLoaderVersion.ShouldBe(3);
        WriteCache(2);
        using var store = Store();

        var missing = await Should.ThrowAsync<RuntimeComponentMissingException>(store.OpenAsync(new OpenSnapshotRequest(_dump), null, Ct));

        missing.Message.ShouldContain("No DAC here");
        _log.Lines.ShouldContain(l => l.Contains("is older than version 3, so it never opens", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_damaged_dump_does_not_open_the_older_cache()
    {
        // Only a DAC the rebuild cannot reach falls back; a dump that cannot be read fails as it did.
        WriteCache(ClrMdDumpLoader.CurrentVersion - 1);
        using var store = Store(new ThrowingLoader(new SnapshotFormatException("The dump is damaged.")));

        await Should.ThrowAsync<SnapshotFormatException>(store.OpenAsync(new OpenSnapshotRequest(_dump), null, Ct));
    }

    // Sprint 3.12 (B2): an open without the DAC question (declined, or the command line) used to list field values as
    // available and fail on each read.
    [Fact]
    public async Task A_current_cache_opened_without_the_DAC_question_refuses_value_reads_from_the_start_when_the_DAC_is_untrusted()
    {
        WriteCache(ClrMdDumpLoader.CurrentVersion);
        using var store = Store(dacCheck: _ => Untrusted());

        var opened = await store.OpenAsync(new OpenSnapshotRequest(_dump, CheckDac: false), null, Ct);

        opened.FromCache.ShouldBeTrue();
        opened.Availability!.Single(a => a.Feature == SnapshotFeatureIds.FieldValues).Unavailable.ShouldBe(UntrustedReason);
        opened.Notes.ShouldContain(UntrustedReason, "the app's status points to the notes");
        opened.UntrustedDac.ShouldNotBeNull().Sha256.ShouldBe(new string('a', 64));
        store.Close(opened.SnapshotId);

        using var asking = Store(dacCheck: _ => Untrusted());
        await Should.ThrowAsync<UntrustedDacException>(asking.OpenAsync(new OpenSnapshotRequest(_dump), null, Ct));

        using var trusted = Store();
        var readable = await trusted.OpenAsync(new OpenSnapshotRequest(_dump, CheckDac: false), null, Ct);
        readable.Availability!.Single(a => a.Feature == SnapshotFeatureIds.FieldValues).Unavailable.ShouldBeNull();
        readable.Notes.ShouldBeEmpty();
        readable.UntrustedDac.ShouldBeNull();
    }

    // The 3.12 review of task 9 (H): a DAC gone since the cache was written (a runtime update) used to list field values as
    // available, and every read then failed. Both kinds of open refuse it, since no answer to a question would bring it back.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_current_cache_whose_DAC_is_missing_refuses_value_reads_from_the_start(bool checkDac)
    {
        WriteCache(ClrMdDumpLoader.CurrentVersion);
        using var store = Store(dacCheck: _ => Missing());

        var opened = await store.OpenAsync(new OpenSnapshotRequest(_dump, CheckDac: checkDac), null, Ct);

        opened.FromCache.ShouldBeTrue();
        opened.Availability!.Single(a => a.Feature == SnapshotFeatureIds.FieldValues).Unavailable.ShouldBe(MissingReason);
        opened.Notes.ShouldBe([MissingReason]);
        opened.UntrustedDac.ShouldBeNull();
        _log.Lines.ShouldContain(l => l.Contains("opens without reading field values or strings", StringComparison.Ordinal) && l.Contains("C:\\dac (not found)", StringComparison.Ordinal));
        _log.Lines.ShouldContain(l => l.Contains(checkDac ? "DAC question allowed" : "DAC question not asked", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_older_cache_rebuilt_says_so()
    {
        WriteCache(ClrMdDumpLoader.CurrentVersion - 1);
        using var store = Store(new GraphLoader(_dump));

        var opened = await store.OpenAsync(new OpenSnapshotRequest(_dump), null, Ct);

        opened.FromCache.ShouldBeFalse();
        opened.Notes.ShouldContain(static n => n.StartsWith("The index cache was rebuilt: it was written by an older version", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_damaged_cache_rebuilt_says_so()
    {
        WriteDamagedCache();
        using var store = Store(new GraphLoader(_dump));

        var opened = await store.OpenAsync(new OpenSnapshotRequest(_dump), null, Ct);

        opened.Notes.ShouldContain(static n => n.StartsWith("The index cache was rebuilt: ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_cache_that_could_not_be_written_is_not_called_rebuilt(bool damaged)
    {
        // The note used to be added before the load, so a failed write showed "rebuilt" and "could not be written" together (M39).
        if (damaged)
        {
            WriteDamagedCache();
        }
        else
        {
            WriteCache(ClrMdDumpLoader.CurrentVersion - 1);
        }

        // A file where the cache's folder belongs, once the old cache has been read: the write fails on every system.
        var folder = Path.GetDirectoryName(_locator.GetCachePath(_dump))!;
        using var store = Store(new GraphLoader(_dump, () =>
        {
            Directory.Delete(folder, recursive: true);
            File.WriteAllBytes(folder, []);
        }));

        var opened = await store.OpenAsync(new OpenSnapshotRequest(_dump), null, Ct);

        opened.CachePath.ShouldBeNull();
        opened.Notes.ShouldContain(static n => n.StartsWith("The index cache could not be written", StringComparison.Ordinal));
        opened.Notes.ShouldContain(static n => n.StartsWith("The index cache was not used: ", StringComparison.Ordinal));
        opened.Notes.ShouldNotContain(static n => n.StartsWith("The index cache was rebuilt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_reopen_after_a_worker_restart_does_not_quietly_take_an_older_cache()
    {
        // It could replace a current graph whose cache could not be written, and a reopen has no rebuild to report (3.10 review H6).
        WriteCache(ClrMdDumpLoader.CurrentVersion - 1);
        using var store = Store();

        var refused = await Should.ThrowAsync<SnapshotChangedException>(store.OpenAsync(new OpenSnapshotRequest(_dump, RequireCache: true), null, Ct));

        // Its own words (3.12 review of task 9): the dump did not change; the snapshot came from the older cache.
        refused.Message.ShouldBe("app.dmp was opened from an older index cache because its DAC could not be used, so it was not reopened automatically after the analysis worker restarted. Open it again.");

        File.Delete(_locator.GetCachePath(_dump));
        (await Should.ThrowAsync<SnapshotChangedException>(store.OpenAsync(new OpenSnapshotRequest(_dump, RequireCache: true), null, Ct))).Message.ShouldContain("out of date");
    }

    [Fact]
    public async Task An_explicit_rebuild_still_fails_and_says_what_is_missing()
    {
        WriteCache(ClrMdDumpLoader.CurrentVersion - 1);
        using var store = Store();

        await Should.ThrowAsync<RuntimeComponentMissingException>(store.OpenAsync(new OpenSnapshotRequest(_dump, Rebuild: true), null, Ct));
    }

    [Fact]
    public async Task With_no_cache_the_missing_DAC_is_the_answer()
    {
        using var store = Store();

        await Should.ThrowAsync<RuntimeComponentMissingException>(store.OpenAsync(new OpenSnapshotRequest(_dump), null, Ct));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A mapped cache can outlive the test by a moment; the temp folder is the system's to clean.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }

    /// <summary>A load that works, with an optional step while it runs.</summary>
    private sealed class GraphLoader(string dump, Action? during = null) : ISnapshotLoader
    {
        public SnapshotKind Kind => SnapshotKind.FullDump;

        public int Version => ClrMdDumpLoader.CurrentVersion;

        public LoadResult Load(string dumpPath, IProgress<LoadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            during?.Invoke();
            return new LoadResult(
                SyntheticGraphs.Chain(10),
                new SnapshotInfo(SnapshotKind.FullDump, dump),
                SnapshotCapabilities.ObjectGraph,
                new LoadDiagnostics([], 0, 0),
                new LoadTimings(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));
        }
    }

    /// <summary>The progress phases an open reported, as they came.</summary>
    private sealed class Phases : IProgress<JobProgress>
    {
        private readonly List<string> _seen = [];

        public IReadOnlyList<string> Seen
        {
            get
            {
                lock (_seen)
                {
                    return [.. _seen];
                }
            }
        }

        public void Report(JobProgress value)
        {
            lock (_seen)
            {
                _seen.Add(value.Phase);
            }
        }
    }

    private sealed class ThrowingLoader(Exception failure, Action? during = null) : ISnapshotLoader
    {
        public SnapshotKind Kind => SnapshotKind.FullDump;

        public int Version => ClrMdDumpLoader.CurrentVersion;

        public LoadResult Load(string dumpPath, IProgress<LoadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            during?.Invoke();
            throw failure;
        }
    }

    private sealed class NoDacLoader : ISnapshotLoader
    {
        public SnapshotKind Kind => SnapshotKind.FullDump;

        public int Version => ClrMdDumpLoader.CurrentVersion;

        public LoadResult Load(string dumpPath, IProgress<LoadProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new RuntimeComponentMissingException("No DAC here.");
    }
}
