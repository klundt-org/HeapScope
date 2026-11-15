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
/// One lesson of the Learn page (sprint 3.13, ADR-042): the sample's scenarios it starts, the page it starts on, its steps, and
/// a question whose answer is the planted type of <paramref name="AnswerScenario"/> (read from the sample's catalog, so the
/// lesson and the sample never disagree). The check does not watch what the user clicks: whatever pages they use, the answer
/// is the type they found. A new feature's lesson is one entry in <see cref="LessonCatalog"/>.
/// </summary>
/// <param name="Id">Stable, for the progress file.</param>
/// <param name="Grows">Whether its sample keeps growing (for lessons about Live, traces, Compare and Trend).</param>
public sealed record Lesson(
    string Id,
    string Title,
    string Summary,
    IReadOnlyList<string> Scenarios,
    bool Grows,
    LessonPage StartPage,
    IReadOnlyList<string> Steps,
    string Question,
    string AnswerScenario)
{
    /// <summary>Types every .NET heap holds: the wrong answers beside the planted one.</summary>
    public static IReadOnlyList<string> Decoys { get; } = ["System.String", "System.Byte[]", "System.Object[]"];

    /// <summary>The choices for <paramref name="answerType"/>: it and the decoys, in an order that depends on the lesson only.</summary>
    public IReadOnlyList<string> Choices(string answerType)
    {
        ArgumentNullException.ThrowIfNull(answerType);
        var choices = Decoys.Append(answerType).ToList();
        var shift = Id.Sum(c => c) % choices.Count;
        return [.. choices.Skip(shift), .. choices.Take(shift)];
    }

    /// <summary>Whether <paramref name="chosen"/> is the planted type.</summary>
    public static bool IsRight(string? chosen, string answerType) => string.Equals(chosen, answerType, StringComparison.Ordinal);
}
