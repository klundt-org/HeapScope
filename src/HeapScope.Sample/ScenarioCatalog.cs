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

using Fixtures.Leaks;

namespace HeapScope.Sample;

/// <summary>What a scenario is started with.</summary>
/// <param name="Count">Items each leak plants at the start.</param>
/// <param name="Payload">Bytes of each item's payload.</param>
internal sealed record ScenarioOptions(int Count, int Payload);

/// <summary>
/// One problem the sample can plant (sprint 3.13, ADR-042): its name (what the app and the lessons use), its title, what it
/// plants, the known answer in fixtures/KnownAnswers that describes it (null for the sample's own scenarios), the type a
/// lesson's answer names, the scenarios it cannot run with and why, and its decoys: other types of the sample that a lesson's
/// steps rule out.
/// <paramref name="Start"/> plants it; <paramref name="Grow"/>, when the scenario can keep growing, adds a step of it;
/// <paramref name="AfterLastCollection"/> plants what must come after the sample's last full collection (what blocks the
/// finalizer thread for good).
/// </summary>
internal sealed record Scenario(
    string Name,
    string Title,
    string Plants,
    string? KnownAnswer,
    string AnswerType,
    IReadOnlyList<string> CannotRunWith,
    string? ConflictReason,
    IReadOnlyList<string> Decoys,
    Action<ScenarioOptions> Start,
    Action<int, ScenarioOptions>? Grow = null,
    Action<ScenarioOptions>? AfterLastCollection = null);

/// <summary>
/// Every scenario the sample can run, the catalog the app reads (<c>--list</c>) and the lessons name. A new feature's scenario
/// is one entry here, with its known answer and its lesson (ADR-042's extension contract).
/// </summary>
internal static class ScenarioCatalog
{
    /// <summary>The catalog's version (sprint 3.13 review M4): raised when a scenario is added or changes what it plants.</summary>
    public const int Version = 1;

    private const string FinalizerConflict = "both block the finalizer thread";

    public static IReadOnlyList<Scenario> All { get; } =
    [
        Leak(new StaticCacheLeak(), "A static cache that only grows", "A static dictionary of entries, each with a byte payload, that nothing removes.", "Fixtures.Leaks.StaticCacheEntry", ["Fixtures.Leaks.EntryMetadata", "Fixtures.Leaks.StaticCacheLeak"]),
        Leak(new EventHandlerLeak(), "Event handlers never removed", "Short-lived subscribers attached to long-lived publishers and never detached.", "Fixtures.Leaks.LeakySubscriber", ["Fixtures.Leaks.LongLivedPublisher", "Fixtures.Leaks.EventHandlerLeak"]),
        Leak(new ClosureLeak(), "A closure that holds its data", "Lambdas stored in a static list, each capturing a buffer.", "Fixtures.Leaks.CapturedState", ["Fixtures.Leaks.ClosureLeak"]),
        Leak(new UndisposedListLeak(), "Undisposed objects kept in a list", "Disposable objects added to a static list and never disposed or removed.", "Fixtures.Leaks.UndisposedResource", ["Fixtures.Leaks.UndisposedListLeak"]),
        Leak(new UnboundedDictionaryLeak(), "A dictionary keyed without bound", "A static dictionary whose keys never repeat, so it grows with every request.", "Fixtures.Leaks.SessionRecord", ["Fixtures.Leaks.UnboundedDictionaryLeak"]),
        Leak(new TimerLeak(), "Timers never stopped", "Periodic timers whose callbacks keep their owners alive.", "Fixtures.Leaks.TimerTarget", ["Fixtures.Leaks.TimerLeak"]),
        Leak(new DisposedReferencedLeak(), "Disposed objects still referenced", "Objects disposed but still held by a static list.", "Fixtures.Leaks.DisposedResource", ["Fixtures.Leaks.DisposedReferencedLeak"]),
        Leak(new PendingAsyncLeak(), "Async methods that never finish", "Async methods awaiting tasks that never complete, kept alive by their continuations.", "Fixtures.Leaks.PendingAsyncLeak", ["Fixtures.Leaks.PendingWork"]),
        new(
            "FinalizerBacklog",
            "A finalizer queue that never drains",
            "Finalizable objects queued behind a finalizer that never returns.",
            "FinalizerBacklog",
            "Fixtures.Leaks.FinalizableItem",
            ["Waste"],
            FinalizerConflict,
            ["Fixtures.Leaks.FinalizerBacklogLeak"],
            static _ => { },
            AfterLastCollection: static options => new FinalizerBacklogLeak().Grow(options.Count, options.Payload)),
        new(
            "Waste",
            "Memory wasted by shape",
            "Duplicate strings, empty and oversized collections of items, sparse and large arrays, and a finalizer backlog of its own.",
            "Waste",
            "Fixtures.Leaks.WasteItem",
            ["FinalizerBacklog"],
            FinalizerConflict,
            ["Fixtures.Leaks.WasteHolder", "Fixtures.Leaks.WasteFinalizable"],
            static _ => WasteFixtures.Create(),
            AfterLastCollection: static _ => WasteFixtures.CreateFinalizerBacklog()),
        new(
            "LargeObjects",
            "Large arrays on the large object heap",
            "Arrays of readings big enough for the large object heap, beside many small arrays of tags.",
            null,
            LargeObjects.AnswerType,
            [],
            null,
            [LargeObjects.SmallType],
            static _ => LargeObjects.Start()),
        new(
            "Inspection",
            "An object with fields of every kind",
            "One object with fields of every kind (numbers, text, an enum, nested objects and arrays), to read in the object view.",
            "Inspection",
            "Fixtures.Leaks.InspectionSample",
            [],
            null,
            ["Fixtures.Leaks.InspectionChild"],
            static _ => InspectionFixtures.Create()),
        new(
            "SteadyAllocation",
            "Steady allocation from one place",
            "Orders priced over and over, each allocating a short-lived result, so an allocation trace names the call site.",
            null,
            SteadyAllocation.AnswerType,
            [],
            null,
            [SteadyAllocation.LineType],
            static _ => SteadyAllocation.Start()),
    ];

    /// <summary>The scenario named <paramref name="name"/> (without regard to case), or null.</summary>
    public static Scenario? Find(string name) => All.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static Scenario Leak(ILeak leak, string title, string plants, string answerType, IReadOnlyList<string> decoys) =>
        new(
            leak.Name,
            title,
            plants,
            leak.Name,
            answerType,
            [],
            null,
            decoys,
            options => leak.Grow(options.Count, options.Payload),
            (step, options) => leak.Grow(step, options.Payload));
}
