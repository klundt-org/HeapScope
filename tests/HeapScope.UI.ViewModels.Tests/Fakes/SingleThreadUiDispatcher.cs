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

using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.ComponentModel;
using HeapScope.UI.Services;
using HeapScope.UI.ViewModels;

namespace HeapScope.UI.ViewModels.Tests;

/// <summary>
/// A UI thread for view model tests: one thread of its own with a synchronization context, so a test body run through
/// <see cref="RunAsync"/> and every await in it continues there, as on the app's UI thread. <see cref="Watch"/> records any
/// property or collection change raised on another thread: a view model that changes its state off the UI thread would
/// break the real app's bindings, and <see cref="ImmediateUiDispatcher"/> cannot see it.
/// </summary>
internal sealed class SingleThreadUiDispatcher : IUiDispatcher, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
    private readonly Thread _thread;
    private readonly List<string> _violations = [];
    private readonly HashSet<SnapshotWorkspaceViewModel> _workspaces = [];

    public SingleThreadUiDispatcher()
    {
        _thread = new Thread(Pump) { IsBackground = true, Name = "Test UI thread" };
        _thread.Start();
    }

    /// <summary>What changed off the UI thread, with where; empty when every change was on it.</summary>
    public IReadOnlyList<string> Violations
    {
        get
        {
            lock (_violations)
            {
                return [.. _violations];
            }
        }
    }

    public bool IsOnUiThread => Thread.CurrentThread == _thread;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsOnUiThread)
        {
            action();
        }
        else
        {
            Enqueue(_ => action(), null);
        }
    }

    public async Task YieldToRenderAsync()
    {
        if (IsOnUiThread)
        {
            await Task.Yield();
        }
    }

    /// <summary>Runs <paramref name="body"/> on the UI thread, its awaits included, and completes when it does.</summary>
    public Task RunAsync(Func<Task> body)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add((async _ =>
        {
            try
            {
                await body();
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        }, null));
        return done.Task;
    }

    /// <summary>Runs <paramref name="body"/> on the UI thread and waits for its value (from a test's own thread).</summary>
    public T Invoke<T>(Func<T> body)
    {
        var result = default(T)!;
        RunAsync(() =>
        {
            result = body();
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
        return result;
    }

    /// <summary>Records a change of <paramref name="source"/> raised off the UI thread, named <paramref name="name"/>.</summary>
    public void Watch(object source, string name)
    {
        if (source is INotifyPropertyChanged properties)
        {
            properties.PropertyChanged += (_, e) => Check($"{name}.{e.PropertyName}");
        }

        if (source is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += (_, e) => Check($"{name} ({e.Action})");
        }
    }

    /// <summary>Runs <paramref name="body"/> on the UI thread, then fails when anything watched changed off it.</summary>
    public async Task RunCheckedAsync(Func<Task> body)
    {
        await RunAsync(body);
        Violations.ShouldBeEmpty();
    }

    /// <summary>Watches the Snapshots page, its list, and the views of every workspace it shows.</summary>
    public void WatchPage(SnapshotsViewModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        Watch(page, "page");
        Watch(page.Snapshots, "page.Snapshots");
        WatchWorkspace(page.Current);
        page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SnapshotsViewModel.Current))
            {
                WatchWorkspace(page.Current);
            }
        };
    }

    public void Dispose() => _queue.CompleteAdding();

    private void WatchWorkspace(SnapshotWorkspaceViewModel workspace)
    {
        lock (_workspaces)
        {
            if (!_workspaces.Add(workspace))
            {
                return;
            }
        }

        var name = $"workspace {System.IO.Path.GetFileName(workspace.Snapshot?.Path) ?? "empty"}";
        Watch(workspace.Types, name + ".Types");
        Watch(workspace.Navigator, name + ".Navigator");
        Watch(workspace.Inspections, name + ".Inspections");
        Watch(workspace.Inspections.Tiles, name + ".Inspections.Tiles");
        Watch(workspace.Inspections.BaselineChoices, name + ".Inspections.BaselineChoices");
        Watch(workspace.Retainers, name + ".Retainers");
    }

    // Work posted after the test ended is dropped, as a closed window's would be.
    private void Enqueue(SendOrPostCallback callback, object? state)
    {
        try
        {
            _queue.Add((callback, state));
        }
        catch (InvalidOperationException) when (_queue.IsAddingCompleted)
        {
        }
    }

    private void Check(string what)
    {
        if (!IsOnUiThread)
        {
            lock (_violations)
            {
                _violations.Add($"{what} changed on thread {Environment.CurrentManagedThreadId} ({Thread.CurrentThread.Name ?? "a pool thread"}):{Environment.NewLine}{Environment.StackTrace}");
            }
        }
    }

    private void Pump()
    {
        SynchronizationContext.SetSynchronizationContext(new Context(this));
        foreach (var (callback, state) in _queue.GetConsumingEnumerable())
        {
            callback(state);
        }
    }

    private sealed class Context(SingleThreadUiDispatcher owner) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            owner.Enqueue(d, state);
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (owner.IsOnUiThread)
            {
                d(state);
            }
            else
            {
                owner.RunAsync(() =>
                {
                    d(state);
                    return Task.CompletedTask;
                }).GetAwaiter().GetResult();
            }
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
