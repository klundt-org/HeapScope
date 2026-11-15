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

namespace HeapScope.Sample;

/// <summary>
/// The steady allocation scenario (sprint 3.13): a background thread prices orders without end. Each order allocates its
/// lines and a priced result that live a moment, and one in a hundred results is kept in a bounded window, so an allocation
/// trace names <see cref="PriceOrder"/> as the call site that allocates most while the heap stays about the same size.
/// </summary>
internal static class SteadyAllocation
{
    /// <summary>The type a lesson's answer names: what <see cref="PriceOrder"/> allocates most.</summary>
    public const string AnswerType = "HeapScope.Sample.PricedOrder";

    /// <summary>The call site a trace names (its method's full name).</summary>
    public const string CallSite = "HeapScope.Sample.SteadyAllocation.PriceOrder";

    private const int Window = 1_000;

    private static readonly Queue<PricedOrder> s_recent = new();
    private static int s_started;

    /// <summary>Priced since the start, for tests.</summary>
    public static long Priced => Interlocked.Read(ref s_priced);

    private static long s_priced;

    /// <summary>Starts the pricing thread once.</summary>
    public static void Start()
    {
        if (Interlocked.Exchange(ref s_started, 1) == 1)
        {
            return;
        }

        new Thread(Run) { IsBackground = true, Name = "HeapScope sample pricing" }.Start();
    }

    private static void Run()
    {
        var random = new Random(42);
        while (true)
        {
            var order = PriceOrder(random);
            if (order.Id % 100 == 0)
            {
                s_recent.Enqueue(order);
                if (s_recent.Count > Window)
                {
                    s_recent.Dequeue();
                }
            }

            if (order.Id % 1_000 == 0)
            {
                Thread.Sleep(1);
            }
        }
    }

    /// <summary>Prices one order: the call site the scenario plants.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static PricedOrder PriceOrder(Random random)
    {
        var id = Interlocked.Increment(ref s_priced);
        var lines = new OrderLine[8];
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = new OrderLine(random.Next(1, 10), random.Next(100, 10_000));
        }

        return new PricedOrder(id, lines, lines.Sum(l => (long)l.Quantity * l.Cents));
    }
}

/// <summary>One line of an order.</summary>
internal sealed record OrderLine(int Quantity, int Cents);

/// <summary>An order with its total, what the steady allocation scenario allocates most.</summary>
internal sealed record PricedOrder(long Id, OrderLine[] Lines, long TotalCents);
