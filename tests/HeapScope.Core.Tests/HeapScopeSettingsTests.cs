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

using System.Text.Json;

namespace HeapScope.Core.Tests;

/// <summary>Sprint 3.12 (B1): the user's settings, shared by the app, the worker and the command line.</summary>
public sealed class HeapScopeSettingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hs-settings-").FullName;

    private HeapScopeFolders Folders => new(_root);

    [Fact]
    public void Without_a_file_filter_text_is_logged()
    {
        HeapScopeSettings.Read(Folders).IncludeFilterTextInLogs.ShouldBeTrue("on by default (owner, 2026-11-12)");
        new LogPrivacy(Folders).IncludeFilterText.ShouldBeTrue();
    }

    [Fact]
    public void A_saved_setting_is_read_back_and_a_member_a_newer_build_wrote_is_kept()
    {
        File.WriteAllText(HeapScopeSettings.PathIn(Folders), "{\"IncludeFilterTextInLogs\": true, \"FutureOption\": 7}");

        (HeapScopeSettings.Read(Folders) with { IncludeFilterTextInLogs = false }).Save(Folders);

        HeapScopeSettings.Read(Folders).IncludeFilterTextInLogs.ShouldBeFalse();
        using var saved = JsonDocument.Parse(File.ReadAllText(HeapScopeSettings.PathIn(Folders)));
        saved.RootElement.GetProperty("FutureOption").GetInt32().ShouldBe(7);
        Directory.GetFiles(_root, "*.tmp").ShouldBeEmpty("the file is replaced in one step");
    }

    [Fact]
    public void A_damaged_file_reads_as_the_defaults()
    {
        File.WriteAllText(HeapScopeSettings.PathIn(Folders), "{ not json");

        HeapScopeSettings.Read(Folders).IncludeFilterTextInLogs.ShouldBeTrue();
    }

    [Fact]
    public void A_change_reaches_a_running_process_without_a_restart()
    {
        var privacy = new LogPrivacy(Folders, checkEvery: TimeSpan.Zero);
        privacy.IncludeFilterText.ShouldBeTrue();

        new HeapScopeSettings(IncludeFilterTextInLogs: false).Save(Folders);
        privacy.IncludeFilterText.ShouldBeFalse();

        new HeapScopeSettings(IncludeFilterTextInLogs: true).Save(Folders);
        File.SetLastWriteTimeUtc(HeapScopeSettings.PathIn(Folders), DateTime.UtcNow.AddSeconds(5));
        privacy.IncludeFilterText.ShouldBeTrue();
    }

    // Sprint 3.12 review H5: the logs' privacy never falls back to "on" because the file could not be read at that moment.
    [Fact]
    public void An_unreadable_file_keeps_the_last_setting_read_and_is_read_again_next_time()
    {
        var path = HeapScopeSettings.PathIn(Folders);
        var privacy = new LogPrivacy(Folders, checkEvery: TimeSpan.Zero);
        new HeapScopeSettings(IncludeFilterTextInLogs: false).Save(Folders);
        privacy.IncludeFilterText.ShouldBeFalse();

        File.WriteAllText(path, "{ damaged");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
        privacy.IncludeFilterText.ShouldBeFalse("the last setting read, not the default");
        HeapScopeSettings.TryRead(Folders, out _).ShouldBeFalse();

        File.WriteAllText(path, "{\"IncludeFilterTextInLogs\": true}");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
        privacy.IncludeFilterText.ShouldBeTrue("read again once it can be, even with the same write time as the damaged one");
    }

    [Fact]
    public void A_file_that_cannot_be_read_from_the_start_keeps_filter_text_out()
    {
        File.WriteAllText(HeapScopeSettings.PathIn(Folders), "{ damaged");

        new LogPrivacy(Folders, checkEvery: TimeSpan.Zero).IncludeFilterText.ShouldBeFalse("it may say off");
        HeapScopeSettings.TryRead(Folders, out var settings).ShouldBeFalse();
        settings.IncludeFilterTextInLogs.ShouldBeTrue("Read's defaults are unchanged");
        HeapScopeSettings.TryRead(new HeapScopeFolders(Path.Combine(_root, "none")), out _).ShouldBeTrue("no file is the defaults");
    }

    // Sprint 3.13.2 (owner, 2026-11-19): values read from a dump reach the logs only when the user turned that on.
    [Fact]
    public void Dump_values_are_off_by_default_and_in_a_file_written_before_the_setting_existed()
    {
        HeapScopeSettings.Read(Folders).IncludeDumpValuesInLogs.ShouldBeFalse();
        new LogPrivacy(Folders).IncludeDumpValues.ShouldBeFalse();

        File.WriteAllText(HeapScopeSettings.PathIn(Folders), "{\"IncludeFilterTextInLogs\": true}");
        HeapScopeSettings.Read(Folders).IncludeDumpValuesInLogs.ShouldBeFalse("a 3.12 file");
        new LogPrivacy(Folders).Describe().ShouldBe("filter text on, dump values off");
    }

    [Fact]
    public void Turning_dump_values_on_reaches_a_running_process_and_is_announced_with_both_settings()
    {
        var privacy = new LogPrivacy(Folders, checkEvery: TimeSpan.Zero);
        var changes = new List<LogPrivacyChange>();
        privacy.IncludeDumpValues.ShouldBeFalse();
        privacy.Changed += changes.Add;

        new HeapScopeSettings(IncludeDumpValuesInLogs: true).Save(Folders);
        privacy.IncludeDumpValues.ShouldBeTrue();
        privacy.IncludeFilterText.ShouldBeTrue("the other setting is unchanged");

        new HeapScopeSettings(IncludeFilterTextInLogs: false, IncludeDumpValuesInLogs: false).Save(Folders);
        File.SetLastWriteTimeUtc(HeapScopeSettings.PathIn(Folders), DateTime.UtcNow.AddSeconds(5));
        privacy.IncludeDumpValues.ShouldBeFalse();

        changes.Select(c => c.ToString()).ShouldBe(["filter text on, dump values on", "filter text off, dump values off"]);
    }

    [Fact]
    public void A_file_that_cannot_be_read_from_the_start_keeps_dump_values_out()
    {
        File.WriteAllText(HeapScopeSettings.PathIn(Folders), "{\"IncludeDumpValuesInLogs\": tru");

        new LogPrivacy(Folders, checkEvery: TimeSpan.Zero).IncludeDumpValues.ShouldBeFalse();
    }

    [Fact]
    public void Dump_values_go_off_when_the_file_cannot_be_read_and_filter_text_keeps_what_was_read()
    {
        var path = HeapScopeSettings.PathIn(Folders);
        var privacy = new LogPrivacy(Folders, checkEvery: TimeSpan.Zero);
        var changes = new List<LogPrivacyChange>();
        privacy.Changed += changes.Add;
        new HeapScopeSettings(IncludeFilterTextInLogs: true, IncludeDumpValuesInLogs: true).Save(Folders);
        privacy.Read().ShouldBe(new LogPrivacyChange(true, true));

        File.WriteAllText(path, "{ damaged");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

        privacy.Read().ShouldBe(new LogPrivacyChange(true, false), "dump values off, filter text as read last");
        changes[^1].ShouldBe(new LogPrivacyChange(true, false), "the change is announced, so the worker logs it");
    }

    // Sprint 4.8 (H15): the Settings page's DAC folder and the symbol server opt-in.
    [Fact]
    public void The_dac_folder_and_the_symbol_server_round_trip_and_default_to_none_and_off()
    {
        var defaults = HeapScopeSettings.Read(Folders);
        defaults.DacFolder.ShouldBeNull();
        defaults.AllowMicrosoftSymbolServer.ShouldBeFalse("off unless the user turned it on (ADR-017)");

        File.WriteAllText(HeapScopeSettings.PathIn(Folders), "{\"IncludeFilterTextInLogs\": false}");
        (HeapScopeSettings.Read(Folders) with { DacFolder = _root, AllowMicrosoftSymbolServer = true }).Save(Folders);

        var read = HeapScopeSettings.Read(Folders);
        read.DacFolder.ShouldBe(_root);
        read.AllowMicrosoftSymbolServer.ShouldBeTrue();
        read.IncludeFilterTextInLogs.ShouldBeFalse("the other settings are kept");
    }

    [Fact]
    public void A_file_that_cannot_be_read_keeps_the_symbol_server_off()
    {
        File.WriteAllText(HeapScopeSettings.PathIn(Folders), "{\"AllowMicrosoftSymbolServer\": tru");

        HeapScopeSettings.Read(Folders).AllowMicrosoftSymbolServer.ShouldBeFalse();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
