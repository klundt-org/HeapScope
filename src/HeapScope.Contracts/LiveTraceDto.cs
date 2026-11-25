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

namespace HeapScope.Contracts;

/// <summary>Where a live trace stands.</summary>
/// <param name="Version">Grows with each sample: what was asked of an older version has changed since.</param>
/// <param name="ProcessId">The traced process, or 0 before its first sample.</param>
/// <param name="StartUtc">When the recording started, or null before its first event; the recording's identity when it is read again.</param>
/// <param name="DurationSeconds">The time of the last event read.</param>
/// <param name="BucketSeconds">How long one stretch of time is: an interval counts whole stretches, by their start.</param>
/// <param name="SamplesUnnamed">Samples whose stack has a frame that has no name yet.</param>
/// <param name="SamplesPastLimits">Samples added up under "other" types or stacks, past what a live trace keeps apart.</param>
/// <param name="DelaySeconds">The longest a sample took to get here since the last time this was asked; null without samples since.</param>
/// <param name="CaughtUp">Whether everything in the file was read.</param>
/// <param name="Completed">Whether reading is over: the recording ended and everything was read, or it stopped (<paramref name="Problem"/>).</param>
/// <param name="HasStacks">Whether call stacks are read (not with a TraceEvent whose record is another; the log says why).</param>
/// <param name="Problem">Why reading stopped before the recording's end, or null.</param>
public sealed record LiveTraceDto(
    long Version,
    int ProcessId,
    string? ProcessName,
    DateTimeOffset? StartUtc,
    double DurationSeconds,
    double BucketSeconds,
    long Samples,
    long SampledBytes,
    int Types,
    long LostEvents,
    long SamplesWithoutStack,
    long SamplesUnnamed,
    long SamplesPastLimits,
    double? DelaySeconds,
    bool CaughtUp,
    bool Completed,
    bool HasStacks,
    string? Problem);
