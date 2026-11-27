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

using System.Diagnostics;
using System.Globalization;
using HeapScope.Application.Allocations;
using HeapScope.Application.Workers;
using HeapScope.Contracts;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>Following a recording's live trace: opening it, asking it again and again, showing its state, and its end.</summary>
public sealed partial class LiveAllocationsViewModel
{
    private async Task FollowAsync(Following following, string path, string? namesPath)
    {
        // Everything asked of the worker for this run joins up under one activity (logging.md).
        using var activity = new Activity("HeapScope.Allocations").SetTag("grid", "LiveAllocations").Start();
        var ct = following.Stop.Token;
        TraceHandle? handle = null;
        try
        {
            handle = await OpenAsync(following, path, namesPath, ct);
            following.Trace = handle;
            await LoopAsync(following, handle, ct);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // Closed: another run took its place, a past run is shown, or the app ends.
        }
        catch (WorkerCrashedException)
        {
            GiveUp(following, path);
        }
        catch (Exception ex) when (ex is WorkerException or OperationCanceledException)
        {
            _logger.LogInformation("The live allocations of {Path} stopped: {Reason}", path, ex.Message);
            Stopped(following, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The live allocations of {Path} failed", path);
            Stopped(following, "The live allocations failed. Details are in the log.");
        }

        // However it ended, the worker lets go of it.
        following.Trace = null;
        following.Over = true;
        if (following.Stacks is { } stacks)
        {
            following.Stacks = null;
            await stacks.CancelAsync();
            stacks.Dispose();
        }

        if (handle is not null)
        {
            await CloseInWorkerAsync(handle);
        }

        following.End.TrySetResult();
    }

    private void GiveUp(Following following, string path)
    {
        _logger.LogInformation("The live allocations of {Path} are given up", path);
        Stopped(following, "The analysis worker stopped each time it followed this recording, so the live allocations are off." + (following.Ended ? string.Empty : " The trace itself is still recorded."));
    }

    private async Task<TraceHandle> OpenAsync(Following following, string path, string? namesPath, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                return await _session!.OpenLiveAsync(path, namesPath, ct);
            }
            catch (WorkerCrashedException ex) when (Crashed(following, path, ex))
            {
                await Wait(PollEvery, ct);
            }
        }
    }

    /// <summary>Counts a worker that stopped: whether it is to be asked again.</summary>
    private bool Crashed(Following following, string path, WorkerCrashedException ex)
    {
        following.Crashes++;
        _logger.LogInformation(
            "The worker stopped while the live allocations of {Path} were asked for ({Crashes} of {Most} in a row, {Samples} samples read before): {Reason}",
            path,
            following.Crashes,
            MostCrashes,
            following.State?.Samples ?? 0,
            ex.Message);
        return following.Crashes < MostCrashes;
    }

    private async Task LoopAsync(Following following, TraceHandle handle, CancellationToken ct)
    {
        var told = false;
        while (true)
        {
            var settled = false;
            try
            {
                if (following.Ended && !told)
                {
                    await _session!.EndLiveAsync(handle, ct);
                    told = true;
                }

                var state = await _session!.QueryLiveAsync(handle, ct);

                // A run followed in its place shows its own figures, never these.
                ct.ThrowIfCancellationRequested();
                following.State = state;
                ShowState(handle, state);

                // What the user chose is not kept waiting, and neither is what a first sample shows.
                var chosen = following.ShownChoice != following.Choice;
                var changed = state.Version != following.ShownVersion;
                var due = state.Completed || following.Shown == 0 || Stopwatch.GetElapsedTime(following.Shown) >= Refresh;
                if (state.Version > 0 && (chosen || (changed && due)))
                {
                    await RefreshAsync(following, handle, state, chosen, ct);
                }

                // All of it worked: a worker that stops later is another matter.
                following.Crashes = 0;
                if (state.Version > 0 && following.ShownChoice != following.Choice)
                {
                    // Chosen while the answer to the choice before it arrived.
                    continue;
                }

                settled = state.Completed && (state.Version == 0 || (following.ShownChoice == following.Choice && following.ShownVersion == state.Version));
            }
            catch (WorkerCrashedException ex)
            {
                // The worker restarts with the next question and follows the file again, from its start.
                if (!Crashed(following, handle.Path, ex))
                {
                    GiveUp(following, handle.Path);
                    return;
                }
            }

            if (!settled)
            {
                await Wait(PollEvery, ct);
                continue;
            }

            if (!following.End.Task.IsCompleted)
            {
                LogEnd(handle, following.State!);
                following.End.TrySetResult();
            }

            // Nothing changes any more but what the user chooses.
            await following.Chosen.WaitAsync(ct);
        }
    }

    private void LogEnd(TraceHandle handle, LiveTraceDto state) => _logger.LogInformation(
        "The live allocations of {Path} (worker trace {WorkerTrace}) ended: {Samples} samples of {Bytes} bytes in {Types} types over {Seconds:N1} s, {Lost} lost events, {Unnamed} with unnamed frames, {Past} past the limits; {Problem}",
        handle.Path,
        handle.Descriptor.TraceId,
        state.Samples,
        state.SampledBytes,
        state.Types,
        state.DurationSeconds,
        state.LostEvents,
        state.SamplesUnnamed,
        state.SamplesPastLimits,
        state.Problem ?? "read to its end");

    private void ShowState(TraceHandle handle, LiveTraceDto state)
    {
        if (state.Samples > 0)
        {
            Summary = string.Create(
                CultureInfo.CurrentCulture,
                $"About {Format.Bytes(state.SampledBytes)} allocated in {state.Samples:N0} samples, {state.Types:N0} types, {state.DurationSeconds:N0} s.");
        }
        else if (state.Completed)
        {
            Summary = "No allocation was sampled in this run.";
        }

        var delay = state.Completed || state.DelaySeconds is not { } seconds || seconds < SaidDelaySeconds
            ? null
            : string.Create(CultureInfo.CurrentCulture, $"These figures are {seconds:N0} s behind the process.");
        var notes = NotesOf(state).ToList();
        var note = string.Join(" ", notes.Select(static n => n.Text));
        var kinds = string.Join(", ", notes.Select(static n => n.Kind));
        if (kinds != _notes || (delay is null) != (Delay is null))
        {
            _logger.LogInformation(
                "Live allocations of {Path} (worker trace {WorkerTrace}) say at {Samples} samples, {Seconds:N1} s: delay {Delay}; note {Note}",
                handle.Path,
                handle.Descriptor.TraceId,
                state.Samples,
                state.DurationSeconds,
                delay ?? "none",
                note.Length == 0 ? "none" : note);
        }

        (Delay, Note, _notes) = (delay, note, kinds);
    }

    private static IEnumerable<(string Kind, string Text)> NotesOf(LiveTraceDto state)
    {
        if (state.Problem is { } problem)
        {
            yield return ("problem", problem + " The figures stop there.");
        }

        if (!state.HasStacks)
        {
            yield return ("no stacks", "Call stacks cannot be shown while recording. The finished trace has them.");
        }

        if (state.LostEvents > 0)
        {
            yield return ("lost events", string.Create(CultureInfo.CurrentCulture, $"The runtime dropped {state.LostEvents:N0} events, so the figures are low."));
        }

        if (state.SamplesUnnamed > 0)
        {
            yield return ("unnamed frames", string.Create(CultureInfo.CurrentCulture, $"{state.SamplesUnnamed:N0} samples have a frame without a name{(state.Completed ? "." : " yet.")}"));
        }

        if (state.SamplesPastLimits > 0)
        {
            yield return ("past limits", string.Create(CultureInfo.CurrentCulture, $"{state.SamplesPastLimits:N0} samples are added up under other types or stacks. The finished trace has them all."));
        }
    }

    /// <summary>Following is over before the recording's end: what is shown stays, with why.</summary>
    private void Stopped(Following following, string reason)
    {
        if (_following != following)
        {
            return;
        }

        Delay = null;
        Note = reason;
        following.Over = true;

        // A choice nobody answered is not what the figures are of, and its stacks are not of what is shown.
        if (Selection != _answered)
        {
            ClearStacks();
            Apply(_answered);
        }

        if (ShowStacks && Stacks.Count == 0)
        {
            StacksStatus = reason;
        }

        if (following.State is not { Samples: > 0 })
        {
            Summary = null;
        }
    }

    /// <summary>One run being followed; everything here is the UI thread's.</summary>
    private sealed class Following
    {
        public CancellationTokenSource Stop { get; } = new();

        public CancellationTokenSource? Stacks { get; set; }

        /// <summary>The trace while the loop asks about it: nothing is asked of one that is closed.</summary>
        public TraceHandle? Trace { get; set; }

        public LiveTraceDto? State { get; set; }

        public bool Ended { get; set; }

        /// <summary>Following is over: what is shown stays, and nothing is answered any more.</summary>
        public bool Over { get; set; }

        public TaskCompletionSource End { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Counts the user's choices, and let go of at each: a loop with nothing else to do waits for it.</summary>
        public int Choice { get; set; }

        public SemaphoreSlim Chosen { get; } = new(0);

        /// <summary>The choice and version what is shown is of.</summary>
        public int ShownChoice { get; set; }

        public long ShownVersion { get; set; } = -1;

        public long Shown { get; set; }

        public int Crashes { get; set; }
    }
}
