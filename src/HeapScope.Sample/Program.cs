// HeapScope
// Copyright (C) 2026 Ray Klundt
// https://github.com/rklundt/HeapScope
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
using Fixtures.Leaks;
using HeapScope.Sample;

// The tutorial's sample process (sprint 3.13, ADR-042): plants the chosen scenarios, prints "READY pid=<pid>", keeps
// growing them when asked, and exits when the process that started it exits.
var (arguments, error) = SampleArguments.Parse(args);
if (arguments is null)
{
    await Console.Error.WriteLineAsync(error).ConfigureAwait(false);
    return 64;
}

if (arguments.List)
{
    // The catalog the app reads, so it never offers a scenario this sample lacks.
    var entries = ScenarioCatalog.All.Select(s => new
    {
        s.Name,
        s.Title,
        s.Plants,
        s.KnownAnswer,
        s.CannotRunWith,
        Grows = s.Grow is not null,
    });
    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(entries)).ConfigureAwait(false);
    return 0;
}

ParentWatch.ExitWhenParentExits(arguments.ParentPid);
foreach (var scenario in arguments.Scenarios.Where(s => !s.AfterLastCollection))
{
    scenario.Start(arguments.Options);
}

// As the fixtures do: the planted objects settle before the scenarios that block the finalizer thread for good.
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
if (arguments.Scenarios.Any(s => s.Name == "Waste"))
{
    WasteFixtures.CreateFinalizerBacklog();
}

foreach (var scenario in arguments.Scenarios.Where(s => s.AfterLastCollection))
{
    scenario.Start(arguments.Options);
}

await Console.Out.WriteLineAsync($"READY pid={Environment.ProcessId}").ConfigureAwait(false);
if (arguments.GrowEvery is { } every)
{
    var step = Math.Max(1, arguments.Options.Count / 10);
    using var timer = new PeriodicTimer(every);
    while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
    {
        foreach (var scenario in arguments.Scenarios)
        {
            scenario.Grow?.Invoke(step, arguments.Options);
        }
    }
}

await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
return 0;
