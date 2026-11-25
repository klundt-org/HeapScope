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

namespace HeapScope.Loaders.Trace;

/// <summary>Where a live trace stands (sprint 4.3).</summary>
/// <param name="Version">Grows with each sample.</param>
/// <param name="DurationMs">The time of the last event read, since the recording started.</param>
/// <param name="BucketMs">How long one stretch of time is; it doubles as a run gets long.</param>
/// <param name="DelayMs">The longest a sample took from the process to here, since the last time this was asked; null without samples.</param>
/// <param name="LongestDelayMs">The longest of the whole recording, the catching up after a worker restart included.</param>
/// <param name="MethodsLeftOut">Methods past what is kept: their frames stay unnamed.</param>
/// <param name="CaughtUp">Whether everything in the file was read.</param>
/// <param name="Completed">Whether the reader is done: the recording ended and everything was read, or it failed.</param>
/// <param name="Problem">Why reading stopped before the recording's end, for the user.</param>
/// <param name="ProblemDetail">What that rests on, for the log.</param>
/// <param name="StackProblem">Why there are no stacks, for the log.</param>
/// <param name="NamesProblem">Why the methods compiled before are not named, or not all, for the log.</param>
/// <param name="NamedBefore">Methods the names file named.</param>
public sealed record LiveTraceStatus(
    long Version,
    double DurationMs,
    double BucketMs,
    long Samples,
    long Bytes,
    int Types,
    int Entries,
    int StackNodes,
    int Methods,
    long MethodsLeftOut,
    long LostEvents,
    long SamplesWithoutStack,
    long SamplesUnnamed,
    long SamplesPastLimits,
    double? DelayMs,
    double LongestDelayMs,
    bool CaughtUp,
    bool Completed,
    long BytesRead,
    string? Checked,
    string? Problem,
    string? ProblemDetail,
    string? StackProblem,
    string? NamesProblem,
    int NamedBefore);
