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

using HeapScope.Contracts;
using HeapScope.Core.Allocations;
using HeapScope.Loaders.Trace;
using Microsoft.Extensions.Logging;

namespace HeapScope.Worker.Traces;

/// <summary>A trace the worker follows while it is recorded (sprint 4.3, ADR-037), with the memory reserved for it.</summary>
internal sealed class LiveTrace : IDisposable
{
    /// <summary>How often the log gets where a live trace stands.</summary>
    private static readonly TimeSpan s_logEvery = TimeSpan.FromMinutes(1);

    private readonly LiveTraceReader _reader;
    private readonly IDisposable _reservation;
    private readonly ILogger _logger;
    private readonly Timer _log;
    private OpenTrace? _view;
    private int _disposed;

    public LiveTrace(Guid id, string path, string? namesPath, IDisposable reservation, ILogger logger)
    {
        Id = id;
        Path = path;
        _reservation = reservation;
        _logger = logger;
        _reader = LiveTraceReader.Start(path, namesPath);
        _log = new Timer(_ => Log("so far", LogLevel.Debug), null, s_logEvery, s_logEvery);
        _ = CompleteAsync();
    }

    public Guid Id { get; }

    public string Path { get; }

    /// <summary>The recording ended: the reader reads what is left.</summary>
    public void End() => _reader.End();

    /// <summary>What was read so far, for the queries; the same object until more is.</summary>
    public OpenTrace View()
    {
        var trace = _reader.View();
        var known = Volatile.Read(ref _view);
        if (known is null || !ReferenceEquals(known.Trace, trace))
        {
            known = new OpenTrace(Path, trace, isLive: true);
            Volatile.Write(ref _view, known);
        }

        return known;
    }

    public LiveTraceDto State()
    {
        var s = _reader.Status();
        var (processId, processName, start) = _reader.Source();
        return new LiveTraceDto(
            s.Version,
            processId,
            processName,
            start == default ? null : new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)),
            s.DurationMs / 1000,
            s.BucketMs / 1000,
            s.Samples,
            s.Bytes,
            s.Types,
            s.LostEvents,
            s.SamplesWithoutStack,
            s.SamplesUnnamed,
            s.SamplesPastLimits,
            s.DelayMs / 1000,
            s.CaughtUp,
            s.Completed,
            s.StackProblem is null,
            s.Problem);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _log.Dispose();
            _reader.Dispose();
            _reservation.Dispose();
        }
    }

    private async Task CompleteAsync()
    {
        await _reader.Completion.ConfigureAwait(false);
        if (_reader.Failure is { } failure)
        {
            _logger.LogError(failure, "Following the trace {Id} ({Path}) failed", Id, Path);
        }

        await _log.DisposeAsync().ConfigureAwait(false);
        Log("at its end", LogLevel.Information);

        // What it keeps is part of the worker's memory from here on, as an open trace's is.
        _reservation.Dispose();
    }

    /// <summary>What a figure shown while recording rests on: the rules, how far the file was read, and what was left out.</summary>
    private void Log(string when, LogLevel level)
    {
        // The delay is the page's to ask for: not asked here.
        var s = _reader.Peek();
        _logger.Log(
            level,
            "Live trace {Id} {When}: {Samples} samples of {Bytes} bytes in {Types} types over {Seconds:N1} s, stretches of {Bucket:N0} ms, {Entries} entries, {Nodes} stack nodes, {Methods} methods ({Named} named before), {Lost} lost events, {NoStack} without a stack, {Unnamed} with unnamed frames, {Past} past the limits, {Read} bytes read; stacks {Stacks}, names {Names}; {Checked}; {Problem} {Detail}",
            Id,
            when,
            s.Samples,
            s.Bytes,
            s.Types,
            s.DurationMs / 1000,
            s.BucketMs,
            s.Entries,
            s.StackNodes,
            s.Methods,
            s.NamedBefore,
            s.LostEvents,
            s.SamplesWithoutStack,
            s.SamplesUnnamed,
            s.SamplesPastLimits,
            s.BytesRead,
            s.StackProblem ?? "read",
            s.NamesProblem ?? "read",
            s.Checked ?? "check not ended",
            s.Problem ?? "no problem",
            s.ProblemDetail ?? string.Empty);
    }
}
