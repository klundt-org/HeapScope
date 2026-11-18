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

namespace HeapScope.Sample;

/// <summary>
/// The sample's command line (sprint 3.13): <c>--list</c>, or <c>--scenarios a,b [--count N] [--payload BYTES]
/// [--grow-every MS] [--parent-pid PID]</c>. Parsed apart from running it, so the rules are tested without a process.
/// </summary>
internal sealed record SampleArguments(
    bool List,
    IReadOnlyList<Scenario> Scenarios,
    ScenarioOptions Options,
    TimeSpan? GrowEvery,
    int? ParentPid)
{
    /// <summary>Items each scenario plants unless told (review M13: twice the detectors' threshold of 1,000).</summary>
    public const int DefaultCount = 2_000;

    /// <summary>The most a scenario's items may hold together (review M5).</summary>
    public const long MaxPlantedBytes = 256L * 1024 * 1024;

    /// <summary>The heap at which a growing sample stops growing (review M5).</summary>
    public const long GrowthLimitBytes = 512L * 1024 * 1024;

    /// <summary>The shortest growth interval (review M5).</summary>
    public const int MinGrowEveryMs = 100;

    /// <summary>The usage line.</summary>
    public const string Usage = "Usage: heapscope-sample --list | --scenarios <name>[,<name>...] [--count N] [--payload BYTES] [--grow-every MS] [--parent-pid PID]";

    /// <summary>Reads <paramref name="args"/>; the error says what is wrong and what to do.</summary>
    public static (SampleArguments? Arguments, string? Error) Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Contains("--list"))
        {
            return (new SampleArguments(true, [], new ScenarioOptions(0, 0), null, null), null);
        }

        if (Value(args, "--scenarios") is not { Length: > 0 } names)
        {
            return (null, "Choose one or more scenarios with --scenarios; --list shows them. " + Usage);
        }

        var scenarios = new List<Scenario>();
        foreach (var name in names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ScenarioCatalog.Find(name) is not { } scenario)
            {
                return (null, $"There is no scenario {name}. The scenarios are: {string.Join(", ", ScenarioCatalog.All.Select(s => s.Name))}.");
            }

            if (!scenarios.Contains(scenario))
            {
                scenarios.Add(scenario);
            }
        }

        foreach (var scenario in scenarios)
        {
            if (scenarios.FirstOrDefault(other => scenario.CannotRunWith.Contains(other.Name, StringComparer.OrdinalIgnoreCase)) is { } other)
            {
                return (null, $"{scenario.Name} and {other.Name} cannot run together: {scenario.ConflictReason ?? "they conflict"}. Choose one.");
            }
        }

        // Review M13: 2,000 items, twice the 1,000 the detectors report from. Review M5: what a scenario plants is bounded.
        var count = Number(args, "--count", DefaultCount, 1, 100_000);
        var payload = Number(args, "--payload", 64, 0, 65_536);
        var grow = Number(args, "--grow-every", 0, 0, 3_600_000);
        var parent = Number(args, "--parent-pid", 0, 0, int.MaxValue);
        if ((count.Error ?? payload.Error ?? grow.Error ?? parent.Error) is { } error)
        {
            return (null, error);
        }

        if ((long)count.Value * payload.Value > MaxPlantedBytes)
        {
            return (null, $"--count times --payload is at most {MaxPlantedBytes / (1024 * 1024)} MB: choose fewer or smaller items.");
        }

        if (grow.Value is > 0 and < MinGrowEveryMs)
        {
            return (null, $"--grow-every is at least {MinGrowEveryMs} ms.");
        }

        return (
            new SampleArguments(
                false,
                scenarios,
                new ScenarioOptions(count.Value, payload.Value),
                grow.Value > 0 ? TimeSpan.FromMilliseconds(grow.Value) : null,
                parent.Value > 0 ? parent.Value : null),
            null);
    }

    private static string? Value(IReadOnlyList<string> args, string name)
    {
        var list = args as IList<string> ?? [.. args];
        var index = list.IndexOf(name);
        return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
    }

    private static (int Value, string? Error) Number(IReadOnlyList<string> args, string name, int fallback, int min, int max)
    {
        if (Value(args, name) is not { } text)
        {
            return (fallback, null);
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max
            ? (value, null)
            : (fallback, $"{name} takes a whole number from {min} to {max}, not {text}.");
    }
}
