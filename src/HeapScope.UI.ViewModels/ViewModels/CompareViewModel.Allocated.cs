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

using System.Collections.Specialized;
using System.Globalization;
using HeapScope.Application.Allocations;
using HeapScope.Application.Presentation;
using HeapScope.Application.Workers;
using HeapScope.Contracts;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>The comparison's allocated columns (ADR-028): the trace and interval they count, and where a row's type was allocated.</summary>
public sealed partial class CompareViewModel
{
    // The rows are asked for with the trace: without it they could not be loaded again.
    private void OnTracesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_readingTrace == 0 && ShowAllocated && _allocated is { } shown && _allocations!.Traces.All(t => t.Trace != shown.Trace))
        {
            Untick(shown.Trace, AllocatedBetweenTexts.TraceClosed);
        }
    }

    // The trace cannot be asked any more: the comparison is shown without it, and why.
    private void Untick(TraceHandle trace, string note)
    {
        Logger.LogInformation("Compare: Allocated between is unticked, as the allocation trace {Trace} cannot be used; said as: {Note}", trace.Path, note);
        _allocated = null;
        _untickedNote = note;
        ShowAllocated = false;
    }

    private void ShowWhereAllocated(DiffRowItem? row)
    {
        if (_readingTrace > 0)
        {
            Status = AllocatedBetweenTexts.StillReading;
            return;
        }

        if (_allocated is not { } allocated || _allocations is null)
        {
            Status = AllocatedNote ?? AllocatedBetweenTexts.TickFirst;
            return;
        }

        if (row is not { IsLoaded: true })
        {
            return;
        }

        if (row.AllocatedBytes is null)
        {
            Status = AllocatedBetweenTexts.NoSample;
            return;
        }

        Logger.LogInformation(
            "Compare: opened {Trace} from {From:R} s to {To:R} s for the row {Row} ({Bytes} bytes, {Samples} samples; its type there: {Type})",
            allocated.Trace.Path,
            allocated.Interval.FromSeconds,
            allocated.Interval.ToSeconds,
            row.Name,
            row.AllocatedBytes,
            row.AllocatedSamples,
            row.AllocatedType ?? "several, so every type is shown");
        var note = row.AllocatedType is not { } traced ? AllocatedBetweenTexts.EveryType
            : row.TypeName?.Contains('+', StringComparison.Ordinal) == true ? AllocatedBetweenTexts.EveryOfName(traced)
            : null;
        if (!_allocations.ShowInterval(allocated.Trace, allocated.Interval, row.AllocatedType, note))
        {
            Status = AllocatedBetweenTexts.TraceClosed;
        }
    }

    /// <summary>The trace between the two snapshots and its interval, or why there is none. Reading a trace takes seconds, shown as a job.</summary>
    private async Task<(DiffAllocations? Allocations, string? Note, double ClockShift)> AllocationsAsync(string baseline, string current, CancellationToken cancellationToken)
    {
        if (_allocations is null)
        {
            return (null, null, 0);
        }

        var found = await Task.Run(() => AllocatedBetween.Find(baseline, current), cancellationToken);
        Logger.LogInformation(
            "Allocations between {Baseline} and {Current}: {Outcome}; {Evidence}",
            baseline,
            current,
            found.Reason is { } reason ? "none, said as: " + reason : string.Create(CultureInfo.InvariantCulture, $"{found.TracePath} from {found.FromUtc:O} to {found.ToUtc:O}"),
            found.Evidence);
        if (found.TracePath is not { } path)
        {
            return (null, found.Reason, 0);
        }

        Interlocked.Increment(ref _readingTrace);
        try
        {
            return (await _allocations.OpenBesideAsync(path, found.FromUtc, found.ToUtc, cancellationToken), found.NoHeapTime ? AllocatedBetweenTexts.NoHeapTime : null, found.ClockShiftSeconds);
        }
        catch (Exception ex) when (ex is WorkerException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            var note = ex is OperationCanceledException ? AllocatedBetweenTexts.ReadCanceled : ex.Message;
            Logger.LogInformation("Compare: the trace {Trace} was not read, said as: {Note}", path, note);
            return (null, note, 0);
        }
        finally
        {
            Interlocked.Decrement(ref _readingTrace);
        }
    }
}
