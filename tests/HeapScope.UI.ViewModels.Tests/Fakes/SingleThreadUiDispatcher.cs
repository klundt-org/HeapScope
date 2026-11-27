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
    private readonly HashSet<object> _watched = [];
    private readonly List<Exception> _faults = [];

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

    /// <summary>What work posted to the UI thread threw; the pump goes on, and a checked run fails on it.</summary>
    public IReadOnlyList<Exception> Faults
    {
        get
        {
            lock (_faults)
            {
                return [.. _faults];
            }
        }
    }

    /// <summary>Runs <paramref name="body"/> on the UI thread and waits for its value; on that thread it runs inline.</summary>
    public T Invoke<T>(Func<T> body)
    {
        if (IsOnUiThread)
        {
            return body();
        }

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
        lock (_watched)
        {
            if (!_watched.Add(source))
            {
                return;
            }
        }

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
        Check();
    }

    /// <summary>
    /// Runs what was posted so far, then fails when anything watched changed off the UI thread or posted work threw: for a
    /// test class's end, so work that finished after the test body is checked too.
    /// </summary>
    public void Check()
    {
        Invoke(() => true);
        Violations.ShouldBeEmpty();
        Faults.ShouldBeEmpty();
    }

    /// <summary>Forgets the violations so far: for a test that makes one on purpose.</summary>
    public void ForgetViolations()
    {
        lock (_violations)
        {
            _violations.Clear();
        }
    }

    /// <summary>Watches the Snapshots page, its list, and the views of every workspace it shows.</summary>
    public void WatchPage(SnapshotsViewModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        Watch(page, "page");
        Watch(page.Snapshots, "page.Snapshots");
        Watch(page.Compare, "page.Compare");
        Watch(page.Bookmarks, "page.Bookmarks");
        Watch(page.Bookmarks.Items, "page.Bookmarks.Items");
        WatchWorkspace(page.Current);
        page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SnapshotsViewModel.Current))
            {
                WatchWorkspace(page.Current);
            }
        };
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(5));
    }

    private void WatchWorkspace(SnapshotWorkspaceViewModel workspace)
    {
        var name = $"workspace {System.IO.Path.GetFileName(workspace.Snapshot?.Path) ?? "empty"}";
        Watch(workspace.Types, name + ".Types");
        Watch(workspace.Navigator, name + ".Navigator");
        Watch(workspace.Inspections, name + ".Inspections");
        Watch(workspace.Inspections.Tiles, name + ".Inspections.Tiles");
        Watch(workspace.Inspections.BaselineChoices, name + ".Inspections.BaselineChoices");
        Watch(workspace.Retainers, name + ".Retainers");
        Watch(workspace.Retainers.Set, name + ".Retainers.Set");

        // The subjects of the path: each one shown is watched from then on.
        var navigator = workspace.Navigator;
        Watch(navigator.Current, $"{name}.Navigator.Current ({navigator.Current.GetType().Name})");
        navigator.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NavigatorViewModel.Current) && navigator.Current is { } subject)
            {
                Watch(subject, $"{name}.Navigator.Current ({subject.GetType().Name})");
            }
        };
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
            try
            {
                callback(state);
            }
            catch (Exception ex)
            {
                // Recorded, not thrown: one view model's failure fails its test instead of the test process.
                lock (_faults)
                {
                    _faults.Add(ex);
                }
            }
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
