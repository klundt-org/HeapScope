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
/// The churn scenario (sprint 4.4): exchange rates allocated all the time, of which only the latest are kept, and trades kept as they
/// come. Between two snapshots the trades grow and the rates do not, yet nearly everything allocated is rates: what a comparison of
/// heaps cannot show and the allocations between them do.
/// </summary>
internal static class Churn
{
    /// <summary>The type a lesson's answer names: what is allocated and not kept.</summary>
    public const string AnswerType = "HeapScope.Sample.ExchangeRate";

    /// <summary>The decoy: what grows.</summary>
    public const string GrowingType = "HeapScope.Sample.Trade";

    private const int RatesPerTick = 10_000;
    private const int RatesKept = 1_000;
    private const int TradesPerTick = 50;

    /// <summary>The trades are no more than this, about 15 MB.</summary>
    private const int MaxTrades = 300_000;
    private const int TickMs = 15;

    private static readonly ExchangeRate[] s_latest = new ExchangeRate[RatesKept];
    private static readonly List<Trade> s_trades = [];
    private static int s_started;

    /// <summary>Starts the thread once.</summary>
    public static void Start()
    {
        if (Interlocked.Exchange(ref s_started, 1) == 1)
        {
            return;
        }

        new Thread(Run) { IsBackground = true, Name = "HeapScope sample churn" }.Start();
    }

    private static void Run()
    {
        while (true)
        {
            Quote(RatesPerTick);
            Book(TradesPerTick);
            Thread.Sleep(TickMs);
        }
    }

    /// <summary>The call site of what is not kept.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static void Quote(int count)
    {
        for (var i = 0; i < count; i++)
        {
            s_latest[i % RatesKept] = new ExchangeRate(i, 1.25, 1.26, 1.24, i);
        }
    }

    /// <summary>The call site of what is kept; the trades booked so far.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static int Book(int count)
    {
        for (var i = 0; i < count && s_trades.Count < MaxTrades; i++)
        {
            s_trades.Add(new Trade(i, i, 1.25));
        }

        return s_trades.Count;
    }
}

/// <summary>What the scenario allocates all the time and keeps only the latest of.</summary>
internal sealed record ExchangeRate(long Pair, double Middle, double Ask, double Bid, long Time);

/// <summary>What the scenario keeps.</summary>
internal sealed record Trade(long Number, long Amount, double Rate);
