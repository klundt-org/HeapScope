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

namespace HeapScope.UI.ViewModels;

/// <summary>A heap kind in the fragmentation bars: live and free bytes, and what is pinned there.</summary>
public sealed record FragmentItem(string Heap, long Live, long? Free, long PinnedObjects, long PinnedBytes)
{
    /// <summary>The free share of the heap (0 when unknown), for the bar's width.</summary>
    public double FreeFraction => Free is { } free && free + Live > 0 ? (double)free / (free + Live) : 0;

    public string Text => string.Create(CultureInfo.CurrentCulture,
        $"{Heap}: {Format.Bytes(Live)} live, {(Free is { } free ? Format.Bytes(free) + $" free ({FreeFraction:P0})" : "free unknown")}{(PinnedObjects > 0 ? $", {PinnedObjects:N0} pinned ({Format.Bytes(PinnedBytes)})" : string.Empty)}");
}
