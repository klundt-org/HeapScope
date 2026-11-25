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

namespace HeapScope.Sample;

/// <summary>
/// The allocation bursts scenario (sprint 4.3): one thread allocates heartbeats evenly and another builds a report every ten
/// seconds, in a burst of a second. Over the whole run the heartbeats are the type allocated most; within a burst the report's
/// rows are, so only an interval of the timeline shows what the burst is. Nothing is kept, so the heap stays about the same size.
/// </summary>
internal static class AllocationBursts
{
    /// <summary>The type a lesson's answer names: what a burst allocates.</summary>
    public const string AnswerType = "HeapScope.Sample.ReportRow";

    /// <summary>The decoy: the type allocated most over the whole run.</summary>
    public const string SteadyType = "HeapScope.Sample.Heartbeat";

    /// <summary>When the first burst comes, and how long it is from one to the next.</summary>
    private static readonly TimeSpan s_first = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan s_every = TimeSpan.FromSeconds(10);

    private const int HeartbeatsPerTick = 2_000;
    private const int RowsPerTick = 4_000;
    private const int TicksPerBurst = 60;
    private const int TickMs = 15;

    private static int s_started;
    private static long s_sink;

    /// <summary>Starts both threads once.</summary>
    public static void Start()
    {
        if (Interlocked.Exchange(ref s_started, 1) == 1)
        {
            return;
        }

        new Thread(Beat) { IsBackground = true, Name = "HeapScope sample heartbeats" }.Start();
        new Thread(Report) { IsBackground = true, Name = "HeapScope sample reports" }.Start();
    }

    private static void Beat()
    {
        while (true)
        {
            s_sink += BeatOnce(HeartbeatsPerTick);
            Thread.Sleep(TickMs);
        }
    }

    private static void Report()
    {
        Thread.Sleep(s_first);
        while (true)
        {
            var started = Environment.TickCount64;
            for (var i = 0; i < TicksPerBurst; i++)
            {
                s_sink += BuildReport(RowsPerTick);
                Thread.Sleep(TickMs);
            }

            Thread.Sleep(Math.Max(0, (int)(s_every.TotalMilliseconds - (Environment.TickCount64 - started))));
        }
    }

    /// <summary>The steady call site.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static long BeatOnce(int count)
    {
        var sum = 0L;
        for (var i = 0; i < count; i++)
        {
            sum += new Heartbeat(i, sum, 1).Sequence;
        }

        return sum;
    }

    /// <summary>The burst's call site.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static long BuildReport(int rows)
    {
        var sum = 0L;
        for (var i = 0; i < rows; i++)
        {
            sum += new ReportRow(i, sum, 1, 2, 3).Line;
        }

        return sum;
    }
}

/// <summary>What the scenario allocates evenly.</summary>
internal sealed record Heartbeat(long Sequence, long Time, long Status);

/// <summary>What a burst allocates: a row of a report that is built and dropped.</summary>
internal sealed record ReportRow(long Line, long Account, long Debit, long Credit, long Balance);
