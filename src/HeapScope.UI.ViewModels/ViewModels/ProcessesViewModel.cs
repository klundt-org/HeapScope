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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeapScope.Application.Sessions;
using HeapScope.Application.Tutorial;
using HeapScope.Contracts;
using HeapScope.UI.Services;

namespace HeapScope.UI.ViewModels;

/// <summary>One row of the process list. Numbers stay numeric so the grid sorts them.</summary>
public sealed record ProcessRow(int ProcessId, string Name, string Runtime, string Elevation, long WorkingSetBytes, string CommandLine, bool NeedsAdministrator, bool IsSample = false)
{
    /// <summary>"Sample" for the tutorial's sample this HeapScope started (sprint 3.13), else empty.</summary>
    public string Kind => IsSample ? "Sample" : string.Empty;

    public static ProcessRow From(ProcessEntry process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var elevation = process.Elevation switch
        {
            ProcessElevation.Elevated => process.NeedsAdministrator ? "Needs administrator" : "Elevated",
            ProcessElevation.NotElevated => string.Empty,
            _ => "Unknown",
        };
        return new ProcessRow(process.ProcessId, process.Name, process.RuntimeVersion ?? string.Empty, elevation, process.WorkingSetBytes ?? 0, process.CommandLine ?? string.Empty, process.NeedsAdministrator);
    }
}

/// <summary>
/// The Processes page (sprint 1.1): running .NET processes with elevation hints, capture into the current session,
/// and opening the captured dump through the Snapshots page (which runs the memory check). Since sprint 2.5 it also
/// records a process's allocations, shown on the Allocations page when done.
/// </summary>
public sealed partial class ProcessesViewModel : ObservableObject
{
    private readonly SampleService? _samples;
    private readonly IUiDispatcher? _dispatcher;
    private readonly Microsoft.Extensions.Logging.ILogger _logger;
    private readonly AllocationsViewModel? _allocations;

    private CancellationTokenSource? _recording;

    private readonly ICaptureService _capture;
    private readonly SnapshotsViewModel _snapshots;
    private readonly IUserInteraction _interaction;

    /// <param name="samples">The tutorial's sample (sprint 3.13); null hides the page's sample section.</param>
    /// <param name="dispatcher">Where a sample's exit is reported (raised on any thread); null runs it at once.</param>
    public ProcessesViewModel(ICaptureService capture, SnapshotsViewModel snapshots, IUserInteraction interaction, AllocationsViewModel? allocations = null, SampleService? samples = null, IUiDispatcher? dispatcher = null, Microsoft.Extensions.Logging.ILogger<ProcessesViewModel>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProcessesViewModel>.Instance;
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _allocations = allocations;
        _samples = samples;
        _dispatcher = dispatcher;
        _sampleStatus = samples?.UnavailableReason;
        if (samples is not null)
        {
            samples.Changed += (_, _) => Post(MarkSamples);
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CaptureCommand), nameof(RecordAllocationsCommand), nameof(StopSampleCommand))]
    private ProcessRow? _selected;

    /// <summary>Whether a sample started here keeps growing (sprint 3.13), so Live, traces, Compare and Trend show change.</summary>
    [ObservableProperty]
    private bool _keepGrowing = true;

    /// <summary>True while Record allocations records; Stop ends it and keeps what was recorded.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecordAllocationsCommand), nameof(StopRecordingCommand))]
    private bool _isRecording;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _showElevationHint;

    /// <summary>Capture a gcdump instead of a full dump (sprint 1.8): the object graph only, smaller, no field values.</summary>
    [ObservableProperty]
    private bool _captureGcDump;

    public ObservableCollection<ProcessRow> Processes { get; } = [];

    /// <summary>The tutorial sample's scenarios to choose from (sprint 3.13), read from the sample.</summary>
    public ObservableCollection<SampleScenarioItem> SampleScenarios { get; } = [];

    /// <summary>
    /// Whether this page offers the tutorial's sample: its host has the service, and capture works here (review M3: off
    /// Windows nothing can be done with it, so the section is not shown).
    /// </summary>
    public bool HasSample => _samples is not null && Unavailable is null;

    /// <summary>Why the sample section has no scenarios to offer (not installed, elevated, could not be read), or null.</summary>
    [ObservableProperty]
    private string? _sampleStatus;

    /// <summary>The path of the last successful capture (tests).</summary>
    public string? LastCapturePath { get; private set; }

    /// <summary>The path of the last allocations recording, read or not (tests).</summary>
    public string? LastRecordingPath { get; private set; }

    /// <summary>Why capture is not available on this system (sprint 2.3.6), or null; the page shows it instead of the list.</summary>
    public string? Unavailable => _capture.UnavailableReason;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Unavailable is { } reason)
        {
            Status = reason;
            return;
        }

        IsBusy = true;
        try
        {
            var processes = await _capture.ListProcessesAsync(CancellationToken.None);
            var selectedId = Selected?.ProcessId;
            Processes.Clear();
            foreach (var process in processes)
            {
                Processes.Add(ProcessRow.From(process) with { IsSample = _samples?.IsSample(process.ProcessId) == true });
            }

            Selected = Processes.FirstOrDefault(p => p.ProcessId == selectedId);
            ShowElevationHint = Processes.Any(p => p.NeedsAdministrator);
            Status = Processes.Count == 0 ? "No running .NET processes were found." : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException)
        {
            Status = "The process list could not be read: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        // Review H2: the sample's catalog after the list, on its own: a sample that cannot run never empties the list.
        await LoadSampleScenariosAsync();
    }

    private bool CanCapture() => Selected is not null && Unavailable is null;

    // The sample's catalog, read once from the sample when the page first lists processes (sprint 3.13).
    private async Task LoadSampleScenariosAsync()
    {
        if (_samples is null || SampleScenarios.Count > 0 || _samples.UnavailableReason is not null)
        {
            return;
        }

        try
        {
            foreach (var scenario in await _samples.ListScenariosAsync(CancellationToken.None))
            {
                SampleScenarios.Add(new SampleScenarioItem(scenario));
            }

            SampleStatus = null;
        }
        catch (SampleRefusedException ex)
        {
            SampleStatus = ex.Message;
        }
    }

    /// <summary>
    /// Starts the tutorial's sample with the chosen scenarios (sprint 3.13), then lists it, selected and marked "Sample", ready to
    /// capture or record.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSample))]
    private async Task StartSampleAsync()
    {
        IsBusy = true;
        try
        {
            var chosen = SampleScenarios.Where(s => s.IsChosen).Select(s => s.Name).ToList();
            if (_samples is null)
            {
                return;
            }

            var sample = await _samples.StartAsync(chosen, KeepGrowing, CancellationToken.None);
            await RefreshAsync();
            Selected = Processes.FirstOrDefault(p => p.ProcessId == sample.ProcessId);
            if (Selected is null)
            {
                // Review H5: the claim "the sample is not in the list" can be backed up from the log.
                Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_logger, "Sample {Pid} started but is not among the {Count} processes listed", sample.ProcessId, Processes.Count);
            }

            Status = Selected is null
                ? $"Sample {sample.ProcessId} started: {string.Join(", ", sample.Scenarios)}, but it is not in the list yet. Press Refresh."
                : $"Sample {sample.ProcessId} started: {string.Join(", ", sample.Scenarios)}.";
        }
        catch (SampleRefusedException ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanStopSample() => Selected is { IsSample: true };

    /// <summary>Stops the selected sample (sprint 3.13).</summary>
    [RelayCommand(CanExecute = nameof(CanStopSample))]
    private async Task StopSampleAsync()
    {
        if (Selected is not { } row || _samples is null)
        {
            return;
        }

        var stopped = _samples.Stop(row.ProcessId, "the Processes page");
        await RefreshAsync();
        Status = stopped ? $"Sample {row.ProcessId} stopped." : $"Sample {row.ProcessId} had already exited.";
    }

    // Review H3: a sample stopped elsewhere, or exited, loses its Sample mark without waiting for a refresh.
    private void MarkSamples()
    {
        for (var i = 0; i < Processes.Count; i++)
        {
            var isSample = _samples?.IsSample(Processes[i].ProcessId) == true;
            if (Processes[i].IsSample != isSample)
            {
                var wasSelected = Selected == Processes[i];
                Processes[i] = Processes[i] with { IsSample = isSample };
                if (wasSelected)
                {
                    Selected = Processes[i];
                }
            }
        }
    }

    private void Post(Action action)
    {
        if (_dispatcher is null)
        {
            action();
        }
        else
        {
            _dispatcher.Post(action);
        }
    }

    /// <summary>Whether this page can record allocations (the host has an Allocations page).</summary>
    public bool CanRecordAllocations => _allocations is not null;

    private bool CanRecord() => CanCapture() && _allocations is not null && !IsRecording;

    /// <summary>
    /// Records the selected process's sampled allocations (sprint 2.5) for up to 30 s into the session folder, then shows
    /// them on the Allocations page.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRecord))]
    private async Task RecordAllocationsAsync()
    {
        var target = Selected!;
        _recording?.Dispose();
        _recording = new CancellationTokenSource();
        IsRecording = true;
        var duration = HeapScope.Application.Allocations.AllocationSession.DefaultRecording;
        Status = $"Recording the allocations of {target.Name} (pid {target.ProcessId}) for up to {duration.TotalSeconds:N0} s. Use the application now; Stop keeps what was recorded.";
        try
        {
            // Stop does nothing once recording has ended and the trace is being read.
            var handle = await _allocations!.RecordAsync(target.ProcessId, target.Name, duration, _recording.Token, () =>
            {
                IsRecording = false;
                Status = $"Reading the allocations of {target.Name}...";
            });
            LastRecordingPath = handle.Path;
            Status = $"Recorded {System.IO.Path.GetFileName(handle.Path)}: about {Format.Bytes(handle.Descriptor.SampledBytes)} allocated in {handle.Descriptor.DurationSeconds:N1} s. A trace holds method and type names, not heap contents; keep it private.";
        }
        catch (OperationCanceledException)
        {
            Status = "Recording canceled.";
        }
        catch (CaptureFailedException ex)
        {
            Status = ex.Message;
        }
        catch (HeapScope.Application.Allocations.TraceRecordedButNotReadException ex)
        {
            // The trace is kept: say where, and how to open it later (the 2.5 review).
            LastRecordingPath = ex.Path;
            Status = ex.Message;
        }
        catch (HeapScope.Application.Workers.WorkerException ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsRecording = false;
            _recording?.Dispose();
            _recording = null;
        }
    }

    [RelayCommand(CanExecute = nameof(IsRecording))]
    private void StopRecording() => _recording?.Cancel();

    [RelayCommand(CanExecute = nameof(CanCapture))]
    private async Task CaptureAsync()
    {
        var target = Selected!;
        IsBusy = true;
        var gcdump = CaptureGcDump;
        Status = gcdump
            ? $"Capturing a gcdump of {target.Name} (pid {target.ProcessId}). The process runs one full garbage collection and reports its heap."
            : $"Capturing {target.Name} (pid {target.ProcessId}). The process is paused while its memory is written to disk.";
        try
        {
            var result = await _capture.StartCapture(target.ProcessId, kind: gcdump ? SnapshotKindDto.GcDump : SnapshotKindDto.FullDump).Result;
            LastCapturePath = result.Path;
            Status = $"Captured {System.IO.Path.GetFileName(result.Path)} ({Format.Bytes(result.SizeBytes)}).{(result.Warning is null ? string.Empty : " " + result.Warning)}";
            if (await _interaction.ConfirmAsync("Capture complete", $"{result.SecretsNote}\n\nOpen the {(gcdump ? "gcdump" : "dump")} now?"))
            {
                await _snapshots.OpenPathAsync(result.Path);
            }
        }
        catch (OperationCanceledException)
        {
            Status = "Capture canceled.";
        }
        catch (CaptureFailedException ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
