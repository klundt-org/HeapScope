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

using Fixtures.Leaks;

namespace HeapScope.Sample;

/// <summary>What a scenario is started with.</summary>
/// <param name="Count">Items each leak plants at the start.</param>
/// <param name="Payload">Bytes of each item's payload.</param>
internal sealed record ScenarioOptions(int Count, int Payload);

/// <summary>
/// One problem the sample can plant (sprint 3.13, ADR-042): its name (what the app and the lessons use), its title, what it
/// plants, the known answer in fixtures/KnownAnswers that describes it (null when it plants allocations, not objects), the type
/// a lesson's answer names, and the scenarios it cannot run with.
/// <paramref name="Start"/> plants it; <paramref name="Grow"/>, when the scenario can keep growing, adds a step of it.
/// <paramref name="AfterLastCollection"/> scenarios start after the sample's last full collection (they block the finalizer
/// thread for good).
/// </summary>
internal sealed record Scenario(
    string Name,
    string Title,
    string Plants,
    string? KnownAnswer,
    string AnswerType,
    IReadOnlyList<string> CannotRunWith,
    Action<ScenarioOptions> Start,
    Action<int, ScenarioOptions>? Grow = null,
    bool AfterLastCollection = false);

/// <summary>
/// Every scenario the sample can run, the catalog the app reads (<c>--list</c>) and the lessons name. A new feature's scenario
/// is one entry here, with its known answer and its lesson (ADR-042's extension contract).
/// </summary>
internal static class ScenarioCatalog
{
    public static IReadOnlyList<Scenario> All { get; } =
    [
        Leak(new StaticCacheLeak(), "A static cache that only grows", "A static dictionary of entries, each with a byte payload, that nothing removes.", "Fixtures.Leaks.StaticCacheEntry"),
        Leak(new EventHandlerLeak(), "Event handlers never removed", "Short-lived subscribers attached to long-lived publishers and never detached.", "Fixtures.Leaks.LeakySubscriber"),
        Leak(new ClosureLeak(), "A closure that holds its data", "Lambdas stored in a static list, each capturing a buffer.", "Fixtures.Leaks.CapturedState"),
        Leak(new UndisposedListLeak(), "Undisposed objects kept in a list", "Disposable objects added to a static list and never disposed or removed.", "Fixtures.Leaks.UndisposedResource"),
        Leak(new UnboundedDictionaryLeak(), "A dictionary keyed without bound", "A static dictionary whose keys never repeat, so it grows with every request.", "Fixtures.Leaks.SessionRecord"),
        Leak(new TimerLeak(), "Timers never stopped", "Periodic timers whose callbacks keep their owners alive.", "Fixtures.Leaks.TimerTarget"),
        Leak(new DisposedReferencedLeak(), "Disposed objects still referenced", "Objects disposed but still held by a static list.", "Fixtures.Leaks.DisposedResource"),
        Leak(new PendingAsyncLeak(), "Async methods that never finish", "Async methods awaiting tasks that never complete, kept alive by their continuations.", "Fixtures.Leaks.PendingWork"),
        new(
            "FinalizerBacklog",
            "A finalizer queue that never drains",
            "Finalizable objects queued behind a finalizer that never returns.",
            "FinalizerBacklog",
            "Fixtures.Leaks.FinalizableItem",
            ["Waste"],
            options => new FinalizerBacklogLeak().Grow(options.Count, options.Payload),
            AfterLastCollection: true),
        new(
            "Waste",
            "Memory wasted by shape",
            "Duplicate strings, empty and oversized collections, sparse arrays and a finalizer backlog of its own.",
            "Waste",
            "Fixtures.Leaks.WasteHolder",
            ["FinalizerBacklog"],
            _ => WasteFixtures.Create()),
        new(
            "Inspection",
            "Patterns the inspections find",
            "Large arrays, the large object heap, finalizable and pinned objects.",
            "Inspection",
            "Fixtures.Leaks.InspectionSample",
            [],
            _ => InspectionFixtures.Create()),
        new(
            "SteadyAllocation",
            "Steady allocation from one place",
            "Orders priced over and over, each allocating short-lived objects, so an allocation trace names the call site.",
            null,
            SteadyAllocation.AnswerType,
            [],
            _ => SteadyAllocation.Start()),
    ];

    /// <summary>The scenario named <paramref name="name"/> (without regard to case), or null.</summary>
    public static Scenario? Find(string name) => All.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static Scenario Leak(ILeak leak, string title, string plants, string answerType) =>
        new(
            leak.Name,
            title,
            plants,
            leak.Name,
            answerType,
            [],
            options => leak.Grow(options.Count, options.Payload),
            (step, options) => leak.Grow(step, options.Payload));
}
