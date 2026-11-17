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

using System.CommandLine;
using System.Globalization;
using HeapScope.Application.Tutorial;

namespace HeapScope.Cli;

/// <summary>
/// The <c>sample</c> commands (sprint 3.13, ADR-042): the tutorial's sample without the app, for a demo or a script.
/// <c>start</c> runs in the foreground until Ctrl+C or <c>sample stop</c>; the sample exits with it, so none is left behind.
/// <c>stop</c> stops only a running sample a HeapScope process started (its record in the data folder, with its start time),
/// on any system (review H4).
/// </summary>
internal static partial class CliCommands
{
    private static Command BuildSampleCommand(Func<IServiceProvider> services)
    {
        var list = new Command("list", "List the sample's scenarios: what each one plants, and the type it plants.");
        list.SetAction((parseResult, ct) => RunAsync(parseResult, services, async context =>
        {
            var samples = context.Get<SampleService>();
            IReadOnlyList<SampleScenario> scenarios;
            try
            {
                scenarios = await samples.ListScenariosAsync(ct).ConfigureAwait(false);
            }
            catch (SampleRefusedException ex)
            {
                await context.Error.WriteLineAsync("Error: " + ex.Message).ConfigureAwait(false);
                return Failure;
            }

            await context.Output.WriteLineAsync($"{"Scenario",-20} {"Grows",-6} Plants").ConfigureAwait(false);
            foreach (var scenario in scenarios)
            {
                await context.Output.WriteLineAsync($"{scenario.Name,-20} {(scenario.Grows ? "yes" : "no"),-6} {scenario.Plants} ({scenario.AnswerType})").ConfigureAwait(false);
            }

            return Success;
        }));

        var scenariosOption = new Option<string>("--scenarios")
        {
            Description = "The scenarios to run, separated by commas (see 'sample list').",
            Required = true,
        };
        var grow = new Option<bool>("--grow") { Description = "Keep growing the scenarios that can, a step every second, for Live, Compare and Trend; growth stops when the sample's heap reaches 512 MB." };
        var start = new Command("start", "Start the sample with the scenarios chosen and keep it running until Ctrl+C or 'sample stop'. Capture it from another terminal.");
        start.Options.Add(scenariosOption);
        start.Options.Add(grow);
        start.SetAction((parseResult, ct) => RunAsync(parseResult, services, async context =>
        {
            var samples = context.Get<SampleService>();
            var names = parseResult.GetValue(scenariosOption)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
            RunningSample sample;
            try
            {
                sample = await samples.StartAsync(names, parseResult.GetValue(grow), ct).ConfigureAwait(false);
            }
            catch (SampleRefusedException ex)
            {
                await context.Error.WriteLineAsync("Error: " + ex.Message).ConfigureAwait(false);
                return Failure;
            }

            var pid = sample.ProcessId.ToString(CultureInfo.InvariantCulture);
            await context.Output.WriteLineAsync($"Sample {pid} runs: {string.Join(", ", sample.Scenarios)}{(sample.Grows ? ", growing" : string.Empty)}.").ConfigureAwait(false);
            await context.Output.WriteLineAsync($"Capture it: heapscope-cli capture --pid {pid}").ConfigureAwait(false);
            await context.Output.WriteLineAsync($"Stop it: Ctrl+C here, or heapscope-cli sample stop --pid {pid}").ConfigureAwait(false);
            await context.Output.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            if (samples.ExitAsync(sample.ProcessId) is not { } exited)
            {
                // It exited between the start and now.
                await context.Error.WriteLineAsync($"Sample {pid} exited on its own as soon as it started. HeapScope's log has its last message.").ConfigureAwait(false);
                return Failure;
            }

            SampleExit exit;
            try
            {
                exit = await exited.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Ctrl+C is how this command ends: the sample is stopped and the command succeeds.
                samples.Stop(sample.ProcessId, "Ctrl+C on the command line");
                await context.Output.WriteLineAsync($"Sample {pid} stopped.").ConfigureAwait(false);
                return Success;
            }

            // Review M9: a stop from 'sample stop' succeeds; a crash, or Task Manager, is a failure with the sample's own words.
            if (exit.Reason == SampleExitReason.OnItsOwn)
            {
                await context.Error.WriteLineAsync(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Sample {pid} exited on its own (exit code {exit.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}){(exit.Error.Length > 0 ? ": " + exit.Error : ".")}")).ConfigureAwait(false);
                return Failure;
            }

            await context.Output.WriteLineAsync($"Sample {pid} stopped.").ConfigureAwait(false);
            return Success;
        }));

        var pidOption = new Option<int>("--pid") { Description = "The sample's process id, as 'sample start' printed it.", Required = true };
        var stop = new Command("stop", "Stop a sample that 'sample start' or the app runs. Only HeapScope's sample is stopped; any other process is refused.");
        stop.Options.Add(pidOption);
        stop.SetAction((parseResult, ct) => RunAsync(parseResult, services, async context =>
        {
            var processId = parseResult.GetValue(pidOption);
            SampleStopOutcome outcome;
            try
            {
                outcome = context.Get<SampleService>().StopRecorded(processId);
            }
            catch (SampleRefusedException ex)
            {
                await context.Error.WriteLineAsync("Error: " + ex.Message).ConfigureAwait(false);
                return Failure;
            }

            if (outcome == SampleStopOutcome.NotASample)
            {
                await context.Error.WriteLineAsync(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Error: {processId} is not a running sample that HeapScope started, so it was not stopped. 'sample start' printed its sample's process id.")).ConfigureAwait(false);
                return Failure;
            }

            await context.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"Sample {processId} stopped.")).ConfigureAwait(false);
            return Success;
        }));

        var command = new Command("sample", "HeapScope's own sample process, with memory problems planted on purpose, to learn HeapScope on (the Learn page in the app).");
        command.Subcommands.Add(list);
        command.Subcommands.Add(start);
        command.Subcommands.Add(stop);
        return command;
    }
}
