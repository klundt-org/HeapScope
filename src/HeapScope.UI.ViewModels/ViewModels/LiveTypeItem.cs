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

using CommunityToolkit.Mvvm.ComponentModel;

namespace HeapScope.UI.ViewModels;

/// <summary>A type a watched process allocates (sprint 4.3): one row whose figures change as samples arrive.</summary>
public sealed partial class LiveTypeItem(string name) : ObservableObject
{
    [ObservableProperty]
    private long _bytes;

    [ObservableProperty]
    private long _samples;

    /// <summary>Its share of the interval's sampled bytes.</summary>
    [ObservableProperty]
    private double _percent;

    public string Name { get; } = name;

    public void Update(long bytes, long samples, long total) =>
        (Bytes, Samples, Percent) = (bytes, samples, total == 0 ? 0 : 100.0 * bytes / total);
}
