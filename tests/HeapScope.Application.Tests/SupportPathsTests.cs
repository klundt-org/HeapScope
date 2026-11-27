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

using HeapScope.Application.Logging;
using HeapScope.Application.Workers;
using Microsoft.Extensions.Logging;

namespace HeapScope.Application.Tests;

/// <summary>Sprint 4.8 (peer review 4.5 M13): the support paths a user needs when something goes wrong.</summary>
public sealed class SupportPathsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HeapScopeTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_crash_report_is_a_log_of_its_own_offered_once()
    {
        var report = CrashReport.Write(_directory, "ui", new InvalidOperationException("broken"), "unhandled, HeapScope closed", DateTimeOffset.Now).ShouldNotBeNull();

        Path.GetFileName(report).ShouldMatch(@"^heapscope-\d{8}-ui-\d+-crash-\d{9}\.log$", "a log file's name, so Purge logs and the 14 days apply");
        var text = File.ReadAllText(report);
        text.ShouldContain("HeapScope crash report: HeapScope ");
        text.ShouldContain("Where: unhandled, HeapScope closed.");
        text.ShouldContain(Contracts.HelpLinks.Issues);
        text.ShouldContain("InvalidOperationException: broken");
        CrashReport.NotOffered(_directory).ShouldBe([report]);

        var offered = CrashReport.MarkOffered(report);

        File.Exists(offered).ShouldBeTrue();
        CrashReport.NotOffered(_directory).ShouldBeEmpty("offered once");
        CrashReport.NotOffered(Path.Combine(_directory, "none")).ShouldBeEmpty();
    }

    [Fact]
    public void A_log_that_cannot_be_written_is_said_once_with_where_and_what_to_do()
    {
        // The log folder's place is taken by a file, so no log can be written.
        Directory.CreateDirectory(_directory);
        var blocked = Path.Combine(_directory, "logs");
        File.WriteAllText(blocked, "not a folder");
        var said = new List<string>();
        using var told = new ManualResetEventSlim();
        using (var provider = new FileLoggerProvider(blocked, LogLevel.Information))
        {
            provider.BecameUnavailable += message =>
            {
                lock (said)
                {
                    said.Add(message);
                }

                told.Set();
            };
            var logger = provider.CreateLogger("Test");
            logger.LogInformation("one");
            logger.LogInformation("two");

            told.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ShouldBeTrue();
            provider.Unavailable.ShouldNotBeNull().ShouldContain(blocked);
        }

        Thread.Sleep(200);
        lock (said)
        {
            said.ShouldHaveSingleItem().ShouldContain("Free space on that drive or check that the folder is yours");
        }
    }

    [Fact]
    public void The_last_unexpected_failures_id_is_kept_for_copy_details()
    {
        ErrorIds.Note("Something the user did wrong, with no id.");
        var before = ErrorIds.Last;

        ErrorIds.Note($"The analysis worker failed unexpectedly while answering this, and nothing was changed. Try again; if it fails again, report it at {Contracts.HelpLinks.Issues} with this id: 0f3a9c2e41b84d7f.");

        ErrorIds.Last.ShouldBe("0f3a9c2e41b84d7f");
        before.ShouldNotBe("0f3a9c2e41b84d7f");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
