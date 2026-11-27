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

using CommunityToolkit.Mvvm.Input;
using HeapScope.Application.Snapshots;
using HeapScope.Application.Workers;
using HeapScope.Contracts;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>The Snapshots page's DAC trust: trusting the DAC that came with a dump, and opening the dump again with it.</summary>
public sealed partial class SnapshotsViewModel
{
    /// <summary>Whether the selected snapshot reads no field values because its DAC is not trusted, which the user can change.</summary>
    public bool CanTrustDac => Selected?.Handle.Descriptor.UntrustedDac is not null && _session.TrustedDacs is not null && !IsOpening;

    /// <summary>
    /// Trusts the DAC that came with the selected dump and opens the dump again (owner 2026-11-13): after a "no",
    /// or an open that never asked (an older index cache in place of a rebuild), the way to field values without closing and
    /// opening it by hand. It asks the open's own question first, so the risk is said the same way every time, and the memory
    /// question of a load before anything is closed. The snapshot opens again in place (its handle stays), so Compare's pair,
    /// Trend's ticks and the selection keep it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTrustDac))]
    private async Task TrustDacAndReopenAsync()
    {
        if (Selected is not { Handle.Descriptor.UntrustedDac: { } dac } item || _session.TrustedDacs is not { } trusted)
        {
            return;
        }

        var path = item.Handle.Path;
        var before = item.Handle.Descriptor.SnapshotId;
        var yes = await _interaction.ConfirmAsync("Trust the DAC next to this dump?", TrustQuestion(dac));
        _logger?.LogInformation("Asked to trust the DAC {Sha256} at {DacPath} for {Path} from Trust this DAC and reopen; the user answered {Answer}", dac.Sha256, dac.Path, path, yes ? "yes" : "no");
        if (!yes || !await TrustAsync(trusted, dac, $"trusted by the user for {path} (Trust this DAC and reopen)", "Not reopened"))
        {
            return;
        }

        IsOpening = true;
        Status = $"Opening {System.IO.Path.GetFileName(path)}...";
        try
        {
            // An older index cache is rebuilt now, which is a load: its memory question comes before anything is closed.
            if (await _session.GetOpenWarningAsync(path, rebuild: false, CancellationToken.None, reopening: true) is { } warning
                && !await _interaction.ConfirmAsync("Not enough free memory", warning + "\n\nOpen the dump anyway?"))
            {
                Status = "Open canceled.";
                return;
            }

            await _session.CloseInWorkerAsync(item.Handle, CancellationToken.None);
            var handle = await _session.StartOpen(path, mode: item.Handle.RequestedMode).Result;
            ShowOpened(handle, before);
            Status = OpenedStatus(handle);
        }
        catch (OperationCanceledException)
        {
            Status = "Open canceled.";
        }
        catch (Exception ex) when (ex is WorkerException)
        {
            // What the user was shown, beside the worker's line with its id.
            _logger?.LogInformation("{Section} shown as: {Message}", "Snapshot open", ex.Message);
            Status = ex.Message;
        }
        finally
        {
            IsOpening = false;
        }
    }

    /// <summary>
    /// Saves the user's trust in <paramref name="dac"/>; false, with the status saying why (after <paramref name="outcome"/>,
    /// "Not opened" or "Not reopened"), when it cannot be saved.
    /// </summary>
    private async Task<bool> TrustAsync(TrustedDacs trusted, DacTrustDetails dac, string why, string outcome)
    {
        try
        {
            await Task.Run(() => trusted.Trust(dac.Sha256, dac.Path, why));
            return true;
        }
        catch (Exception trustFailed) when (trustFailed is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(trustFailed, "The trust in DAC {Sha256} could not be saved to {File}", dac.Sha256, trusted.Path);
            Status = $"{outcome}: your trust in the DAC could not be saved to {trusted.Path} ({trustFailed.Message}). Close other HeapScope windows and try again.";
            return false;
        }
    }

    partial void OnIsOpeningChanged(bool value) => RaiseCanTrustDac();

    private void RaiseCanTrustDac()
    {
        OnPropertyChanged(nameof(CanTrustDac));
        TrustDacAndReopenCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The question asked before a DAC that came with a dump is loaded.</summary>
    internal static string TrustQuestion(DacTrustDetails dac) =>
        $"The .NET {dac.RuntimeVersion} runtime's data access component (DAC) was found only next to the dump:\n\n{dac.Path}\nSHA-256 {dac.Sha256}\n\n" +
        "A DAC is native code HeapScope runs to read the dump. One that came with a dump runs code from whoever made it, " +
        "so trust it only if you know where it came from (for example, you copied it from that runtime yourself). " +
        "HeapScope remembers this file by its hash.\n\nTrust it and open the dump?";
}
