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

namespace HeapScope.Integration.Tests.Infrastructure;

internal static class TestPaths
{
#if DEBUG
    public const string Configuration = "Debug";
#else
    public const string Configuration = "Release";
#endif

    /// <summary>The runtimes the fixtures are built for and the known answers are checked on. Before <see cref="FixtureRuntime"/>, which reads it.</summary>
    public static readonly IReadOnlyList<string> FixtureRuntimes = ["net8.0", "net9.0", "net10.0"];

    /// <summary>
    /// The runtime the fixtures run on: <c>HEAPSCOPE_FIXTURE_RUNTIME</c> (net8.0, net9.0 or net10.0), net10.0 when unset
    /// (sprint 4.7, peer review 4.5 H1). HeapScope itself always runs on net10.0.
    /// </summary>
    public static string FixtureRuntime { get; } = FixtureRuntimeOf(Environment.GetEnvironmentVariable(FixtureRuntimeVariable));

    public const string FixtureRuntimeVariable = "HEAPSCOPE_FIXTURE_RUNTIME";

    /// <summary>The major version of <see cref="FixtureRuntime"/>: 8, 9 or 10.</summary>
    public static int FixtureRuntimeMajor => int.Parse(FixtureRuntime.AsSpan(3, FixtureRuntime.IndexOf('.', StringComparison.Ordinal) - 3), System.Globalization.CultureInfo.InvariantCulture);

    internal static string FixtureRuntimeOf(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "net10.0"
        : FixtureRuntimes.Contains(value.Trim()) ? value.Trim()
        : throw new InvalidOperationException($"{FixtureRuntimeVariable} is '{value}'; it must be one of {string.Join(", ", FixtureRuntimes)}.");

    public static string RepoRoot => global::HeapScope.Tests.Shared.RepoRoot.Path;

    /// <summary>A program's file name on this system: with .exe on Windows, without elsewhere (sprint 4.7, peer review 4.5 H12).</summary>
    public static string Executable(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    public static string WorkerExe => Path.Combine(AppContext.BaseDirectory, "worker", Executable("HeapScope.Worker"));

    public static string CliOutput => Path.Combine(RepoRoot, "src", "HeapScope.Cli", "bin", Configuration, "net10.0");

    public static string CliExe => Path.Combine(CliOutput, Executable("heapscope-cli"));

    public static string UiOutput => Path.Combine(RepoRoot, "src", "HeapScope.UI", "bin", Configuration, "net10.0");

    public static string UiExe => Path.Combine(UiOutput, Executable("HeapScope"));

    /// <summary>The tutorial's sample process (sprint 3.13).</summary>
    public static string SampleExe => Path.Combine(RepoRoot, "src", "HeapScope.Sample", "bin", Configuration, "net10.0", Executable("heapscope-sample"));

    public static string LeakyConsoleExe => Fixture("LeakyConsole", FixtureRuntime);

    /// <summary>LeakyConsole on <paramref name="runtime"/>, whatever <see cref="FixtureRuntime"/> is: for a test of one runtime's dumps.</summary>
    public static string LeakyConsoleExeOn(string runtime) => Fixture("LeakyConsole", FixtureRuntimeOf(runtime));

    public static string LeakyApiExe => Fixture("LeakyApi", FixtureRuntime);

    public static string LoadGenExe => Fixture("LoadGen", "net10.0");

    public static string DenseHeapExe => Fixture("DenseHeap", "net10.0");

    public static string KnownAnswers => Path.Combine(RepoRoot, "fixtures", "KnownAnswers");

    private static string Fixture(string name, string runtime) => Path.Combine(RepoRoot, "fixtures", name, "bin", Configuration, runtime, Executable(name));
}
