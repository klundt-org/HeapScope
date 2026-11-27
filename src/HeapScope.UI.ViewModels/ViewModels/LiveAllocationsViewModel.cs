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
using HeapScope.Application.Workers;
using HeapScope.Contracts;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>
/// What the process being watched allocates, as it does (ADR-037): the worker follows the run's allocation trace, and
/// one loop asks it a few times a second what changed, until another run takes its place. The types of the chosen interval are kept
/// up to date in place; call stacks are looked up when asked for. The run's finished trace stays the record.
/// </summary>
public sealed partial class LiveAllocationsViewModel : ObservableObject
{
    /// <summary>Types listed, most allocated first.</summary>
    public const int TopTypes = 20;

    /// <summary>Types looked through for the selected one, which stays listed when it falls behind.</summary>
    private const int LookedThrough = 500;

    /// <summary>Types of a chosen interval written to the log.</summary>
    private const int LoggedTypes = 5;

    /// <summary>Stacks listed, most allocated first.</summary>
    public const int TopStacks = 20;

    /// <summary>How often the worker is asked where it stands.</summary>
    public static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(500);

    /// <summary>How often what is shown is brought up to date while samples arrive.</summary>
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(2);

    /// <summary>A delay longer than this is said: at best it is a quarter of a second.</summary>
    public const double SaidDelaySeconds = 2;

    /// <summary>
    /// A worker that stops this often in a row while it follows a recording or answers about it is not started for it again: each time
    /// takes the user's open snapshots with it.
    /// </summary>
    public const int MostCrashes = 3;

    /// <summary>Bars of the timeline, at most about this many.</summary>
    private const double Bars = 200;

    private const string WholeRunText = "The whole run so far";

    private readonly AllocationSession? _session;
    private readonly ILogger _logger;
    private Following? _following;
    private bool _updating;
    private bool _applying;

    /// <summary>The interval the types shown are of.</summary>
    private TimeInterval? _answered;

    /// <summary>Which notes were last shown: their numbers change with every sample.</summary>
    private string _notes = string.Empty;

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

    /// <summary>The delay last measured, said or not (smoke runs log it).</summary>
    public double? DelaySeconds => _following?.State?.DelaySeconds;

    /// <summary>What the stacks are of.</summary>
    public string StacksOf => SelectedType is { } type ? type.Name : "all types";

    /// <summary>How the wait between two questions passes: tests hold it.</summary>
    public Func<TimeSpan, CancellationToken, Task> Wait { get; set; } = static (wait, ct) => Task.Delay(wait, ct);

    /// <summary>How long what is shown stays as it is, at least: tests make it shorter.</summary>
    public TimeSpan Refresh { get; set; } = RefreshEvery;

    /// <summary>Ends once the followed recording's end is shown, or following it failed (tests and smoke runs); completed without one.</summary>
    public Task EndTask { get; private set; } = Task.CompletedTask;

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
        EndTask = following.End.Task;
        IsShown = true;
        Summary = "Waiting for the first allocation sample.";
        _ = FollowAsync(following, path, namesPath);
    }

    /// <summary>The run's recording ended: what is left of it is read, and the figures stay.</summary>
    public void End()
    {
        if (_following is { } following)
        {
            following.Ended = true;
        }
    }

    /// <summary>Stops following, in the worker too, and shows nothing.</summary>
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
        _answered = null;
        _notes = string.Empty;
    }

    partial void OnSelectionChanged(TimeInterval? value)
    {
        if (!_applying && _following is { Over: true } && value != _answered)
        {
            // Nobody answers any more: the figures shown keep the interval they are of.
            Apply(_answered);
            return;
        }

        if (value is not null && !_applying)
        {
            if (value.ToSeconds <= value.FromSeconds)
            {
                // A click without a drag is no interval.
                Selection = null;
                return;
            }

            // Whole stretches, as the worker counts them; it says which it counted, since they may have grown meanwhile.
            var width = _following?.State?.BucketSeconds ?? BucketSeconds;
            var snapped = new TimeInterval(Math.Floor(value.FromSeconds / width) * width, Math.Ceiling(value.ToSeconds / width) * width);
            if (snapped != value)
            {
                Selection = snapped;
                return;
            }
        }

        Interval = value is null
            ? WholeRunText
            : string.Create(CultureInfo.CurrentCulture, $"From {value.FromSeconds:N0} s to {value.ToSeconds:N0} s");
        if (!_applying)
        {
            Ask();
        }
    }

    partial void OnSelectedTypeChanged(LiveTypeItem? value)
    {
        if (!_updating)
        {
            LoadStacks();
        }
    }

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
    private void RefreshStacks() => LoadStacks(again: true);

    private AllocationScope Scope => new(Selection?.FromSeconds ?? 0, Selection?.ToSeconds, SelectedType?.Name);

    /// <summary>The user chose another interval: the loop shows it next, whatever it is doing.</summary>
    private void Ask()
    {
        if (_following is not { } following)
        {
            return;
        }

        following.Choice++;
        following.Chosen.Release();
        LoadStacks();
    }

    /// <summary>The timeline and the types, as of <paramref name="state"/>; an answer for an interval no longer chosen is dropped.</summary>
    private async Task RefreshAsync(Following following, TraceHandle handle, LiveTraceDto state, bool chosen, CancellationToken ct)
    {
        var choice = following.Choice;
        var asked = state.BucketSeconds * Math.Max(1, Math.Ceiling(state.DurationSeconds / state.BucketSeconds / Bars));
        var timeline = await _session!.QueryTimelineAsync(handle, asked, ct);
        var answer = await _session.QueryTypesAsync(handle, Scope with { TypeName = null }, ct);
        ResultPage page;
        try
        {
            page = await answer.Rows.GetRowsAsync(0, Math.Min(SelectedType is null ? TopTypes : LookedThrough, answer.Result.Types.TotalCount), ct);
        }
        finally
        {
            answer.Rows.ReleaseInBackground(_logger);
        }

        if (ct.IsCancellationRequested || choice != following.Choice)
        {
            return;
        }

        (Buckets, BucketSeconds, DurationSeconds) = (timeline.Buckets, timeline.BucketSeconds, state.DurationSeconds);
        Applied(answer.Result.Interval);
        var columns = answer.Result.Types.Columns.Select((c, i) => (c.Name, i)).ToDictionary(c => c.Name, c => c.i, StringComparer.Ordinal);
        var (type, bytes, samples) = (columns[AllocationTypeColumns.Type], columns[AllocationTypeColumns.Bytes], columns[AllocationTypeColumns.Samples]);
        ShowTypes([.. page.Rows.Select(r => (r[type] ?? string.Empty, Number(r[bytes]), Number(r[samples])))], answer.Result.SampledBytes);
        following.Shown = Stopwatch.GetTimestamp();

        // What the answers are of, not what was asked about: one a little older is asked for again.
        var of = answer.Result.Interval;
        (following.ShownVersion, following.ShownChoice) = (Math.Min(of?.Version ?? state.Version, timeline.Interval?.Version ?? state.Version), choice);
        _answered = Selection;
        if (chosen)
        {
            // A figure the user looked at can be found again in the finished trace: the interval counted, and as of when.
            _logger.LogInformation(
                "Live allocations of {Path} (worker trace {WorkerTrace}) show {Interval} as of {Samples} samples, {Seconds:N1} s, stretches of {Bucket:N0} s: {IntervalSamples} samples of {IntervalBytes} bytes in {Types} types; top {Top}",
                handle.Path,
                handle.Descriptor.TraceId,
                Selection is { } s ? string.Create(CultureInfo.InvariantCulture, $"{s.FromSeconds:R} s to {s.ToSeconds:R} s") : "the whole run",
                of?.Version ?? state.Samples,
                of?.AsOfSeconds ?? state.DurationSeconds,
                of?.StretchSeconds ?? state.BucketSeconds,
                answer.Result.Samples,
                answer.Result.SampledBytes,
                answer.Result.Types.TotalCount,
                string.Join("; ", Types.Take(LoggedTypes).Select(t => string.Create(CultureInfo.InvariantCulture, $"{t.Name} {t.Bytes} bytes ({t.Samples} samples)"))));
        }

        static long Number(string? text) => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    /// <summary>Shows the interval the worker counted: whole stretches, which grow in a long run.</summary>
    private void Applied(AllocationIntervalDto? interval)
    {
        if (interval is null || Selection is not { } selection || interval.ToSeconds is not { } to || (selection.FromSeconds == interval.FromSeconds && selection.ToSeconds == to))
        {
            return;
        }

        Apply(new TimeInterval(interval.FromSeconds, to));
    }

    /// <summary>Shows an interval that was not chosen, so nothing is asked for it.</summary>
    private void Apply(TimeInterval? interval)
    {
        _applying = true;
        try
        {
            Selection = interval;
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>Updates the rows in place, so that a selected type stays selected and only what changed is drawn again.</summary>
    public void ShowTypes(IReadOnlyList<(string Name, long Bytes, long Samples)> found, long total)
    {
        ArgumentNullException.ThrowIfNull(found);
        var selected = SelectedType;
        _updating = true;
        try
        {
            var known = Types.ToDictionary(t => t.Name, StringComparer.Ordinal);
            var rows = found.Take(TopTypes).ToList();
            if (selected is not null)
            {
                rows.AddRange(found.Skip(TopTypes).Where(r => r.Name == selected.Name));
            }

            for (var i = 0; i < rows.Count; i++)
            {
                var (name, bytes, samples) = rows[i];
                if (known.Remove(name, out var item))
                {
                    var at = Types.IndexOf(item);
                    if (at != i)
                    {
                        // Not moved: the grid keeps showing a moved row where it was.
                        Types.RemoveAt(at);
                        Types.Insert(i, item);
                    }
                }
                else
                {
                    item = new LiveTypeItem(name);
                    Types.Insert(i, item);
                }

                item.Update(bytes, samples, total);
            }

            while (Types.Count > rows.Count)
            {
                Types.RemoveAt(Types.Count - 1);
            }

            // The grid lets go of a row that was taken out to be put elsewhere; the user did not.
            SelectedType = selected is not null && Types.Contains(selected) ? selected : null;
        }
        finally
        {
            _updating = false;
        }

        if (selected != SelectedType)
        {
            LoadStacks();
        }
    }

    private void ClearStacks()
    {
        _following?.Stacks?.Cancel();
        Stacks.Clear();
        SelectedStack = null;
        StacksStatus = null;
    }

    /// <param name="again">Of the same choice: what is shown may stay.</param>
    private void LoadStacks(bool again = false)
    {
        if (!ShowStacks || _following is not { } following)
        {
            return;
        }

        if (following.Trace is not { } handle || following.Over)
        {
            StacksStatus = following.Over ? Note : null;
            if (!again)
            {
                // Another type's are not shown under this one's name.
                Stacks.Clear();
                SelectedStack = null;
            }

            return;
        }

        following.Stacks?.Cancel();
        following.Stacks?.Dispose();
        following.Stacks = CancellationTokenSource.CreateLinkedTokenSource(following.Stop.Token);
        _ = LoadStacksAsync(handle, Scope, following.Stacks.Token);
    }

    private async Task LoadStacksAsync(TraceHandle handle, AllocationScope scope, CancellationToken ct)
    {
        using var activity = new Activity("HeapScope.Query").SetTag("grid", "LiveAllocationStacks").Start();
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
            _logger.LogInformation("The live call stacks of {Path} were refused: {Reason}", handle.Path, ex.Message);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Closing the live trace {Path} in the worker failed", handle.Path);
        }
    }
}
