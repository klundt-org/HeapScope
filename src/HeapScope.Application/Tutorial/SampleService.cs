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

using System.Globalization;
using System.Text.Json;
using HeapScope.Application.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeapScope.Application.Tutorial;

/// <summary>
/// The tutorial's sample (sprint 3.13, ADR-042): reads the shipped sample's scenario catalog, starts a sample with the chosen
/// scenarios as the user, marks its process as a sample and stops it. Refused while this process runs elevated (the sample would
/// be elevated too) and above <see cref="MaxRunning"/> at once; every sample exits with the process that started it (it watches
/// this process's pid), and the ones still running are stopped when this service is disposed.
/// </summary>
public sealed class SampleService : IDisposable
{
    /// <summary>The most samples that run at once.</summary>
    public const int MaxRunning = 3;

    /// <summary>The interval of a growing sample's step.</summary>
    public static readonly TimeSpan GrowEvery = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan s_readyTimeout = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions s_json = new() { PropertyNameCaseInsensitive = true };

    private readonly ISampleProcessStarter _starter;
    private readonly Func<bool> _isElevated;
    private readonly Func<SampleLaunch?> _resolve;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly List<(RunningSample Sample, ISampleProcess Process)> _running = [];
    private IReadOnlyList<SampleScenario>? _catalog;

    public SampleService(ISampleProcessStarter starter, Func<bool> isElevated, Func<SampleLaunch?>? resolve = null, ILogger<SampleService>? logger = null)
    {
        _starter = starter ?? throw new ArgumentNullException(nameof(starter));
        _isElevated = isElevated ?? throw new ArgumentNullException(nameof(isElevated));
        _resolve = resolve ?? (() => SampleLaunch.Resolve(AppContext.BaseDirectory, WorkerHostOptions.RunningDotnetHost(), File.Exists));
        _logger = logger ?? NullLogger<SampleService>.Instance;
    }

    /// <summary>Raised when the samples running change (one started, stopped or exited).</summary>
    public event EventHandler? Changed;

    /// <summary>Why no sample can start here, or null: it is not in this installation, or this process runs elevated.</summary>
    public string? UnavailableReason => _resolve() is null
        ? "The sample is not in this installation. Reinstall HeapScope from its release archive."
        : _isElevated()
            ? "HeapScope runs as administrator, so the sample would too. Start HeapScope without administrator rights to run it."
            : null;

    /// <summary>The samples running now (the ones that exited are dropped).</summary>
    public IReadOnlyList<RunningSample> Running
    {
        get
        {
            lock (_gate)
            {
                var exited = _running.RemoveAll(r => r.Process.HasExited);
                if (exited > 0)
                {
                    _logger.LogInformation("{Count} samples exited on their own", exited);
                }

                return [.. _running.Select(r => r.Sample)];
            }
        }
    }

    /// <summary>Whether <paramref name="processId"/> is a sample this process started and that still runs.</summary>
    public bool IsSample(int processId) => Running.Any(s => s.ProcessId == processId);

    /// <summary>The sample's scenarios, read once from the sample itself.</summary>
    /// <exception cref="SampleRefusedException">The sample is not here or could not list its scenarios.</exception>
    public async Task<IReadOnlyList<SampleScenario>> ListScenariosAsync(CancellationToken cancellationToken)
    {
        if (_catalog is { } known)
        {
            return known;
        }

        var launch = _resolve() ?? throw new SampleRefusedException(UnavailableReason!);
        using var process = _starter.Start(launch.FileName, [.. launch.Prefix, "--list"]);
        var json = await process.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _catalog = JsonSerializer.Deserialize<List<SampleScenario>>(json ?? "[]", s_json) ?? [];
        }
        catch (JsonException ex)
        {
            throw new SampleRefusedException("The sample could not list its scenarios. Reinstall HeapScope from its release archive.", ex);
        }

        _logger.LogInformation("The sample at {File} lists {Count} scenarios: {Names}", launch.SampleFile, _catalog.Count, string.Join(", ", _catalog.Select(s => s.Name)));
        return _catalog;
    }

    /// <summary>
    /// Starts a sample with <paramref name="scenarios"/>, growing when <paramref name="grow"/>, and returns once it is ready.
    /// </summary>
    /// <exception cref="SampleRefusedException">Why it cannot start, with what to do.</exception>
    public async Task<RunningSample> StartAsync(IReadOnlyList<string> scenarios, bool grow, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scenarios);
        if (UnavailableReason is { } unavailable)
        {
            throw Refused(unavailable);
        }

        if (scenarios.Count == 0)
        {
            throw Refused("Choose at least one scenario.");
        }

        var catalog = await ListScenariosAsync(cancellationToken).ConfigureAwait(false);
        var chosen = new List<SampleScenario>();
        foreach (var name in scenarios)
        {
            var scenario = catalog.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw Refused($"There is no scenario {name} in this HeapScope's sample.");
            chosen.Add(scenario);
        }

        foreach (var scenario in chosen)
        {
            if (chosen.FirstOrDefault(other => scenario.CannotRunWith.Contains(other.Name, StringComparer.OrdinalIgnoreCase)) is { } other)
            {
                throw Refused($"{scenario.Name} and {other.Name} cannot run together: both block the finalizer thread. Choose one.");
            }
        }

        if (Running.Count >= MaxRunning)
        {
            throw Refused($"{MaxRunning} samples are running. Stop one first.");
        }

        var launch = _resolve()!;
        var names = chosen.Select(s => s.Name).ToList();
        List<string> arguments = [.. launch.Prefix, "--scenarios", string.Join(',', names), "--parent-pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture)];
        if (grow)
        {
            arguments.AddRange(["--grow-every", ((int)GrowEvery.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)]);
        }

        var process = _starter.Start(launch.FileName, arguments);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(s_readyTimeout);
            while (await process.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line)
            {
                if (line.StartsWith("READY pid=", StringComparison.Ordinal))
                {
                    var sample = new RunningSample(process.Id, names, grow, DateTimeOffset.UtcNow);
                    lock (_gate)
                    {
                        _running.Add((sample, process));
                    }

                    _logger.LogInformation("Sample {Pid} started: {Scenarios}, growing {Grows}", process.Id, string.Join(", ", names), grow);
                    Changed?.Invoke(this, EventArgs.Empty);
                    return sample;
                }
            }

            throw Refused($"The sample did not start: {(process.Error is { Length: > 0 } error ? error : "it exited without a word")}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill();
            process.Dispose();
            throw Refused($"The sample did not get ready within {s_readyTimeout.TotalSeconds:N0} seconds and was stopped.");
        }
        catch
        {
            process.Kill();
            process.Dispose();
            throw;
        }
    }

    /// <summary>Stops the sample <paramref name="processId"/>; false when it is not one this process started.</summary>
    public bool Stop(int processId)
    {
        ISampleProcess? process;
        lock (_gate)
        {
            var index = _running.FindIndex(r => r.Sample.ProcessId == processId);
            if (index < 0)
            {
                return false;
            }

            process = _running[index].Process;
            _running.RemoveAt(index);
        }

        process.Kill();
        process.Dispose();
        _logger.LogInformation("Sample {Pid} stopped", processId);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Dispose()
    {
        foreach (var sample in Running)
        {
            Stop(sample.ProcessId);
        }
    }

    private SampleRefusedException Refused(string reason)
    {
        _logger.LogInformation("Sample refused: {Reason}", reason);
        return new SampleRefusedException(reason);
    }
}
