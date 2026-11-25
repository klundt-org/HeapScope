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
using HeapScope.Integration.Tests.Infrastructure;

namespace HeapScope.Integration.Tests;

/// <summary>
/// Every lesson's steps followed on a real sample (sprint 3.13 review H1): the command line does what the lesson tells the user
/// to do in the app (sort by Retained, select an inspection, compare two captures) on a dump of the lesson's scenario, and
/// arrives at the type the lesson checks. A scenario added without a check here fails
/// <see cref="Every_scenario_with_a_lesson_has_its_steps_checked"/>: ADR-042's extension contract.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LessonTests : IDisposable
{
    // Scenarios without a lesson (LessonCatalog.NoLesson) and those SampleTests checks, with an allocation trace and a live one.
    private static readonly string[] s_checkedElsewhere = ["Inspection", "SteadyAllocation", "AllocationBursts"];

    private readonly TempFolder _root = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] s_checkedHere =
    [
        "StaticCache",
        "EventHandler",
        "Closure",
        "UndisposedList",
        "UnboundedDictionary",
        "Timer",
        "DisposedReferenced",
        "PendingAsync",
        "FinalizerBacklog",
        "Waste",
        "LargeObjects",
    ];

    public static TheoryData<string> Scenarios => new(s_checkedHere);

    [Fact]
    public async Task Every_scenario_with_a_lesson_has_its_steps_checked()
    {
        (await AnswersAsync()).Keys.Except(s_checkedElsewhere).Order(StringComparer.Ordinal).ShouldBe(s_checkedHere.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task A_lesson_s_steps_lead_to_its_answer(string scenario)
    {
        var answer = (await AnswersAsync())[scenario];
        if (scenario == "UnboundedDictionary")
        {
            // Two captures of the growing sample, compared, filtered to the sample's types and sorted by Bytes +/-.
            using var growing = await FixtureProcess.StartSampleAsync("--scenarios", scenario, "--grow-every", "200");
            var first = _root.File("first.dmp");
            var second = _root.File("second.dmp");
            growing.CaptureDump(first);
            await Task.Delay(TimeSpan.FromSeconds(1.5), Ct);
            growing.CaptureDump(second);
            var diff = await CsvAsync("diff", first, second, "--filter", "Fixtures.Leaks", "--sort", "bytes-delta", "--all");
            var top = diff.First(r => r["Name"].StartsWith("Fixtures.Leaks.", StringComparison.Ordinal));
            top["Name"].ShouldBe(answer);
            long.Parse(top["BytesDelta"], System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThan(0);
            return;
        }

        using var sample = await FixtureProcess.StartSampleAsync("--scenarios", scenario);
        var dump = _root.File(scenario + ".dmp");
        sample.CaptureDump(dump);
        switch (scenario)
        {
            case "StaticCache":
                // Tick Retained sizes, filter Fixtures.Leaks, sort by Retained: the top row of the sample's own types.
                (await SampleTypesByRetainedAsync(dump))[0].ShouldBe(answer);
                break;
            case "Closure":
                // The top row is the display class; the captured type is next, held by its field.
                var byRetained = await SampleTypesByRetainedAsync(dump);
                byRetained[0].ShouldContain("<>c__DisplayClass");
                byRetained[1].ShouldBe(answer);
                break;
            case "UndisposedList":
                // Filter Fixtures.Leaks: the type with thousands of objects.
                var types = await CsvAsync("summary", dump, "--filter", "Fixtures.Leaks", "--all");
                types.Where(r => r["Name"].StartsWith("Fixtures.Leaks.", StringComparison.Ordinal) && int.Parse(r["Objects"], System.Globalization.CultureInfo.InvariantCulture) >= 1_000)
                    .Select(r => r["Name"]).ShouldBe([answer]);
                break;
            case "EventHandler":
                (await InspectAsync(dump, "event-handler-leaks"))[0]["SubscriberTypes"].ShouldStartWith(answer + " (");
                break;
            case "Timer":
                (await InspectAsync(dump, "timer-leaks"))[0]["Target"].ShouldBe(answer);
                break;
            case "DisposedReferenced":
                (await InspectAsync(dump, "disposed-but-referenced"))[0]["Type"].ShouldBe(answer);
                break;
            case "PendingAsync":
                (await InspectAsync(dump, "pending-async-methods"))[0]["Type"].ShouldStartWith(answer + ".");
                break;
            case "FinalizerBacklog":
                (await InspectAsync(dump, "finalizer-backlog"))[0]["Type"].ShouldBe(answer);
                break;
            case "Waste":
                // Each empty or oversized collection of the sample's was made to hold the answer.
                foreach (var inspection in new[] { "empty-collections", "oversized-collections" })
                {
                    var own = (await InspectAsync(dump, inspection)).Where(r => r["Type"].Contains("Fixtures.Leaks.", StringComparison.Ordinal)).ToList();
                    own.ShouldNotBeEmpty(inspection);
                    own.ShouldAllBe(r => r["Type"].Contains("<" + answer + ">") || r["Type"].Contains(", " + answer + ">"), inspection);
                }

                break;
            case "LargeObjects":
                var large = await InspectAsync(dump, "large-arrays");
                large.ShouldNotBeEmpty();
                large.ShouldAllBe(r => r["Type"] == answer, "the small arrays are not listed");
                break;
            default:
                throw new InvalidOperationException($"No check for {scenario}.");
        }
    }

    public void Dispose() => _root.Dispose();

    // The sample's catalog, as the app reads it: the answer type of each scenario.
    private static async Task<Dictionary<string, string>> AnswersAsync()
    {
        using var list = ChildProcess.Start(TestPaths.SampleExe, "--list");
        (await list.WaitForExitAsync(TimeSpan.FromSeconds(60))).ShouldBeTrue(list.Output);
        using var json = JsonDocument.Parse(list.StandardOutput);
        return json.RootElement.GetProperty("Scenarios").EnumerateArray()
            .ToDictionary(s => s.GetProperty("Name").GetString()!, s => s.GetProperty("AnswerType").GetString()!, StringComparer.Ordinal);
    }

    private async Task<List<string>> SampleTypesByRetainedAsync(string dump) =>
        [.. (await CsvAsync("summary", dump, "--filter", "Fixtures.Leaks", "--sort", "retained", "--all"))
            .Select(r => r["Name"]).Where(n => n.StartsWith("Fixtures.Leaks.", StringComparison.Ordinal))];

    private Task<List<Dictionary<string, string>>> InspectAsync(string dump, string inspection) => CsvAsync("inspect", dump, inspection, "--all");

    private async Task<List<Dictionary<string, string>>> CsvAsync(params string[] arguments)
    {
        using var cli = ChildProcess.Start(TestPaths.CliExe, [.. arguments, "--format", "csv"]);
        (await cli.WaitForExitAsync(TimeSpan.FromSeconds(180))).ShouldBeTrue(cli.Output);
        cli.Process.ExitCode.ShouldBe(0, cli.Output);
        var lines = cli.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var header = Fields(lines[0]);
        return [.. lines.Skip(1).Select(line => header.Zip(Fields(line)).ToDictionary(p => p.First, p => p.Second, StringComparer.Ordinal))];
    }

    // One CSV line: fields separated by commas, a quoted field may hold commas and doubled quotes.
    private static List<string> Fields(string line)
    {
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted && c == '"' && i + 1 < line.Length && line[i + 1] == '"')
            {
                field.Append('"');
                i++;
            }
            else if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }
}
