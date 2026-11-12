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

using System.Collections.Concurrent;
using HeapScope.Capture;

namespace HeapScope.Tests.Shared;

/// <summary>
/// A dump writer that waits for the test before it lets the real one write (sprint 3.12): <see cref="Started"/> completes when a
/// capture reaches the writer, and <see cref="Release"/> or <see cref="Fail"/> lets it go on. Each process has one write held at a
/// time; once it ends, the next write of that process waits for a new release. The integration tests wrap the runtime's own
/// writer in it, so a cancel lands while the dump is written however fast the machine is.
/// </summary>
internal sealed class HeldDumpWriter(IDumpWriter inner) : IDumpWriter
{
    private readonly ConcurrentDictionary<int, Hold> _holds = new();
    private readonly ConcurrentDictionary<int, int> _completed = new();

    public Task Started(int processId) => Current(processId).Started.Task;

    public void Release(int processId) => Current(processId).Go.TrySetResult(null);

    public void Fail(int processId, Exception error) => Current(processId).Go.TrySetResult(error);

    /// <summary>How many writes of the process the inner writer finished.</summary>
    public int Completed(int processId) => _completed.GetValueOrDefault(processId);

    public void ReleaseAll()
    {
        foreach (var hold in _holds.Values)
        {
            hold.Go.TrySetResult(null);
        }
    }

    public async Task WriteAsync(int processId, string path)
    {
        var hold = Current(processId);
        hold.Started.TrySetResult();
        var error = await hold.Go.Task.ConfigureAwait(false);
        try
        {
            if (error is not null)
            {
                throw error;
            }

            await inner.WriteAsync(processId, path).ConfigureAwait(false);
            _completed.AddOrUpdate(processId, 1, static (_, n) => n + 1);
        }
        finally
        {
            hold.Ended = true;
        }
    }

    private Hold Current(int processId) => _holds.AddOrUpdate(processId, static _ => new Hold(), static (_, h) => h.Ended ? new Hold() : h);

    private sealed class Hold
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<Exception?> Go { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public volatile bool Ended;
    }
}
