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

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeapScope.Application.Allocations;
using HeapScope.Application.Presentation;
using HeapScope.Application.Workers;
using HeapScope.Contracts;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// What the process being watched allocates, as it does (sprint 4.3, ADR-037): the worker follows the run's allocation trace, and
/// this asks it a few times a second what changed. The types of the chosen interval are kept up to date in place; call stacks are
/// looked up when asked for. The run's finished trace stays the record.
/// </summary>
public sealed partial class LiveAllocationsViewModel : ObservableObject
{
    /// <summary>Types listed, most allocated first.</summary>
    public const int TopTypes = 20;

    /// <summary>Stacks listed, most allocated first.</summary>
    public const int TopStacks = 20;

    /// <summary>How often the worker is asked where it stands.</summary>
    public static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(500);

    /// <summary>How often what is shown is brought up to date while samples arrive.</summary>
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(2);

    /// <summary>A delay longer than this is said: at best it is a quarter of a second.</summary>
    public const double SaidDelaySeconds = 2;

    /// <summary>Bars of the timeline, at most about this many.</summary>
    private const double Bars = 200;

    private readonly AllocationSession? _session;
    private readonly ILogger _logger;
    private Following? _following;

    /// <summary>Whether a run's live allocations are shown: from its recording's start until another run takes their place.</summary>
    [ObservableProperty]
    private bool _isShown;

    [ObservableProperty]
    private string? _summary;

    /// <summary>How far behind the process the figures are, while that is worth saying.</summary>
    [ObservableProperty]
    private string? _delay;

    /// <summary>What to know of the figures: what was left out, or why they stopped.</summary>
    [ObservableProperty]
    private string? _note;

    [ObservableProperty]
    private IReadOnlyList<AllocationBucketDto> _buckets = [];

    [ObservableProperty]
    private double _bucketSeconds = 1;

    [ObservableProperty]
    private double _durationSeconds;

    /// <summary>The interval chosen on the timeline, or null for the whole run so far.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WholeRunCommand))]
    private TimeInterval? _selection;

    [ObservableProperty]
    private string _interval = WholeRunText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StacksOf))]
    [NotifyCanExecuteChangedFor(nameof(AllTypesCommand))]
    private LiveTypeItem? _selectedType;

    /// <summary>Whether call stacks are asked for (a view of their own, on demand).</summary>
    [ObservableProperty]
    private bool _showStacks;

    [ObservableProperty]
    private string? _stacksStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedFrames))]
    private AllocationStackItem? _selectedStack;

    private const string WholeRunText = "The whole run so far";

    public LiveAllocationsViewModel(AllocationSession? session, ILogger logger)
    {
        _session = session;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The types of the interval, most allocated first; a type keeps its row as figures change.</summary>
    public ObservableCollection<LiveTypeItem> Types { get; } = [];

    /// <summary>The top call stacks of the interval and type, as of when they were asked for.</summary>
    public ObservableCollection<AllocationStackItem> Stacks { get; } = [];

    public IReadOnlyList<string> SelectedFrames => SelectedStack?.Frames ?? [];

    /// <summary>What the stacks are of.</summary>
    public string StacksOf => SelectedType is { } type ? type.Name : "all types";

    /// <summary>How the wait between two questions passes: tests hold it.</summary>
    public Func<TimeSpan, CancellationToken, Task> Wait { get; set; } = static (wait, ct) => Task.Delay(wait, ct);

    /// <summary>How long what is shown stays as it is, at least: tests make it shorter.</summary>
    public TimeSpan Refresh { get; set; } = RefreshEvery;

    /// <summary>The following in progress, for tests and smoke runs; completed without one.</summary>
    public Task FollowTask { get; private set; } = Task.CompletedTask;

    /// <summary>Follows a run's trace from here on, in place of what is shown.</summary>
    public void Follow(string path, string? namesPath)
    {
        Close();
        if (_session is null)
        {
            return;
        }

        var following = new Following();
        _following = following;
        IsShown = true;
        Summary = "Waiting for the first allocation sample.";
        FollowTask = FollowAsync(following, path, namesPath);
    }

    /// <summary>The run's recording ended: what is left of it is read, and the figures stay.</summary>
    public void End()
    {
        if (_following is { } following)
        {
            following.Ended = true;
        }
    }

    /// <summary>Stops following and shows nothing.</summary>
    public void Close()
    {
        if (_following is { } following)
        {
            _following = null;
            following.Stop.Cancel();
        }

        IsShown = false;
        Types.Clear();
        ClearStacks();
        (Summary, Delay, Note, Buckets, BucketSeconds, DurationSeconds) = (null, null, null, [], 1, 0);
        ShowStacks = false;
        SelectedType = null;
        Selection = null;
    }

    private async Task FollowAsync(Following following, string path, string? namesPath)
    {
        var ct = following.Stop.Token;
        TraceHandle? handle = null;
        try
        {
            handle = await _session!.OpenLiveAsync(path, namesPath, ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            following.Trace = handle;
            await PollAsync(following, handle, ct);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // Another run took its place.
        }
        catch (Exception ex) when (ex is WorkerException or OperationCanceledException)
        {
            _logger.LogInformation("The live allocations of {Path} stopped: {Reason}", path, ex.Message);
            Stopped(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The live allocations of {Path} failed", path);
            Stopped("The live allocations failed. Details are in the log.");
        }
        finally
        {
            if (ct.IsCancellationRequested && handle is not null)
            {
                await CloseInWorkerAsync(handle);
            }
        }
    }

    private async Task PollAsync(Following following, TraceHandle handle, CancellationToken ct)
    {
        var told = false;
        while (true)
        {
            try
            {
                if (following.Ended && !told)
                {
                    await _session!.EndLiveAsync(handle, ct);
                    told = true;
                }

                var state = await _session!.QueryLiveAsync(handle, ct);
                following.State = state;
                ShowState(state);

                // Before the first sample there is nothing to show, and what a first sample shows is not kept waiting.
                var due = following.Asked || state.Completed || following.Shown == 0 || Stopwatch.GetElapsedTime(following.Shown) >= Refresh;
                if (due && state.Version > 0 && (state.Version != following.ShownVersion || following.Asked))
                {
                    following.Asked = false;
                    await RefreshAsync(following, handle, state, ct);
                }

                if (state.Completed)
                {
                    _logger.LogInformation(
                        "The live allocations of {Path} ended: {Samples} samples of {Bytes} bytes in {Types} types over {Seconds:N1} s",
                        handle.Path,
                        state.Samples,
                        state.SampledBytes,
                        state.Types,
                        state.DurationSeconds);
                    Delay = null;
                    following.Completed = true;
                    return;
                }
            }
            catch (WorkerCrashedException ex)
            {
                // The worker restarts with the next question and follows the file again, from its start.
                _logger.LogInformation("The live allocations of {Path} wait for the worker: {Reason}", handle.Path, ex.Message);
                told = false;
            }

            await Wait(PollEvery, ct);
        }
    }

    private void ShowState(LiveTraceDto state)
    {
        if (state.Samples > 0)
        {
            Summary = string.Create(
                CultureInfo.CurrentCulture,
                $"About {Format.Bytes(state.SampledBytes)} allocated in {state.Samples:N0} samples, {state.Types:N0} types, {state.DurationSeconds:N0} s.");
        }

        // Catching up with a recording that is ahead (after a worker restart) is said differently: the delay is the file's age.
        Delay = state.Completed || state.DelaySeconds is not { } seconds || seconds < SaidDelaySeconds
            ? state is { Completed: false, CaughtUp: false, Samples: > 0 } && Delay is not null ? Delay : null
            : string.Create(CultureInfo.CurrentCulture, $"These figures are {seconds:N0} s behind the process.");
        Note = string.Join(" ", NotesOf(state));
    }

    private static IEnumerable<string> NotesOf(LiveTraceDto state)
    {
        if (state.Problem is { } problem)
        {
            yield return problem + " The figures stop there.";
        }

        if (!state.HasStacks)
        {
            yield return "Call stacks cannot be shown while recording. The finished trace has them.";
        }

        if (state.LostEvents > 0)
        {
            yield return string.Create(CultureInfo.CurrentCulture, $"The runtime dropped {state.LostEvents:N0} events, so the figures are low.");
        }

        if (state.SamplesPastLimits > 0)
        {
            yield return string.Create(CultureInfo.CurrentCulture, $"{state.SamplesPastLimits:N0} samples are added up under other types or stacks. The finished trace has them all.");
        }
    }

    private void Stopped(string reason)
    {
        Delay = null;
        Note = reason;
    }

    partial void OnSelectionChanged(TimeInterval? value)
    {
        if (value is not null)
        {
            // Whole stretches, as the worker counts them; a click without a drag is no interval.
            var width = _following?.State?.BucketSeconds ?? BucketSeconds;
            var snapped = new TimeInterval(Math.Floor(value.FromSeconds / width) * width, Math.Ceiling(value.ToSeconds / width) * width);
            if (value.ToSeconds <= value.FromSeconds)
            {
                Selection = null;
                return;
            }

            if (snapped != value)
            {
                Selection = snapped;
                return;
            }
        }

        Interval = value is null
            ? WholeRunText
            : string.Create(CultureInfo.CurrentCulture, $"From {value.FromSeconds:N0} s to {value.ToSeconds:N0} s");
        Ask("the interval");
    }

    partial void OnSelectedTypeChanged(LiveTypeItem? value) => LoadStacks();

    partial void OnShowStacksChanged(bool value)
    {
        if (value)
        {
            LoadStacks();
        }
        else
        {
            ClearStacks();
        }
    }

    private bool HasSelection() => Selection is not null;

    /// <summary>The whole run again.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void WholeRun() => Selection = null;

    private bool HasType() => SelectedType is not null;

    /// <summary>The stacks of every type again.</summary>
    [RelayCommand(CanExecute = nameof(HasType))]
    private void AllTypes() => SelectedType = null;

    /// <summary>The stacks as they are by now.</summary>
    [RelayCommand]
    private void RefreshStacks() => LoadStacks();

    private AllocationScope Scope => new(Selection?.FromSeconds ?? 0, Selection?.ToSeconds, SelectedType?.Name);

    /// <summary>Brings up to date what the user's choice changed, at once.</summary>
    private void Ask(string what)
    {
        if (_following is not { Trace: { } handle, State: { } state } following)
        {
            return;
        }

        _logger.LogInformation("Live allocations of {Path} (worker trace {WorkerTrace}): {What} changed to {Interval}", handle.Path, handle.Descriptor.TraceId, what, Interval);
        following.Asked = true;
        if (following.Completed)
        {
            // Nobody asks any more where it stands.
            following.Asked = false;
            FollowTask = RefreshCompletedAsync(following, handle, state);
        }

        LoadStacks();
    }

    private async Task RefreshCompletedAsync(Following following, TraceHandle handle, LiveTraceDto state)
    {
        try
        {
            await RefreshAsync(following, handle, state, following.Stop.Token);
        }
        catch (Exception) when (following.Stop.IsCancellationRequested)
        {
            // Another run took its place.
        }
        catch (Exception ex) when (ex is WorkerException or OperationCanceledException)
        {
            Stopped(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The live allocations of {Path} failed", handle.Path);
            Stopped("The live allocations failed. Details are in the log.");
        }
    }

    /// <summary>The timeline and the types, as of <paramref name="state"/>.</summary>
    private async Task RefreshAsync(Following following, TraceHandle handle, LiveTraceDto state, CancellationToken ct)
    {
        following.Shown = Stopwatch.GetTimestamp();
        var bucket = state.BucketSeconds * Math.Max(1, Math.Ceiling(state.DurationSeconds / state.BucketSeconds / Bars));
        var timeline = await _session!.QueryTimelineAsync(handle, bucket, ct);
        var answer = await _session.QueryTypesAsync(handle, Scope with { TypeName = null }, ct);
        ResultPage page;
        try
        {
            page = await answer.Rows.GetRowsAsync(0, Math.Min(TopTypes, answer.Result.Types.TotalCount), ct);
        }
        finally
        {
            answer.Rows.ReleaseInBackground(_logger);
        }

        if (ct.IsCancellationRequested)
        {
            return;
        }

        (Buckets, BucketSeconds, DurationSeconds) = (timeline.Buckets, bucket, state.DurationSeconds);
        ShowTypes(answer.Result, page);
        following.ShownVersion = state.Version;
    }

    /// <summary>Updates the rows in place, so that a selected type stays selected and only what changed is drawn again.</summary>
    private void ShowTypes(AllocationTypesResult result, ResultPage page)
    {
        var columns = result.Types.Columns.Select((c, i) => (c.Name, i)).ToDictionary(c => c.Name, c => c.i, StringComparer.Ordinal);
        var (type, bytes, samples) = (columns[AllocationTypeColumns.Type], columns[AllocationTypeColumns.Bytes], columns[AllocationTypeColumns.Samples]);
        var known = Types.ToDictionary(t => t.Name, StringComparer.Ordinal);
        for (var i = 0; i < page.Rows.Count; i++)
        {
            var row = page.Rows[i];
            var name = row[type] ?? string.Empty;
            if (known.Remove(name, out var item))
            {
                var at = Types.IndexOf(item);
                if (at != i)
                {
                    Types.Move(at, i);
                }
            }
            else
            {
                item = new LiveTypeItem(name);
                Types.Insert(i, item);
            }

            item.Update(Number(row[bytes]), Number(row[samples]), result.SampledBytes);
        }

        while (Types.Count > page.Rows.Count)
        {
            Types.RemoveAt(Types.Count - 1);
        }

        static long Number(string? text) => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    private void ClearStacks()
    {
        _following?.Stacks?.Cancel();
        Stacks.Clear();
        SelectedStack = null;
        StacksStatus = null;
    }

    private void LoadStacks()
    {
        if (!ShowStacks || _following is not { Trace: { } handle } following)
        {
            return;
        }

        following.Stacks?.Cancel();
        following.Stacks = CancellationTokenSource.CreateLinkedTokenSource(following.Stop.Token);
        _ = LoadStacksAsync(handle, Scope, following.Stacks.Token);
    }

    private async Task LoadStacksAsync(TraceHandle handle, AllocationScope scope, CancellationToken ct)
    {
        StacksStatus = "Finding the top call stacks...";
        try
        {
            var result = await _session!.QueryStacksAsync(handle, scope, TopStacks, ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            Stacks.Clear();
            for (var i = 0; i < result.Stacks.Count; i++)
            {
                var stack = result.Stacks[i];
                Stacks.Add(new AllocationStackItem(i + 1, stack.Bytes, result.SampledBytes == 0 ? 0 : 100.0 * stack.Bytes / result.SampledBytes, stack.Samples, stack.Frames));
            }

            SelectedStack = Stacks.FirstOrDefault();
            StacksStatus = Stacks.Count == 0
                ? "No allocation was sampled in this interval."
                : string.Create(CultureInfo.CurrentCulture, $"As of {DateTime.Now:T}. Refresh brings them up to date.");
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // A newer choice, or another run, took its place.
        }
        catch (Exception ex) when (ex is WorkerException or OperationCanceledException)
        {
            Stacks.Clear();
            StacksStatus = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The live call stacks failed");
            StacksStatus = "The call stacks failed. Details are in the log.";
        }
    }

    private async Task CloseInWorkerAsync(TraceHandle handle)
    {
        try
        {
            await _session!.CloseLiveAsync(handle, CancellationToken.None);
        }
        catch (Exception ex) when (ex is WorkerException or OperationCanceledException)
        {
            _logger.LogInformation("Closing the live trace {Path} in the worker failed: {Reason}", handle.Path, ex.Message);
        }
    }

    /// <summary>One run being followed; everything here is the UI thread's.</summary>
    private sealed class Following
    {
        public CancellationTokenSource Stop { get; } = new();

        public CancellationTokenSource? Stacks { get; set; }

        public TraceHandle? Trace { get; set; }

        public LiveTraceDto? State { get; set; }

        public bool Ended { get; set; }

        public bool Completed { get; set; }

        /// <summary>The user's choice changed what is to be shown.</summary>
        public bool Asked { get; set; }

        public long ShownVersion { get; set; } = -1;

        public long Shown { get; set; }
    }
}
