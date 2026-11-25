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

/// <summary>What <see cref="TraceFileCheck"/> walked of a trace, for the log.</summary>
/// <param name="End">Why the walk ended: the end of the trace, or where the reader itself stops.</param>
/// <param name="Bytes">How far it got.</param>
/// <param name="Descriptions">Event descriptions read.</param>
/// <param name="Fields">Fields they state.</param>
internal sealed record TraceCheckResult(string End, long Bytes, int Blocks, int Descriptions, long Fields)
{
    /// <summary>The walk reached the trace's end tag.</summary>
    public const string Whole = "whole";

    /// <summary>The file ends inside something: a recording cut short. What came before is read.</summary>
    public const string CutShort = "cut short";

    /// <summary>A tag that is not the format's: the reader stops there too, and what came before is read.</summary>
    public const string OtherTag = "another tag";

    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"check {TraceFileCheck.Version}: {End} at byte {Bytes}, {Blocks} blocks, {Descriptions} descriptions, {Fields} fields");
}
