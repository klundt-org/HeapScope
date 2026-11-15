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

namespace HeapScope.UI.ViewModels;

/// <summary>
/// Every lesson of the Learn page (sprint 3.13, ADR-042), one per scenario of the sample. A feature a user would learn adds its
/// lesson here (or a reason in <see cref="NoLesson"/>), with its texts approved in docs/design/info-text.md; tests keep the
/// lessons and the sample's scenarios consistent.
/// </summary>
public static class LessonCatalog
{
    private const string Capture = "On the Processes page, select the row marked Sample and choose Capture dump, then open the dump.";

    public static IReadOnlyList<Lesson> All { get; } =
    [
        new(
            "static-cache",
            "A cache that never lets go",
            "Find what a static cache keeps alive and why it is never collected.",
            ["StaticCache"],
            false,
            LessonPage.Processes,
            [
                Capture,
                "On the Snapshots page, sort the types by Retained and find the largest of the sample's own types (namespace Fixtures.Leaks).",
                "Double-click it to open its objects, then choose Why alive: the path ends at a static field, which keeps every entry alive.",
            ],
            "Which type does the static cache keep alive?",
            "StaticCache"),
        new(
            "event-handlers",
            "Event handlers never removed",
            "Find subscribers that a long-lived publisher keeps alive.",
            ["EventHandler"],
            false,
            LessonPage.Processes,
            [Capture, "On the Inspections tab, run Event handler leaks.", "Double-click its row to open the subscribers as a set; its Paths view shows the publishers' events holding them."],
            "Which type do the events keep alive?",
            "EventHandler"),
        new(
            "closure",
            "A closure that holds its data",
            "Follow a retention path through a compiler-generated closure.",
            ["Closure"],
            false,
            LessonPage.Processes,
            [Capture, "On the Snapshots page, sort the types by Retained and find the largest of the sample's own types (namespace Fixtures.Leaks).", "Double-click it and choose Why alive: the path goes through a display class, which holds a lambda's captured variables."],
            "Which type do the stored lambdas capture?",
            "Closure"),
        new(
            "undisposed-list",
            "Objects that are never disposed",
            "Find disposable objects a list keeps after they are no longer used.",
            ["UndisposedList"],
            false,
            LessonPage.Processes,
            [Capture, "On the Snapshots page, type Fixtures.Leaks in the filter to see the sample's own types.", "Double-click the largest one and choose Why alive: a static list holds its objects."],
            "Which type does the list keep without disposing it?",
            "UndisposedList"),
        new(
            "unbounded-dictionary",
            "A dictionary that grows with every request",
            "Compare two captures to see which type grows.",
            ["UnboundedDictionary"],
            true,
            LessonPage.Processes,
            [
                Capture,
                "Wait a few seconds while the sample keeps growing, then capture and open a second dump.",
                "On the Compare tab, compare the second with the first and sort by Bytes +/-.",
            ],
            "Which type grows between the two captures?",
            "UnboundedDictionary"),
        new(
            "timers",
            "Timers never stopped",
            "Find the objects that running timers keep alive.",
            ["Timer"],
            false,
            LessonPage.Processes,
            [Capture, "On the Inspections tab, run Timer leaks.", "Double-click its row to open the timers as a set: each timer's callback keeps its owner alive."],
            "Which type do the timers' callbacks keep alive?",
            "Timer"),
        new(
            "disposed-referenced",
            "Disposed objects still referenced",
            "Find objects that were disposed but are still held.",
            ["DisposedReferenced"],
            false,
            LessonPage.Processes,
            [Capture, "On the Inspections tab, run Disposed but referenced.", "Double-click its row to open them as a set; its Paths view shows who still holds them."],
            "Which type is disposed but still referenced?",
            "DisposedReferenced"),
        new(
            "pending-async",
            "Async methods that never finish",
            "Find async methods waiting on tasks that never complete.",
            ["PendingAsync"],
            false,
            LessonPage.Processes,
            [Capture, "On the Inspections tab, run Async methods that never complete.", "Double-click its row to open the waiting state machines as a set: they keep their data alive."],
            "Which type do the waiting methods keep alive?",
            "PendingAsync"),
        new(
            "finalizer-backlog",
            "A finalizer queue that never drains",
            "Find objects waiting for a finalizer that never returns.",
            ["FinalizerBacklog"],
            false,
            LessonPage.Processes,
            [Capture, "On the Inspections tab, run Finalizer backlog.", "Its rows are the objects waiting on the finalizer queue; double-click one to open them as a set."],
            "Which type waits on the finalizer queue?",
            "FinalizerBacklog"),
        new(
            "waste",
            "Memory wasted by shape",
            "Find duplicate strings and collections that hold nothing.",
            ["Waste"],
            false,
            LessonPage.Processes,
            [Capture, "On the Inspections tab, run Duplicate strings, Empty collections and Oversized collections.", "Double-click a row to open its objects as a set; its Paths view shows who holds the waste."],
            "Which of the sample's types holds the wasted collections?",
            "Waste"),
        new(
            "large-objects",
            "Large objects and pinned memory",
            "Find what lives on the large object heap and what is pinned.",
            ["Inspection"],
            false,
            LessonPage.Processes,
            [Capture, "On the Inspections tab, run Large object heap and Large arrays.", "Double-click a row to open its objects as a set; its Paths view shows who holds them."],
            "Which of the sample's types holds the large arrays?",
            "Inspection"),
        new(
            "steady-allocation",
            "Where allocations come from",
            "Record allocations and find the type and call site that allocate most.",
            ["SteadyAllocation"],
            false,
            LessonPage.Processes,
            [
                "On the Processes page, select the row marked Sample and choose Record allocations; stop after a few seconds.",
                "On the Allocations page, sort the types by Bytes and open the top Back traces.",
            ],
            "Which of the sample's types is allocated most?",
            "SteadyAllocation"),
    ];

    /// <summary>The scenarios without a lesson, with the reason (ADR-042: each feature adds its lesson or says why not).</summary>
    public static IReadOnlyDictionary<string, string> NoLesson { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The lesson <paramref name="id"/>, or null.</summary>
    public static Lesson? Find(string id) => All.FirstOrDefault(l => l.Id == id);
}
