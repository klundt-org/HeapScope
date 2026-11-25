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
using Microsoft.Extensions.Logging;

namespace HeapScope.Application.Allocations;

/// <summary>
/// Traces followed while they are recorded (sprint 4.3, ADR-037): the worker tails the file, and the queries of a finished trace
/// answer for what it read so far. A live trace is the Live page's own: it is not among <see cref="Traces"/>.
/// </summary>
public sealed partial class AllocationSession
{
    /// <summary>Starts following the trace of a recording that has started.</summary>
    /// <param name="namesPath">What names the methods compiled before, if recorded.</param>
    public async Task<TraceHandle> OpenLiveAsync(string path, string? namesPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var fullPath = System.IO.Path.GetFullPath(path);
        await worker.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        var generation = worker.Generation;
        var opened = await worker.InvokeAsync((service, ct) => service.OpenLiveTraceAsync(new OpenLiveTraceRequest(fullPath, namesPath), ct), cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Following the recording {Path} as worker trace {WorkerTraceId} (worker generation {Generation})", fullPath, opened.TraceId, generation);
        return new TraceHandle(Describe(opened), generation, new LiveTraceSource(namesPath));
    }

    /// <summary>
    /// Where a live trace stands; the worker follows it again when it restarted. Fails with
    /// <see cref="HeapScopeErrorCode.TraceChanged"/> when the file is another recording by now.
    /// </summary>
    public async Task<LiveTraceDto> QueryLiveAsync(TraceHandle handle, CancellationToken cancellationToken)
    {
        var live = LiveOf(handle);
        var state = await UseAsync(handle, (service, id, ct) => service.QueryLiveTraceAsync(new LiveTraceRequest(id), ct), cancellationToken).ConfigureAwait(false);
        if (!live.IsSame(state))
        {
            _logger.LogInformation("Not using {Path}: it is another recording now, started {Start:O}", handle.Path, state.StartUtc);
            handle.FileChanged = true;
            throw Changed(handle);
        }

        return state;
    }

    /// <summary>The recording ended and its file is closed.</summary>
    public async Task EndLiveAsync(TraceHandle handle, CancellationToken cancellationToken)
    {
        // First: a worker that follows it again from here on is told when it does.
        LiveOf(handle).Ended = true;
        await UseAsync(handle, async (service, id, ct) =>
        {
            await service.EndLiveTraceAsync(id, ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops following a trace. The recording and its file are not touched.</summary>
    public async Task CloseLiveAsync(TraceHandle handle, CancellationToken cancellationToken)
    {
        _ = LiveOf(handle);
        if (handle.IsClosed)
        {
            return;
        }

        handle.IsClosed = true;
        if (handle.Generation == worker.Generation && worker.State == Workers.WorkerState.Running)
        {
            await worker.InvokeAsync((service, ct) => service.CloseTraceAsync(handle.WorkerTraceId, ct), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReopenLiveAsync(TraceHandle handle, LiveTraceSource live, CancellationToken cancellationToken)
    {
        var generation = worker.Generation;
        _logger.LogInformation("Following {Path} again (worker trace {WorkerTraceId} of generation {Generation} is gone; the worker is now generation {Now})", handle.Path, handle.WorkerTraceId, handle.Generation, generation);
        var opened = await worker.InvokeAsync((service, ct) => service.OpenLiveTraceAsync(new OpenLiveTraceRequest(handle.Path, live.NamesPath), ct), cancellationToken).ConfigureAwait(false);
        handle.Update(Describe(opened), generation);
        if (live.Ended)
        {
            await worker.InvokeAsync((service, ct) => service.EndLiveTraceAsync(opened.TraceId, ct), cancellationToken).ConfigureAwait(false);
        }

        if (handle.IsClosed)
        {
            // Closed while this ran: CloseLiveAsync saw the old generation and closed nothing.
            await worker.InvokeAsync((service, ct) => service.CloseTraceAsync(opened.TraceId, ct), CancellationToken.None).ConfigureAwait(false);
            ThrowIfClosed(handle);
        }
    }

    private static LiveTraceSource LiveOf(TraceHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return handle.Live ?? throw new ArgumentException("The trace is not a live one.", nameof(handle));
    }

    /// <summary>A live trace's handle has nothing of the trace yet: its state is asked for.</summary>
    private static TraceDescriptor Describe(LiveTraceDescriptor live) => new(live.TraceId, live.Path, 0, null, default, 0, 0, 0, 0, 0, 0, 0);
}
