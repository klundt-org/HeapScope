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
/// The steady allocation scenario (sprint 3.13): a background thread prices orders without end. Each order's lines live on
/// the stack and its priced result on the heap for a moment, and one in a hundred results is kept in a bounded window, so an
/// allocation trace names <see cref="PricedOrder"/> as the type allocated most and <see cref="PriceOrder"/> as its call site,
/// while the heap stays about the same size. (Review H1: the lines were objects too, and outnumbered the result.)
/// </summary>
internal static class SteadyAllocation
{
    /// <summary>The type a lesson's answer names: the only object <see cref="PriceOrder"/> allocates.</summary>
    public const string AnswerType = "HeapScope.Sample.PricedOrder";

    /// <summary>The order lines' type, a decoy: a struct on the stack, never allocated on the heap.</summary>
    public const string LineType = "HeapScope.Sample.OrderLine";

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
        Span<OrderLine> lines = stackalloc OrderLine[8];
        var total = 0L;
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = new OrderLine(random.Next(1, 10), random.Next(100, 10_000));
            total += (long)lines[i].Quantity * lines[i].Cents;
        }

        return new PricedOrder(id, lines.Length, total);
    }
}

/// <summary>One line of an order, on the stack.</summary>
internal readonly record struct OrderLine(int Quantity, int Cents);

/// <summary>An order with its total, the one object the steady allocation scenario allocates.</summary>
internal sealed record PricedOrder(long Id, int Lines, long TotalCents);
