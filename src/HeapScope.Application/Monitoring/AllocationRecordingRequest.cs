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

using HeapScope.Capture;

namespace HeapScope.Application.Monitoring;

/// <summary>A monitoring run's allocation trace to record (sprint 4.3): live, into <paramref name="Path"/> itself.</summary>
/// <param name="CounterInterval">The run's own interval: the process's counters have one, and the shortest wins.</param>
/// <param name="NoNames">Why the methods compiled before are not to be named, or null.</param>
/// <param name="Started">To be called once the recording has started.</param>
public sealed record AllocationRecordingRequest(int ProcessId, string Path, TimeSpan CounterInterval, string? NoNames, Action<LiveTraceStart> Started);
