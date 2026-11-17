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

namespace HeapScope.Cli.Tests;

/// <summary>
/// The <c>sample</c> commands (sprint 3.13, task 9): usage errors before any host, and <c>stop</c> refuses a process that is not
/// HeapScope's sample, on every system (review H4). Starting, capturing and stopping the real sample is an integration test (SampleTests).
/// </summary>
public sealed class SampleCommandTests
{
    [Theory]
    [InlineData("sample", "start")]
    [InlineData("sample", "stop")]
    [InlineData("sample", "stop", "--pid", "x")]
    public async Task A_missing_or_bad_option_is_a_usage_error(params string[] args)
    {
        var (code, _, _) = await InProcessCli.RunAsync(InProcessCli.NoHost, args);

        code.ShouldBe(64);
    }

    [Fact]
    public async Task Stop_refuses_a_process_that_is_not_the_sample()
    {
        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);

        var (code, output, error) = await InProcessCli.RunAsync(CliHost.Build, "sample", "stop", "--pid", pid);

        code.ShouldBe(1);
        output.ShouldBeEmpty();
        error.ShouldContain($"Error: {pid} is not a running sample that HeapScope started, so it was not stopped.");
    }
}
