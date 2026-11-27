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

namespace HeapScope.Application.Allocations;

/// <summary>What a live trace's handle remembers, to follow it again after a worker restart (ADR-037).</summary>
internal sealed class LiveTraceSource(string? namesPath)
{
    public string? NamesPath { get; } = namesPath;

    /// <summary>The recording ended: a worker that follows it again is told at once.</summary>
    public volatile bool Ended;

    private DateTimeOffset? _start;

    /// <summary>When the recording first followed started, once known.</summary>
    public DateTimeOffset? StartUtc => _start;

    /// <summary>
    /// The recording's own start is its identity: false when <paramref name="state"/> is of another recording than the first that
    /// said one (the file only ever grows, so its size and time say nothing).
    /// </summary>
    public bool IsSame(LiveTraceDto state)
    {
        if (state.StartUtc is not { } start)
        {
            return true;
        }

        _start ??= start;
        return _start == start;
    }
}
