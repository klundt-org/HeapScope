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

using System.CommandLine;
using System.Globalization;
using HeapScope.Application.Sessions;
using HeapScope.Application.Tutorial;
using Microsoft.Extensions.Logging;

namespace HeapScope.Cli;

/// <summary>
/// The <c>sample</c> commands (sprint 3.13, ADR-042): the tutorial's sample without the app, for a demo or a script.
/// <c>start</c> runs in the foreground until Ctrl+C or <c>sample stop</c>; the sample exits with it, so none is left behind.
/// </summary>
internal static partial class CliCommands
{
    private static readonly TimeSpan s_sampleWatch = TimeSpan.FromMilliseconds(500);

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
        var grow = new Option<bool>("--grow") { Description = "Keep growing the scenarios that can, a step every second, for Live, Compare and Trend." };
        var start = new Command("start", "Start the sample with the scenarios chosen and keep it running until Ctrl+C or 'sample stop'. Capture it from another terminal.");
        start.Options.Add(scenariosOption);
        start.Options.Add(grow);
        start.SetAction((parseResult, ct) => RunAsync(parseResult, services, async context =>
        {
            var samples = context.Get<SampleService>();
            var names = parseResult.GetValue(scenariosOption)!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
            try
            {
                while (samples.IsSample(sample.ProcessId))
                {
                    await Task.Delay(s_sampleWatch, ct).ConfigureAwait(false);
                }

                await context.Output.WriteLineAsync($"Sample {pid} stopped.").ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Ctrl+C is how this command ends: the sample is stopped and the command succeeds.
                samples.Stop(sample.ProcessId);
                await context.Output.WriteLineAsync($"Sample {pid} stopped.").ConfigureAwait(false);
            }

            return Success;
        }));

        var pidOption = new Option<int>("--pid") { Description = "The sample's process id, as 'sample start' printed it.", Required = true };
        var stop = new Command("stop", "Stop a sample that 'sample start' runs. Only HeapScope's sample is stopped; any other process is refused.");
        stop.Options.Add(pidOption);
        stop.SetAction((parseResult, ct) => RunAsync(parseResult, services, async context =>
        {
            var processId = parseResult.GetValue(pidOption);
            var processes = await context.Get<ICaptureService>().ListProcessesAsync(ct).ConfigureAwait(false);
            var entry = processes.FirstOrDefault(p => p.ProcessId == processId);
            if (!IsSampleCommandLine(entry?.CommandLine))
            {
                context.Logger.LogInformation("Sample stop refused for {Pid}: {Reason}", processId, entry is null ? "no such .NET process" : "not the sample");
                await context.Error.WriteLineAsync(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Error: {processId} is not HeapScope's sample, so it was not stopped. 'heapscope-cli list' shows the running .NET processes.")).ConfigureAwait(false);
                return Failure;
            }

            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(processId);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // It exited meanwhile.
            }

            context.Logger.LogInformation("Sample {Pid} stopped from the command line", processId);
            await context.Output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"Sample {processId} stopped.")).ConfigureAwait(false);
            return Success;
        }));

        var command = new Command("sample", "HeapScope's own sample process, with memory problems planted on purpose, to learn HeapScope on (the Learn page in the app).");
        command.Subcommands.Add(list);
        command.Subcommands.Add(start);
        command.Subcommands.Add(stop);
        return command;
    }

    /// <summary>Whether a process's command line runs HeapScope's sample with scenarios (as <c>sample start</c> does).</summary>
    internal static bool IsSampleCommandLine(string? commandLine) =>
        commandLine is not null
        && commandLine.Contains("heapscope-sample", StringComparison.Ordinal)
        && commandLine.Contains("--scenarios", StringComparison.Ordinal);
}
