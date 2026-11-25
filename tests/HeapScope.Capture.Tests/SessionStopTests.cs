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


namespace HeapScope.Capture.Tests;

/// <summary>A session's stop never hangs on a target that does not answer (peer review 4.5 H9).</summary>
public sealed class SessionStopTests
{
    [Fact]
    public async Task A_stop_that_never_answers_lets_the_session_go_after_the_wait()
    {
        var disposed = false;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var answered = await SessionStop.StopAsync(_ => Task.Delay(Timeout.Infinite, CancellationToken.None), () => disposed = true, TimeSpan.FromMilliseconds(200));

        (answered, disposed).ShouldBe((false, true));
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_stop_that_answers_or_finds_the_target_gone_keeps_the_session()
    {
        var disposed = false;

        (await SessionStop.StopAsync(_ => Task.CompletedTask, () => disposed = true, TimeSpan.FromSeconds(5))).ShouldBeTrue();
        (await SessionStop.StopAsync(_ => Task.FromException(new EndOfStreamException()), () => disposed = true, TimeSpan.FromSeconds(5))).ShouldBeTrue();
        disposed.ShouldBeFalse();
    }

    [Fact]
    public void Every_stop_of_a_session_in_capture_goes_through_the_helper()
    {
        var folder = Path.Combine(RepoRoot(), "src", "HeapScope.Capture");
        var offenders = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) != "SessionStop.cs" && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("session.StopAsync(CancellationToken.None)", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    private static string RepoRoot()
    {
        var folder = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(folder, "HeapScope.sln")))
        {
            folder = Path.GetDirectoryName(folder) ?? throw new InvalidOperationException("No HeapScope.sln above the test's folder.");
        }

        return folder;
    }
}
