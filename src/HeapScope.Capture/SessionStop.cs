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


using Microsoft.Diagnostics.NETCore.Client;

namespace HeapScope.Capture;

/// <summary>
/// Stops an EventPipe session without hanging on a target that does not answer (frozen in a debugger, suspended): it waits
/// <see cref="Wait"/> at most, then disposes the session, which ends its stream (peer review 4.5 H9; the counters had this since 1.7).
/// </summary>
internal static class SessionStop
{
    /// <summary>How long a stop may wait for the target.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    /// <summary>Stops <paramref name="session"/>: true when the target answered or had ended, false when it was let go after the wait.</summary>
    public static Task<bool> StopAsync(EventPipeSession session) =>
        StopAsync(session.StopAsync, session.Dispose, Wait);

    /// <summary>The rule, apart from a real session (tests).</summary>
    internal static async Task<bool> StopAsync(Func<CancellationToken, Task> stop, Action dispose, TimeSpan wait)
    {
        using var timeout = new CancellationTokenSource(wait);
        try
        {
            await stop(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            dispose();
            return false;
        }
        catch (Exception ex) when (ex is ServerNotAvailableException or ServerErrorException or EndOfStreamException or IOException or ObjectDisposedException)
        {
            // The process exited, or the session already ended: the stream is over either way.
            return true;
        }
    }
}
