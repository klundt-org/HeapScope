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

using System.IO;
using HeapScope.Application;
using HeapScope.Application.Jobs;
using HeapScope.Application.Snapshots;
using HeapScope.Contracts;
using HeapScope.UI.Services;
using HeapScope.UI.ViewModels;

namespace HeapScope.UI.ViewModels.Tests;

public sealed class SnapshotsViewModelTests : IDisposable
{
    private const long Gb = 1024L * 1024 * 1024;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));
    private readonly FakeWorkerClient _worker = new();
    private readonly FakeUserInteraction _interaction = new();
    private readonly WorkerOperations _operations;
    private readonly SingleThreadUiDispatcher _ui = new();
    private readonly SnapshotsViewModel _snapshots;
    private PhysicalMemory _memory = new(32 * Gb, 20 * Gb);

    public SnapshotsViewModelTests()
    {
        Directory.CreateDirectory(_folder);
        _operations = new WorkerOperations(_worker, new JobTracker(TimeProvider.System));
        // The page runs on a UI thread of its own: every change of it and its workspaces must be raised there.
        _snapshots = _ui.Invoke(() => new SnapshotsViewModel(TestSessions.Create(_worker, _operations.Jobs, () => _memory), _operations, _ui, _interaction));
        _ui.WatchPage(_snapshots);
        _worker.Service.Rows.Add(["System.String", "600", "36000"]);
        _worker.Service.Rows.Add(["System.Byte[]", "400", "12000"]);
    }

    /// <summary>The only way back from a refused reopen is opening the dump again, so that has to work on the page (3.10 review M40).</summary>
    [Fact]
    public Task Opening_a_dump_again_after_a_refused_reopen_shows_it_afresh() => _ui.RunCheckedAsync(async () =>
    {
        var dump = Dump("app.dmp", 1024);
        _worker.Service.CacheIdentity = 0x8000_0000_0000_0001;
        await _snapshots.OpenPathAsync(dump);
        await _snapshots.Types.QueryTask;
        var before = _snapshots.Selected.ShouldNotBeNull();

        // The worker restarted, and the cache is not the one this snapshot was opened from: the reopen is refused.
        _worker.Service.CacheIdentity = 0x8000_0000_0000_0002;
        _worker.NewGeneration();
        _snapshots.Types.Show(before.Handle);
        await _snapshots.Types.QueryTask;
        _snapshots.Types.Status.ShouldNotBeNull().ShouldContain("was rebuilt");

        _worker.Service.OpenNotes = ["A newer note."];
        await _snapshots.OpenPathAsync(dump);
        await _snapshots.Types.QueryTask;

        // The row stays and says what it shows now (3.11 review M1).
        _snapshots.Selected.ShouldBeSameAs(before);
        before.Notes.ShouldBe(["A newer note."]);
        before.ShownSnapshotId.ShouldBe(before.Handle.Descriptor.SnapshotId);
        _snapshots.Snapshots.ShouldHaveSingleItem().ShouldBeSameAs(before);
        _snapshots.Types.Status.ShouldBeNull();
        _snapshots.Types.Rows.Count.ShouldBe(2);
    });

    /// <summary>
    /// A new row for a dump opened again read as a close and an open: Compare fell back to the automatic pair and Trend ticked an
    /// unticked snapshot again and moved it last, while the steps that asked the earlier snapshot stayed on screen (3.11 review M1).
    /// </summary>
    [Fact]
    public Task Opening_a_dump_again_keeps_the_choices_made_with_it_and_starts_its_path_again() => _ui.RunCheckedAsync(async () =>
    {
        var first = Dump("first.dmp", 10);
        _worker.Service.CacheIdentity = 0x8000_0000_0000_0001;
        await _snapshots.OpenPathAsync(first);

        // The others' caches are the ones a reopen will find, so only the first is refused.
        _worker.Service.CacheIdentity = 0x8000_0000_0000_0002;
        await _snapshots.OpenPathAsync(Dump("second.dmp", 10));
        await _snapshots.OpenPathAsync(Dump("third.dmp", 10));
        var items = _snapshots.Snapshots.ToList();
        items.Count.ShouldBe(3);

        // Not the automatic pair, which is the two latest.
        _snapshots.Compare.Baseline = items[0];
        _snapshots.Compare.Current = items[1];
        _snapshots.Compare.Trend.Choices[1].IsIncluded = false;
        _snapshots.Compare.IsTrendShown = true;
        await _snapshots.Compare.QueryTask;
        await _snapshots.Compare.Trend.QueryTask;
        var ticks = _snapshots.Compare.Trend.Choices.Select(static c => (c.Snapshot, c.IsIncluded)).ToList();
        _snapshots.Selected = items[0];
        _snapshots.Navigator.OpenObject(items[0].Handle, 0x1000);
        _snapshots.Navigator.Current.ShouldBeOfType<ObjectViewModel>();
        var changed = new List<string?>();
        items[0].PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _worker.NewGeneration();
        _snapshots.Types.Show(items[0].Handle);
        await _snapshots.Types.QueryTask;
        _snapshots.Types.Status.ShouldNotBeNull().ShouldContain("was rebuilt");
        await _snapshots.OpenPathAsync(first);
        await _snapshots.Types.QueryTask;
        await _snapshots.Compare.QueryTask;
        await _snapshots.Compare.Trend.QueryTask;

        // Their rows came from the earlier snapshot, and a page fetched later would ask the new one (3.11 review).
        var reopened = items[0].Handle.Descriptor.SnapshotId;
        _worker.Service.DiffQueries[^1].BaselineId.ShouldBe(reopened, "the pair holds the snapshot, so it runs again");
        _worker.Service.TrendQueries[^1].SnapshotIds.ShouldContain(reopened, "the trend ticks it, so it runs again");
        _snapshots.Snapshots.ShouldBe(items);
        _snapshots.Selected.ShouldBeSameAs(items[0]);
        changed.ShouldContain(string.Empty, "the row says again what it shows");
        (_snapshots.Compare.Baseline, _snapshots.Compare.Current).ShouldBe((items[0], items[1]));
        _snapshots.Compare.Trend.Choices.Select(static c => (c.Snapshot, c.IsIncluded)).ShouldBe(ticks);
        _snapshots.Navigator.Current.ShouldBeOfType<TypeSummaryViewModel>("every step asked the earlier snapshot");
        _snapshots.Navigator.Steps.Count.ShouldBe(1);
    });

    /// <summary>After a worker restart a snapshot reopens by itself; opening it again then changes nothing (3.11 review M12).</summary>
    [Fact]
    public Task After_a_silent_reopen_opening_the_dump_again_keeps_its_path() => _ui.RunCheckedAsync(async () =>
    {
        var dump = Dump("app.dmp", 1024);
        await _snapshots.OpenPathAsync(dump);
        await _snapshots.Types.QueryTask;
        var item = _snapshots.Selected.ShouldNotBeNull();
        var shown = _snapshots.Navigator.OpenObject(item.Handle, 0x1000);

        _worker.NewGeneration();
        _snapshots.Types.Show(item.Handle);
        await _snapshots.Types.QueryTask;
        _snapshots.Types.Status.ShouldBeNull();
        item.ShownSnapshotId.ShouldBe(item.Handle.Descriptor.SnapshotId, "the row follows a silent reopen");

        _worker.Service.NextSnapshotId = item.Handle.Descriptor.SnapshotId;
        await _snapshots.OpenPathAsync(dump);

        _snapshots.Selected.ShouldBeSameAs(item);
        _snapshots.Navigator.Current.ShouldBeSameAs(shown);
    });

    [Fact]
    public Task Opening_a_dump_that_is_already_open_changes_nothing() => _ui.RunCheckedAsync(async () =>
    {
        var dump = Dump("app.dmp", 1024);
        await _snapshots.OpenPathAsync(dump);
        await _snapshots.Types.QueryTask;
        var before = _snapshots.Selected.ShouldNotBeNull();

        _worker.Service.NextSnapshotId = before.Handle.Descriptor.SnapshotId;
        await _snapshots.OpenPathAsync(dump);

        _snapshots.Selected.ShouldBeSameAs(before);
    });

    [Fact]
    public Task Opening_a_dump_lists_and_selects_it_and_loads_its_largest_types() => _ui.RunCheckedAsync(async () =>
    {
        var dump = Dump("app.dmp", 1024);

        await _snapshots.OpenPathAsync(dump);
        await _snapshots.Types.QueryTask;
        var rows = _snapshots.Types.Rows;
        rows.GetRow(0);
        await rows.WhenLoadedAsync();

        var item = _snapshots.Snapshots.ShouldHaveSingleItem();
        _snapshots.Selected.ShouldBeSameAs(item);
        item.Title.ShouldBe("app.dmp");
        item.Process.ShouldStartWith("App (pid 42), .NET 10.0.13");
        item.Source.ShouldContain("from the dump");
        item.Notes.ShouldBe(["A note."]);
        rows.Count.ShouldBe(2);
        (rows.GetRow(0).Name, rows.GetRow(0).Objects, rows.GetRow(0).Bytes).ShouldBe(("System.String", 600L, 36000L));
        (rows.GetRow(1).Name, rows.GetRow(1).Bytes).ShouldBe(("System.Byte[]", 12000L));
        _interaction.Questions.ShouldBeEmpty();
    });

    [Fact]
    public Task A_load_that_may_not_fit_asks_first_and_declining_opens_nothing() => _ui.RunCheckedAsync(async () =>
    {
        _memory = new PhysicalMemory(16 * Gb, Gb / 2);
        _interaction.Answer = false;

        await _snapshots.OpenPathAsync(Dump("big.dmp", 1024));

        _interaction.Questions.ShouldHaveSingleItem().ShouldContain("only 0.5 GB of 16.0 GB");
        _worker.Service.Opened.ShouldBeEmpty();
        _snapshots.Status.ShouldBe("Open canceled.");
    });

    [Fact]
    public Task No_memory_question_when_an_index_cache_exists() => _ui.RunCheckedAsync(async () =>
    {
        _memory = new PhysicalMemory(16 * Gb, 1 * Gb);
        var dump = Dump("cached.dmp", 1024);
        await File.WriteAllBytesAsync(dump + ".hsidx", [1], TestContext.Current.CancellationToken);

        await _snapshots.OpenPathAsync(dump);

        _interaction.Questions.ShouldBeEmpty();
        _worker.Service.Opened.ShouldHaveSingleItem();
    });

    [Fact]
    public Task Open_dump_command_uses_the_file_dialog_and_does_nothing_when_canceled() => _ui.RunCheckedAsync(async () =>
    {
        _interaction.NextFile = null;
        await _snapshots.OpenDumpCommand.ExecuteAsync(null);
        _worker.Service.Opened.ShouldBeEmpty();

        _interaction.NextFile = Dump("picked.dmp", 10);
        await _snapshots.OpenDumpCommand.ExecuteAsync(null);
        _worker.Service.Opened.ShouldHaveSingleItem().Path.ShouldBe(_interaction.NextFile);
    });

    [Fact]
    public Task Closing_removes_the_snapshot_and_closes_it_in_the_worker() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Dump("a.dmp", 10));
        var item = _snapshots.Selected!;

        await _snapshots.CloseCommand.ExecuteAsync(item);

        _snapshots.Snapshots.ShouldBeEmpty();
        _snapshots.Selected.ShouldBeNull();
        _worker.Service.Closed.ShouldBe([item.Handle.Descriptor.SnapshotId]);
    });

    [Fact]
    public Task Missing_file_is_reported_and_nothing_is_opened() => _ui.RunCheckedAsync(async () =>
    {
        await _snapshots.OpenPathAsync(Path.Combine(_folder, "missing.dmp"));

        _snapshots.Status!.ShouldContain("does not exist");
        _snapshots.IsOpening.ShouldBeFalse();
        _worker.Service.Opened.ShouldBeEmpty();
    });

    [Fact]
    public Task A_worker_error_while_opening_is_shown_as_the_status() => _ui.RunCheckedAsync(async () =>
    {
        _worker.Service.OpenFailure = new HeapScope.Application.Workers.WorkerOperationException(
            Contracts.HeapScopeErrorCode.SnapshotFormat, "Not a dump.", null);

        await _snapshots.OpenPathAsync(Dump("broken.dmp", 10));

        _snapshots.Status.ShouldBe("Not a dump.");
        _snapshots.Snapshots.ShouldBeEmpty();
    });

    [Fact]
    public Task A_DAC_found_next_to_the_dump_is_loaded_only_after_the_user_trusts_it() => _ui.RunCheckedAsync(async () =>
    {
        // Sprint 3.6, peer review P2: the app asks, naming the file and its hash, and remembers a yes by the hash.
        var trusted = new TrustedDacs(_folder);
        var snapshots = new SnapshotsViewModel(TestSessions.Create(_worker, _operations.Jobs, () => _memory, trustedDacs: trusted), _operations, _ui, _interaction);
        _ui.WatchPage(snapshots);
        var dac = new DacTrustDetails(Path.Combine(_folder, "mscordaccore.dll"), new string('a', 64), "10.0.13");
        _worker.Service.RequiredDac = dac;
        var dump = Dump("received.dmp", 10);

        _interaction.Answer = false;
        await snapshots.OpenPathAsync(dump);
        _interaction.Questions.ShouldHaveSingleItem().ShouldContain(dac.Sha256);
        _interaction.Questions[0].ShouldContain(dac.Path);
        snapshots.Snapshots.ShouldBeEmpty();
        snapshots.Status!.ShouldContain("not trusted");
        trusted.All().ShouldBeEmpty("a no is not remembered");

        _interaction.Answer = true;
        await snapshots.OpenPathAsync(dump);
        snapshots.Snapshots.ShouldHaveSingleItem();
        trusted.Kept().ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            d => d.Sha256.ShouldBe(dac.Sha256),
            d => d.Path.ShouldBe(dac.Path),
            d => d.Reason.ShouldContain(dump));
        _worker.Service.Opened.ShouldHaveSingleItem().TrustedDacs.ShouldBe([dac.Sha256]);

        // Kept across runs: a new session sends it with every open without asking.
        new TrustedDacs(_folder).All().ShouldBe([dac.Sha256]);
    });

    // Sprint 3.12 (owner, 2026-11-13): after a "no" the snapshot opens from its cache without value reads, and the page offers
    // Trust this DAC and reopen, which asks the same question, trusts, closes the snapshot and opens the dump again.
    [Fact]
    public Task A_declined_DAC_can_be_trusted_later_from_the_snapshot_and_the_dump_opens_again() => _ui.RunCheckedAsync(async () =>
    {
        var trusted = new TrustedDacs(_folder);
        using var snapshots = new SnapshotsViewModel(TestSessions.Create(_worker, _operations.Jobs, () => _memory, trustedDacs: trusted), _operations, _ui, _interaction);
        _ui.WatchPage(snapshots);
        var dac = new DacTrustDetails(Path.Combine(_folder, "mscordaccore.dll"), new string('b', 64), "10.0.13");
        _worker.Service.RequiredDac = dac;
        _worker.Service.CachedWithoutDac = true;
        var dump = Dump("received.dmp", 10);

        _interaction.Answer = false;
        await snapshots.OpenPathAsync(dump);
        var declined = snapshots.Selected.ShouldNotBeNull();
        var declinedId = declined.Handle.Descriptor.SnapshotId;
        snapshots.Status.ShouldBe("Opened without the dump's DAC (Data Access Component): field values and strings are not shown. The snapshot's notes say why.");
        snapshots.CanTrustDac.ShouldBeTrue();
        snapshots.TrustDacAndReopenCommand.CanExecute(null).ShouldBeTrue();

        // A second no changes nothing.
        await snapshots.TrustDacAndReopenCommand.ExecuteAsync(null);
        _interaction.Questions.Count.ShouldBe(2);
        trusted.All().ShouldBeEmpty();
        snapshots.Selected.ShouldBeSameAs(declined);
        _worker.Service.Closed.ShouldBeEmpty();

        _interaction.Answer = true;
        await snapshots.TrustDacAndReopenCommand.ExecuteAsync(null);

        _interaction.Questions.Count.ShouldBe(3);
        _interaction.Questions[2].ShouldContain(dac.Sha256);
        trusted.Kept().ShouldHaveSingleItem().Reason.ShouldContain("Trust this DAC and reopen");
        _worker.Service.Closed.ShouldBe([declinedId]);

        // In place (3.12 re-review): the same item and handle, now on the new worker snapshot, in the mode it was opened with.
        snapshots.Selected.ShouldBeSameAs(declined);
        snapshots.Snapshots.ShouldHaveSingleItem();
        declined.Handle.Descriptor.SnapshotId.ShouldNotBe(declinedId);
        declined.Handle.Descriptor.UntrustedDac.ShouldBeNull();
        declined.Handle.Descriptor.IsAvailable(SnapshotFeatureIds.FieldValues).ShouldBeTrue();
        _worker.Service.Opened[^1].TrustedDacs.ShouldBe([dac.Sha256]);
        snapshots.CanTrustDac.ShouldBeFalse();
        snapshots.Status.ShouldNotBeNull().ShouldEndWith("the Compare, Inspections and Retainers tabs analyze it further.", customMessage: "opened with everything (sprint 3.13.2)");
    });

    // The 3.12 re-review: a reopen that loads the dump asks about memory first, and a "no" leaves the snapshot as it was.
    [Fact]
    public Task Trust_this_DAC_and_reopen_asks_about_memory_before_it_closes_anything() => _ui.RunCheckedAsync(async () =>
    {
        var trusted = new TrustedDacs(_folder);
        using var snapshots = new SnapshotsViewModel(TestSessions.Create(_worker, _operations.Jobs, () => _memory, trustedDacs: trusted), _operations, _ui, _interaction);
        _ui.WatchPage(snapshots);
        _worker.Service.RequiredDac = new DacTrustDetails(Path.Combine(_folder, "mscordaccore.dll"), new string('c', 64), "10.0.13");
        _worker.Service.CachedWithoutDac = true;
        var dump = Dump("big.dmp", 1024);
        await File.WriteAllBytesAsync(dump + ".hsidx", [1], TestContext.Current.CancellationToken);
        _interaction.Answer = false;
        await snapshots.OpenPathAsync(dump);
        var item = snapshots.Selected.ShouldNotBeNull();

        // The worker now reads its cache as one to rebuild (an older loader's), so the reopen is a load.
        File.Delete(dump + ".hsidx");
        _memory = new PhysicalMemory(16 * Gb, Gb / 2);
        _interaction.Answers.Enqueue(true);
        _interaction.Answers.Enqueue(false);

        await snapshots.TrustDacAndReopenCommand.ExecuteAsync(null);

        _interaction.Titles[^1].ShouldBe("Not enough free memory");
        _worker.Service.Closed.ShouldBeEmpty();
        snapshots.Selected.ShouldBeSameAs(item);
        snapshots.Status.ShouldBe("Open canceled.");
    });

    [Fact]
    public Task Trust_this_DAC_and_reopen_keeps_the_memory_mode_the_snapshot_was_opened_with() => _ui.RunCheckedAsync(async () =>
    {
        var trusted = new TrustedDacs(_folder);
        var session = TestSessions.Create(_worker, _operations.Jobs, () => _memory, trustedDacs: trusted);
        using var snapshots = new SnapshotsViewModel(session, _operations, _ui, _interaction);
        _ui.WatchPage(snapshots);
        var dac = new DacTrustDetails(Path.Combine(_folder, "mscordaccore.dll"), new string('d', 64), "10.0.13");
        _worker.Service.RequiredDac = dac;
        _worker.Service.CachedWithoutDac = true;
        var dump = Dump("mapped.dmp", 1024);
        await File.WriteAllBytesAsync(dump + ".hsidx", [1], TestContext.Current.CancellationToken);
        await session.StartOpen(dump, mode: SnapshotMemoryMode.Mapped, checkDac: false).Result;
        snapshots.Selected = snapshots.Snapshots.ShouldHaveSingleItem();

        await snapshots.TrustDacAndReopenCommand.ExecuteAsync(null);

        _worker.Service.Opened[^1].Mode.ShouldBe(SnapshotMemoryMode.Mapped);
        _worker.Service.Opened[^1].TrustedDacs.ShouldBe([dac.Sha256]);
    });

    // Sprint 3.12 (review of task 9): a full dump whose DAC cannot be used opens without value reads, and the status says so.
    [Fact]
    public Task A_full_dump_opened_without_its_DAC_says_so_in_the_status_and_offers_no_trust() => _ui.RunCheckedAsync(async () =>
    {
        _worker.Service.Unavailable[SnapshotFeatureIds.FieldValues] = "Field values and strings are not read: the DAC (Data Access Component) of this dump's .NET 10.0.13 runtime was not found.";

        await _snapshots.OpenPathAsync(Dump("moved.dmp", 10));

        _snapshots.Status.ShouldBe("Opened without the dump's DAC (Data Access Component): field values and strings are not shown. The snapshot's notes say why.");
        _snapshots.CanTrustDac.ShouldBeFalse();

        _worker.Service.Unavailable.Clear();
        await _snapshots.OpenPathAsync(Dump("fine.dmp", 10));
        _snapshots.Status.ShouldBe(string.Format(System.Globalization.CultureInfo.CurrentCulture, InfoTexts.OpenedStatus, "fine.dmp"), "sprint 3.13.2: an open says it worked");
    });

    [Fact]
    public Task Shell_shows_the_snapshots_page_when_a_snapshot_opens() => _ui.RunCheckedAsync(async () =>
    {
        var processes = new ProcessesViewModel(new FakeCaptureService(Path.Combine(_folder, "sessions"), _operations.Jobs), _snapshots, _interaction);
        using var shell = new ShellViewModel(_operations, _ui, new HomeViewModel(_operations, _snapshots), _snapshots, processes, new AboutViewModel(_operations));

        await _snapshots.OpenPathAsync(Dump("b.dmp", 10));

        shell.SelectedItem.Page.ShouldBeSameAs(_snapshots);
        shell.Home.OpenDumpCommand.ShouldBeSameAs(_snapshots.OpenDumpCommand);
    });

    [Theory]
    [InlineData(512L, "512 bytes")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(3L * 1024 * 1024, "3.0 MB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5.00 GB")]
    public void Sizes_are_humanized(long bytes, string expected)
    {
        using var culture = new InvariantCultureScope();

        Format.Bytes(bytes).ShouldBe(expected);
    }

    public void Dispose()
    {
        try
        {
            // Work that ended after the test body is checked too.
            _ui.Check();
        }
        finally
        {
            _ui.Invoke(() =>
            {
                _snapshots.Dispose();
                return true;
            });
            _ui.Dispose();
        }
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string Dump(string name, int bytes)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private sealed class InvariantCultureScope : IDisposable
    {
        private readonly System.Globalization.CultureInfo _previous = System.Globalization.CultureInfo.CurrentCulture;

        public InvariantCultureScope() => System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        public void Dispose() => System.Globalization.CultureInfo.CurrentCulture = _previous;
    }
}
