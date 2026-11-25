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


using HeapScope.Application.Workers;
using HeapScope.Contracts;

namespace HeapScope.Application.Tests;

/// <summary>The one reopen protocol for snapshots and traces (sprint 3.8, peer review P5).</summary>
public class WorkerResourcesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Resources_of_an_earlier_worker_are_reopened_once_each_before_the_call()
    {
        var worker = new Client { Generation = 2 };
        var reopened = new List<Resource>();
        var resources = Protocol(worker, (r, _) => { reopened.Add(r); r.Reopen(worker.Generation); return Task.CompletedTask; });
        var a = new Resource(1);
        var b = new Resource(1);

        var ids = await resources.UseAsync([a, b, a], (_, i, _) => Task.FromResult(i), Ct);

        reopened.ShouldBe([a, b]);
        ids.ShouldBe([a.WorkerId, b.WorkerId, a.WorkerId]);
    }

    [Fact]
    public async Task A_not_loaded_answer_reopens_every_resource_and_asks_again()
    {
        var worker = new Client { Generation = 1 };
        var reopens = 0;
        var resources = Protocol(worker, (r, _) => { reopens++; r.Reopen(worker.Generation); return Task.CompletedTask; });
        var a = new Resource(1);
        var b = new Resource(1);
        var first = (a.WorkerId, b.WorkerId);
        var calls = 0;

        var ids = await resources.UseAsync([a, b], (_, i, _) => ++calls == 1 ? throw NotLoaded() : Task.FromResult(i), Ct);

        reopens.ShouldBe(2);
        calls.ShouldBe(2);
        ids.ShouldBe([a.WorkerId, b.WorkerId]);
        (a.WorkerId, b.WorkerId).ShouldNotBe(first);
    }

    /// <summary>Sprint 4.4: a question about two kinds of resources gets one's id, reopened first and after "not loaded" like any other.</summary>
    [Fact]
    public async Task A_call_that_asks_the_worker_itself_gets_a_current_id()
    {
        var worker = new Client { Generation = 2 };
        var reopens = 0;
        var resources = Protocol(worker, (r, _) => { reopens++; r.Reopen(worker.Generation); return Task.CompletedTask; });
        var a = new Resource(1);
        var asked = new List<Guid>();

        var id = await resources.UseIdAsync(a, (i, _) => { asked.Add(i); return asked.Count == 1 ? throw NotLoaded() : Task.FromResult(i); }, ex => ex, Ct);

        (reopens, id).ShouldBe((2, a.WorkerId));
        asked.Distinct().Count().ShouldBe(2);
    }

    /// <summary>What fails about the resource is told apart from what fails about the call.</summary>
    [Fact]
    public async Task A_resource_that_cannot_be_reopened_is_told_apart()
    {
        var worker = new Client { Generation = 2 };
        var resources = Protocol(worker, (_, _) => Task.FromException(new WorkerOperationException(HeapScopeErrorCode.InsufficientMemory, "no memory", null)));

        (await Should.ThrowAsync<HeapScope.Application.Allocations.AllocationsUnavailableException>(() => resources.UseIdAsync(
            new Resource(1), (i, _) => Task.FromResult(i), ex => new HeapScope.Application.Allocations.AllocationsUnavailableException(ex.Message, ex), Ct))).Message.ShouldBe("no memory");
        (await Should.ThrowAsync<WorkerOperationException>(() => resources.UseIdAsync<Guid>(
            new Resource(2), (_, _) => throw new WorkerOperationException(HeapScopeErrorCode.InvalidArgument, "the call", null), ex => new HeapScope.Application.Allocations.AllocationsUnavailableException(ex.Message, ex), Ct))).Message.ShouldBe("the call");
    }

    [Fact]
    public async Task Concurrent_callers_reopen_a_resource_once()
    {
        var worker = new Client { Generation = 2 };
        var reopens = 0;
        var resources = Protocol(worker, async (r, ct) =>
        {
            Interlocked.Increment(ref reopens);
            await Task.Delay(50, ct);
            r.Reopen(worker.Generation);
        });
        var shared = new Resource(1);

        var answers = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => resources.UseAsync([shared], (_, i, _) => Task.FromResult(i[0]), Ct)));

        reopens.ShouldBe(1);
        answers.ShouldAllBe(id => id == shared.WorkerId);
    }

    [Fact]
    public async Task A_closed_resource_is_never_reopened()
    {
        var worker = new Client { Generation = 2 };
        var reopens = 0;
        var resources = Protocol(worker, (_, _) => { reopens++; return Task.CompletedTask; });
        var closed = new Resource(1) { IsClosed = true };

        await Should.ThrowAsync<WorkerOperationException>(() => resources.UseAsync([closed], (_, i, _) => Task.FromResult(i), Ct));

        reopens.ShouldBe(0);
    }

    private static WorkerResources<Resource> Protocol(Client worker, Func<Resource, CancellationToken, Task> reopen) =>
        new(worker, HeapScopeErrorCode.SnapshotNotLoaded, reopen, r =>
        {
            if (r.IsClosed)
            {
                throw new WorkerOperationException(HeapScopeErrorCode.SnapshotNotLoaded, "closed", null);
            }
        });

    private static WorkerOperationException NotLoaded() => new(HeapScopeErrorCode.SnapshotNotLoaded, "gone", null);

    private sealed class Resource(int generation) : IWorkerResource
    {
        public string Path => "app.dmp";

        public bool IsClosed { get; init; }

        public int Generation { get; private set; } = generation;

        public Guid WorkerId { get; private set; } = Guid.NewGuid();

        public SemaphoreSlim Reopening { get; } = new(1, 1);

        public void Reopen(int generation)
        {
            Generation = generation;
            WorkerId = Guid.NewGuid();
        }
    }

    private sealed class Client : IWorkerClient
    {
        public WorkerState State => WorkerState.Running;

        public WorkerInfo? Info => null;

        public int Generation { get; init; }

        public event EventHandler<WorkerStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public Task<WorkerInfo> EnsureStartedAsync(CancellationToken cancellationToken) => Task.FromResult<WorkerInfo>(null!);

        public Task<T> InvokeAsync<T>(Func<IWorkerService, CancellationToken, Task<T>> call, CancellationToken cancellationToken) => call(null!, cancellationToken);

        public Task InvokeAsync(Func<IWorkerService, CancellationToken, Task> call, CancellationToken cancellationToken) => call(null!, cancellationToken);

        public Task RestartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
