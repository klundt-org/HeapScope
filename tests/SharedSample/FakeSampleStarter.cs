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

using System.Collections.Concurrent;
using System.Text.Json;
using HeapScope.Application.Tutorial;

namespace HeapScope.Tests.Shared;

/// <summary>
/// The sample as tests see it (sprint 3.13 review L4: one fake instead of three): <c>--list</c> prints <see cref="Catalog"/>,
/// a start prints READY unless told to fail, hang or not start at all, and every process can be killed, exit on its own and be
/// found by pid with its start time, as the real starter would.
/// </summary>
internal sealed class FakeSampleStarter : ISampleProcessStarter
{
    private int _nextId = 4000;

    /// <summary>A catalog of four scenarios, two of which conflict.</summary>
    public static string DefaultCatalog { get; } = CatalogJson(
        new SampleScenario("StaticCache", "A static cache", "p", "StaticCache", "Fixtures.Leaks.StaticCacheEntry", [], null, ["Fixtures.Leaks.EntryMetadata"], true),
        new SampleScenario("Timer", "Timers", "p", "Timer", "Fixtures.Leaks.TimerTarget", [], null, ["Fixtures.Leaks.TimerLeak"], true),
        new SampleScenario("Waste", "Waste", "p", "Waste", "Fixtures.Leaks.WasteItem", ["FinalizerBacklog"], "both block the finalizer thread", ["Fixtures.Leaks.WasteHolder"], false),
        new SampleScenario("FinalizerBacklog", "Backlog", "p", "FinalizerBacklog", "Fixtures.Leaks.FinalizableItem", ["Waste"], "both block the finalizer thread", [], false));

    /// <summary>What <c>--list</c> prints (null: nothing, as a sample that cannot run).</summary>
    public string? Catalog { get; set; } = DefaultCatalog;

    public ConcurrentQueue<(IReadOnlyList<string> Arguments, FakeSampleProcess Process)> Started { get; } = new();

    /// <summary>The next start exits at once with this message on its standard error.</summary>
    public string? NextExitsWith { get; set; }

    /// <summary>The next start never prints READY (until it is killed).</summary>
    public bool NextHangs { get; set; }

    /// <summary>Hanging reads ignore cancellation and end only when the process is killed, as a real pipe's may.</summary>
    public bool ReadsIgnoreCancellation { get; set; }

    /// <summary>The next start throws this (a blocked or missing executable).</summary>
    public Exception? NextThrows { get; set; }

    /// <summary>Processes that run without this starter having started them (another HeapScope's samples, a reused pid).</summary>
    public ConcurrentDictionary<int, DateTime> Others { get; } = new();

    public FakeSampleProcess Last => Started.Last().Process;

    public static string CatalogJson(params SampleScenario[] scenarios) =>
        JsonSerializer.Serialize(new SampleCatalog(1, scenarios));

    public ISampleProcess Start(string fileName, IReadOnlyList<string> arguments)
    {
        if (NextThrows is { } failure)
        {
            NextThrows = null;
            throw failure;
        }

        var id = Interlocked.Increment(ref _nextId);
        FakeSampleProcess process;
        if (arguments.Contains("--list"))
        {
            process = new FakeSampleProcess(id, Catalog is null ? [] : [Catalog], null, hangs: false);
        }
        else
        {
            process = NextExitsWith is { } error
                ? new FakeSampleProcess(id, [], error, hangs: false)
                : new FakeSampleProcess(id, ["planting", $"READY pid={id}"], null, NextHangs) { IgnoresCancellation = ReadsIgnoreCancellation };
            NextExitsWith = null;
            NextHangs = false;
        }

        Started.Enqueue((arguments, process));
        return process;
    }

    public DateTime? StartTimeUtcOf(int processId) =>
        Others.TryGetValue(processId, out var started) ? started
        : Started.Select(s => s.Process).FirstOrDefault(p => p.Id == processId && !p.HasExited)?.StartTimeUtc;

    public void Kill(int processId)
    {
        Others.TryRemove(processId, out _);
        Started.Select(s => s.Process).FirstOrDefault(p => p.Id == processId)?.Kill();
    }
}

/// <summary>A fake sample process: prints its lines, then waits until it is killed or exits.</summary>
internal sealed class FakeSampleProcess : ISampleProcess
{
    private readonly IReadOnlyList<string> _lines;
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool _hangs;
    private int _next;

    public FakeSampleProcess(int id, IReadOnlyList<string> lines, string? error, bool hangs)
    {
        Id = id;
        _lines = lines;
        _hangs = hangs;
        Error = error ?? string.Empty;
        StartTimeUtc = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc).AddSeconds(id);
        if (error is not null)
        {
            ExitCode = 64;
            _exited.TrySetResult();
        }
    }

    public event EventHandler? Exited;

    public int Id { get; }

    public DateTime StartTimeUtc { get; }

    public bool HasExited => _exited.Task.IsCompleted;

    public int? ExitCode { get; private set; }

    public string Error { get; set; }

    public bool Killed { get; private set; }

    public bool IgnoresCancellation { get; init; }

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        if (_next < _lines.Count && !(_hangs && _next > 0))
        {
            return _lines[_next++];
        }

        if (HasExited || !_hangs)
        {
            return null;
        }

        // A hanging sample: the read ends only when it is killed (or, if the pipe honors it, canceled).
        await (IgnoresCancellation ? _exited.Task : _exited.Task.WaitAsync(cancellationToken)).ConfigureAwait(false);
        return null;
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken) => _exited.Task.WaitAsync(cancellationToken);

    /// <summary>Exits by itself, as a crash or Task Manager would.</summary>
    public void Exit(int code = -1, string? error = null)
    {
        Error = error ?? Error;
        Finish(code);
    }

    public void Kill()
    {
        Killed = true;
        Finish(-1);
    }

    public void Dispose()
    {
    }

    private void Finish(int code)
    {
        if (_exited.TrySetResult())
        {
            ExitCode = code;
            Exited?.Invoke(this, EventArgs.Empty);
        }
    }
}
