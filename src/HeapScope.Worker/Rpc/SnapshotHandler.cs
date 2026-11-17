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

using HeapScope.Contracts;
using HeapScope.Worker.Snapshots;
using Microsoft.Extensions.Logging;

namespace HeapScope.Worker.Rpc;

/// <summary>Opening and closing snapshots (<see cref="ISnapshotService"/>).</summary>
internal sealed class SnapshotHandler(
    SnapshotStore snapshots,
    ILogger<SnapshotHandler> logger) : RpcHandler(logger), ISnapshotService
{
    public Task<SnapshotProbe> ProbeSnapshotAsync(string path, CancellationToken cancellationToken) =>
        GuardAsync(() => Task.Run(() => snapshots.Probe(path), cancellationToken), LogLevel.Debug);

    public Task<SnapshotDescriptor> OpenSnapshotAsync(OpenSnapshotRequest request, IProgress<JobProgress>? progress, CancellationToken cancellationToken) =>
        GuardAsync(request, () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            Logger.LogInformation("Opening snapshot {Path} (rebuild {Rebuild}, mode {Mode}, reopen {Reopen})", request.Path, request.Rebuild, request.Mode, request.RequireCache);
            return snapshots.OpenAsync(request, progress, cancellationToken);
        });

    /// <summary>Closes the snapshot; the store drops its derived data and its object sets with it.</summary>
    /// <remarks>Through the guard like every method (sprint 3.12 review M17): a failure is logged with its id, as B5 shows it.</remarks>
    public Task CloseSnapshotAsync(Guid snapshotId, CancellationToken cancellationToken) =>
        GuardAsync(
            snapshotId,
            () =>
            {
                snapshots.Close(snapshotId);
                return Task.FromResult(true);
            },
            LogLevel.Debug);
}
