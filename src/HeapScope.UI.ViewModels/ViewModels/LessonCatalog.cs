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

namespace HeapScope.UI.ViewModels;

/// <summary>
/// Every lesson of the Learn page (sprint 3.13, ADR-042), one per scenario of the sample. A feature a user would learn adds its
/// lesson here (or a reason in <see cref="NoLesson"/>), with its texts approved in docs/design/info-text.md; tests keep the
/// lessons and the sample's scenarios consistent, and an integration test follows each lesson's steps on a real sample
/// (review H1: the steps must lead to the answer they check).
/// </summary>
public static class LessonCatalog
{
    /// <summary>The catalog's version (review M4): raised when a lesson is added or its steps or question change.</summary>
    public const int Version = 2;

    private const string Capture = "On the Processes page, select the sample's row (it says Sample in the Kind column) and choose Capture dump (leave As gcdump unticked). When it asks to open the dump, say yes: the dump opens on the Snapshots page.";
    private const string ByRetained = "On the Snapshots page, tick Retained sizes, type Fixtures.Leaks in the filter and sort by Retained. The first time, working out retained sizes can take a minute; the bar above the grid moves until it is done.";
    private const string Inspections = "On the Snapshots page, open the Inspections tab and select ";

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
                ByRetained,
                "Among the rows whose names start with Fixtures.Leaks, the top one is what the cache keeps: double-click it and choose Why alive to see the static field that holds it.",
            ],
            "Which of the sample's own types (names starting with Fixtures.Leaks) retains the most?",
            "StaticCache"),
        new(
            "event-handlers",
            "Event handlers never removed",
            "Find subscribers that a long-lived publisher keeps alive.",
            ["EventHandler"],
            false,
            LessonPage.Processes,
            [Capture, Inspections + "Event handler leaks.", "Its row names the event, and under Subscriber types what the event keeps alive."],
            "Which type does the inspection list as the subscribers the event keeps alive?",
            "EventHandler"),
        new(
            "closure",
            "A closure that holds its data",
            "Follow a retention path through a compiler-generated closure.",
            ["Closure"],
            false,
            LessonPage.Processes,
            [
                Capture,
                ByRetained,
                "The top row is a display class (its name holds <>c__DisplayClass): the compiler made it to hold what a lambda captured. Double-click it, then double-click one of its objects: its field holds the captured object.",
            ],
            "Which of the sample's types do the lambdas capture?",
            "Closure"),
        new(
            "undisposed-list",
            "Objects that are never disposed",
            "Find disposable objects a list keeps after they are no longer used.",
            ["UndisposedList"],
            false,
            LessonPage.Processes,
            [
                Capture,
                "On the Snapshots page, type Fixtures.Leaks in the filter: the type with thousands of objects is what the list keeps.",
                "Double-click it and choose Why alive: a static list holds every one.",
            ],
            "Which of the sample's types does the static list keep without disposing it?",
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
                "On the Compare tab, compare the second with the first, type Fixtures.Leaks in the filter and sort by Bytes +/-: among the rows whose names start with Fixtures.Leaks, the top one grows.",
            ],
            "Which of the sample's own types (names starting with Fixtures.Leaks) grows between the two captures?",
            "UnboundedDictionary"),
        new(
            "timers",
            "Timers never stopped",
            "Find the objects that running timers keep alive.",
            ["Timer"],
            false,
            LessonPage.Processes,
            [Capture, Inspections + "Timer leaks.", "Its row's Target is what the timers' callbacks keep alive."],
            "Which type do the timers keep alive?",
            "Timer"),
        new(
            "disposed-referenced",
            "Disposed objects still referenced",
            "Find objects that were disposed but are still held.",
            ["DisposedReferenced"],
            false,
            LessonPage.Processes,
            [Capture, Inspections + "Disposed but referenced.", "Its row names the type whose objects were disposed and are still held."],
            "Which type is disposed but still referenced?",
            "DisposedReferenced"),
        new(
            "pending-async",
            "Async methods that never finish",
            "Find async methods waiting on tasks that never complete.",
            ["PendingAsync"],
            false,
            LessonPage.Processes,
            [Capture, Inspections + "Async methods that never complete.", "Its row names the waiting method as type and method, and how many calls wait."],
            "Which of the sample's types declares the async method that never completes?",
            "PendingAsync"),
        new(
            "finalizer-backlog",
            "A finalizer queue that never drains",
            "Find objects waiting for a finalizer that never returns.",
            ["FinalizerBacklog"],
            false,
            LessonPage.Processes,
            [Capture, Inspections + "Finalizer backlog.", "Its row names the type whose objects wait on the finalizer queue."],
            "Which type waits on the finalizer queue?",
            "FinalizerBacklog"),
        new(
            "waste",
            "Memory wasted by shape",
            "Find collections that hold nothing, or far less than they have room for.",
            ["Waste"],
            false,
            LessonPage.Processes,
            [
                Capture,
                Inspections + "Empty collections, then Oversized collections.",
                "Their rows name each collection's type, with what it was made to hold in angle brackets.",
            ],
            "Which of the sample's types were the empty and oversized collections made to hold?",
            "Waste"),
        new(
            "large-objects",
            "Large arrays on the large object heap",
            "Find which arrays are big enough for the large object heap.",
            ["LargeObjects"],
            false,
            LessonPage.Processes,
            [
                Capture,
                Inspections + "Large arrays.",
                "Its rows are the arrays of 85,000 bytes or more, which live on the large object heap; the sample's small arrays are not listed.",
            ],
            "Which arrays are on the large object heap?",
            "LargeObjects"),
        new(
            "steady-allocation",
            "Where allocations come from",
            "Record allocations and find the type and call site that allocate most.",
            ["SteadyAllocation"],
            false,
            LessonPage.Processes,
            [
                "On the Processes page, select the sample's row (it says Sample in the Kind column) and choose Record allocations; stop after a few seconds.",
                "On the Allocations page, the Types list is ordered by bytes: find the sample's type nearest the top, select it and look at Back traces for the method that allocates it.",
            ],
            "Which of the sample's types is allocated most?",
            "SteadyAllocation"),
    ];

    /// <summary>The scenarios without a lesson, with the reason (ADR-042: each feature adds its lesson or says why not).</summary>
    public static IReadOnlyDictionary<string, string> NoLesson { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Inspection"] = "It plants one object for reading field values in the object view, which no question checks yet; a lesson on reading an object's fields is in the Backlog.",
    };

    /// <summary>The lesson <paramref name="id"/>, or null.</summary>
    public static Lesson? Find(string id) => All.FirstOrDefault(l => l.Id == id);
}
