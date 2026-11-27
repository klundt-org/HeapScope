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

using System.CommandLine.Help;
using HeapScope.Application.Snapshots;
using HeapScope.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HeapScope.Cli;

/// <summary>
/// One run of the command line: parse, then (unless the command line is wrong) build the host and run the command. The
/// writers and the host factory are parameters so tests run the CLI in process.
/// </summary>
internal static class CliApp
{
    /// <param name="args">The command line.</param>
    /// <param name="output">Results, help and version (stdout).</param>
    /// <param name="error">Progress, notes and errors (stderr).</param>
    /// <param name="hostFactory">
    /// Builds the host from the worker attach pipe, the DAC folder and the symbol server opt-in (<see cref="CliHost.Build"/>);
    /// a usage error never calls it.
    /// </param>
    /// <param name="errorIsConsole">Whether stderr is a console, where the attribution notice starts a run.</param>
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, Func<string?, string?, bool, IHost> hostFactory, bool errorIsConsole)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(hostFactory);
        IHost? host = null;
        var root = CliCommands.Build(() => host!.Services);
        var parseResult = root.Parse(args);
        s_parsed = parseResult;
        var configuration = CliCommands.CreateInvocationConfiguration(output, error);

        // The Attribution Notice (ADDITIONAL-TERMS.md 1.4) ends help output (version output prints it itself) and starts
        // every other run. At startup it goes to stderr and only to a console, so piped table, csv and json output stays
        // machine readable.
        var isHelp = parseResult.Action is HelpAction;
        if (!isHelp && parseResult.Action is not CliCommands.ProductVersionAction && errorIsConsole)
        {
            await error.WriteLineAsync(ProductInfo.AttributionWithUrl).ConfigureAwait(false);
        }

        if (parseResult.Errors.Count > 0)
        {
            // Prints the errors and usage; usage errors exit 64 (EX_USAGE) so 1 and 2 keep their meanings for CI.
            await parseResult.InvokeAsync(configuration).ConfigureAwait(false);
            await output.WriteLineAsync(ProductInfo.AttributionWithUrl).ConfigureAwait(false);
            return CliCommands.Usage;
        }

        // The worker attach pipe must be known before the host is built. Building and starting it reads appsettings.json: a mistake there
        // is said with the file and the line, and logged, not a stack trace for every command.
        try
        {
            host = hostFactory(
                parseResult.GetValue(CliCommands.PipeOption),
                parseResult.GetValue(CliCommands.DacFolderOption),
                parseResult.GetValue(CliCommands.AllowSymbolServerOption));

            // The CLI never asks: graph-only commands open a cached dump whatever its DAC, and reads that need an untrusted
            // DAC refuse with the --trust-dac option to add.
            host.Services.GetRequiredService<SnapshotSession>().CheckDacOnCacheOpen = false;
            var trustedDacs = host.Services.GetRequiredService<TrustedDacs>();
            foreach (var sha256 in parseResult.GetValue(CliCommands.TrustDacOption) ?? [])
            {
                trustedDacs.TrustForThisRun(sha256);
            }

            await host.StartAsync().ConfigureAwait(false);
            s_hostLogger = host.Services.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("HeapScope.Cli");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            host?.Dispose();
            var folder = LogFatal("heapscope-cli could not start", ex);
            await error.WriteLineAsync(StartFailure(ex, folder)).ConfigureAwait(false);
            return 1;
        }

        using (host)
        {
            try
            {
                var exitCode = await parseResult.InvokeAsync(configuration).ConfigureAwait(false);
                if (isHelp)
                {
                    await output.WriteLineAsync(ProductInfo.AttributionWithUrl).ConfigureAwait(false);
                }

                return exitCode;
            }
            finally
            {
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await host.StopAsync(stopTimeout.Token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>What a failed start says: a settings file that is not JSON with its file and line, a value of the wrong kind, else the error.</summary>
    internal static string StartFailure(Exception ex, string? logFolder = null)
    {
        var json = Find<System.Text.Json.JsonException>(ex);
        var file = Find<InvalidDataException>(ex)?.Message is { } loading && loading.IndexOf('\'', StringComparison.Ordinal) is var start and >= 0
            && loading.IndexOf('\'', start + 1) is var end and > 0 ? loading[(start + 1)..end] : null;
        if (json is not null)
        {
            var line = json.LineNumber is { } number ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $", line {number + 1}") : string.Empty;
            return $"HeapScope's settings could not be read: {file ?? "appsettings.json"}{line} is not valid JSON ({json.Message.Split('.')[0]}). In JSON, write each \\ in a path twice, as in \"C:\\\\dumps\". Fix the file, then run the command again.";
        }

        if (ex is InvalidOperationException { Message: var message } && message.Contains("configuration value", StringComparison.Ordinal))
        {
            return $"A setting has a value HeapScope cannot use: {message} Fix it in appsettings.json or the environment, then run the command again.";
        }

        return $"HeapScope could not start: {ex.GetType().Name}: {ex.Message} Details are in HeapScope's log{(logFolder is null ? string.Empty : $" in {logFolder}")}.";
    }

    private static T? Find<T>(Exception? ex)
        where T : Exception
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is T found)
            {
                return found;
            }
        }

        return null;
    }

    // The command line of this run, for the log lines written when there is no host (a failed start, a crash).
    private static System.CommandLine.ParseResult? s_parsed;

    // The host's logger once it started: a later failure goes through its own writer, which a second writer would overwrite.
    private static Microsoft.Extensions.Logging.ILogger? s_hostLogger;

    /// <summary>This run's command line as a log line describes it: a filter's text only with <paramref name="includeFilterText"/>.</summary>
    internal static string Arguments(bool includeFilterText, System.CommandLine.ParseResult? parsed = null) => (parsed ?? s_parsed) is { } line ? CliCommands.DescribeTokens(line, includeFilterText) : "not parsed";

    /// <summary>
    /// A failure in the file log: through the host's logger once it started, else (a failed start) a writer of its own in the folder of
    /// <c>Logging__File__Directory</c>, else the default, which it returns. The arguments are described as every command's are, a filter's
    /// text only when the setting allows it (never the raw command line).
    /// </summary>
    internal static string? LogFatal(string what, Exception exception, bool critical = true)
    {
        if (s_hostLogger is { } host)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.Log(host, critical ? Microsoft.Extensions.Logging.LogLevel.Critical : Microsoft.Extensions.Logging.LogLevel.Error, exception, "{What}", what);
            return null;
        }

        try
        {
            using var provider = new HeapScope.Application.Logging.FileLoggerProvider(Environment.GetEnvironmentVariable("Logging__File__Directory"), app: "cli");
            var includeFilterText = HeapScope.Application.Settings.SettingsService.FilterTextInLogsSaved;
            var arguments = Arguments(includeFilterText);
            Microsoft.Extensions.Logging.LoggerExtensions.LogCritical(provider.CreateLogger("HeapScope.Cli"), exception, "{What}; arguments {Arguments}", what, arguments);
            return provider.Directory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The message on stderr is what is left.
            return null;
        }
    }
}
