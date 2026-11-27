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

namespace HeapScope.Application.Workers;

/// <summary>
/// The one reopen protocol for what the worker holds for a client: a call runs with the current worker
/// ids of its resources; a resource from an earlier worker generation is reopened first, and every resource once more
/// when the worker answers <c>notLoaded</c>. Concurrent callers reopen a resource once: a caller that finds it reopened
/// since it asked uses the new id. A closed resource is never reopened.
/// </summary>
internal sealed class WorkerResources<T>(
    IWorkerClient worker,
    HeapScopeErrorCode notLoaded,
    Func<T, CancellationToken, Task> reopen,
    Action<T> throwIfClosed)
    where T : class, IWorkerResource
{
    /// <summary>Runs <paramref name="call"/> with the worker ids of <paramref name="resources"/>, in their order.</summary>
    public async Task<TResult> UseAsync<TResult>(
        IReadOnlyList<T> resources,
        Func<IWorkerService, IReadOnlyList<Guid>, CancellationToken, Task<TResult>> call,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(call);
        foreach (var resource in resources)
        {
            throwIfClosed(resource);
        }

        await worker.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        var distinct = resources.Distinct().ToList();
        foreach (var resource in distinct)
        {
            if (resource.Generation != worker.Generation)
            {
                await ReopenOnceAsync(resource, resource.WorkerId, cancellationToken).ConfigureAwait(false);
            }
        }

        var asked = distinct.ToDictionary(r => r, r => r.WorkerId);
        try
        {
            return await worker.InvokeAsync((service, ct) => call(service, [.. resources.Select(r => asked[r])], ct), cancellationToken).ConfigureAwait(false);
        }
        catch (WorkerOperationException ex) when (ex.ErrorCode == notLoaded)
        {
            foreach (var resource in distinct)
            {
                throwIfClosed(resource);
                await ReopenOnceAsync(resource, asked[resource], cancellationToken).ConfigureAwait(false);
            }

            return await worker.InvokeAsync((service, ct) => call(service, [.. resources.Select(r => r.WorkerId)], ct), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs <paramref name="call"/>, which asks the worker itself, with the resource's worker id: for a question about resources of
    /// two kinds. Reopened before and once after <c>notLoaded</c>, as in <see cref="UseAsync"/>. What fails about the
    /// resource itself is thrown as <paramref name="unavailable"/> makes it.
    /// </summary>
    public async Task<TResult> UseIdAsync<TResult>(T resource, Func<Guid, CancellationToken, Task<TResult>> call, Func<WorkerException, Exception> unavailable, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(unavailable);
        await ReadyAsync(async () =>
        {
            throwIfClosed(resource);
            await worker.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            if (resource.Generation != worker.Generation)
            {
                await ReopenOnceAsync(resource, resource.WorkerId, cancellationToken).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        var asked = resource.WorkerId;
        try
        {
            return await call(asked, cancellationToken).ConfigureAwait(false);
        }
        catch (WorkerOperationException ex) when (ex.ErrorCode == notLoaded)
        {
            await ReadyAsync(async () =>
            {
                throwIfClosed(resource);
                await ReopenOnceAsync(resource, asked, cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
            return await call(resource.WorkerId, cancellationToken).ConfigureAwait(false);
        }

        async Task ReadyAsync(Func<Task> ready)
        {
            try
            {
                await ready().ConfigureAwait(false);
            }
            catch (WorkerException ex)
            {
                throw unavailable(ex);
            }
        }
    }

    private async Task ReopenOnceAsync(T resource, Guid missing, CancellationToken cancellationToken)
    {
        await resource.Reopening.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (resource.WorkerId != missing && resource.Generation == worker.Generation)
            {
                return;
            }

            await reopen(resource, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            resource.Reopening.Release();
        }
    }
}
