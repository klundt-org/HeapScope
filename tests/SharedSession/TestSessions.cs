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

global using HeapScope.Tests.Shared;

using HeapScope.Application.Jobs;
using HeapScope.Application.Snapshots;
using HeapScope.Application.Workers;
using Microsoft.Extensions.Logging;

namespace HeapScope.Tests.Shared;

/// <summary>
/// The one way tests make a <see cref="SnapshotSession"/> (sprint 3.12): it reads a fixed machine with room to spare, never
/// the machine's own free memory, so a busy machine cannot make an open ask the low memory question first. A test of that
/// question passes its own <paramref name="memory"/>. A test in the architecture project fails on any other
/// <c>new SnapshotSession(</c> under <c>tests/</c>.
/// </summary>
internal static class TestSessions
{
    /// <summary>32 GB with 16 GB free: enough for every dump the tests open.</summary>
    public static PhysicalMemory Roomy { get; } = new(32L << 30, 16L << 30);

    public static SnapshotSession Create(
        IWorkerClient worker,
        JobTracker jobs,
        Func<PhysicalMemory>? memory = null,
        ILogger<SnapshotSession>? logger = null,
        TrustedDacs? trustedDacs = null)
        => new(worker, jobs, memory ?? (static () => Roomy), logger, trustedDacs);
}
