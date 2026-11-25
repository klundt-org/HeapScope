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

using System.Globalization;
using HeapScope.Application.Presentation;

namespace HeapScope.UI.ViewModels;

/// <summary>A snapshot a rule took during the run, as a card: when, which rule, how big, or why it failed.</summary>
public sealed record SnapshotCard(DateTimeOffset At, string Rule, string? Path, long SizeBytes, string? Error)
{
    public string Time => At.ToLocalTime().ToString("T", CultureInfo.CurrentCulture);

    public string Summary => Error is null ? $"{Format.Bytes(SizeBytes)}, {System.IO.Path.GetFileName(Path)}" : "failed: " + Error;
}
