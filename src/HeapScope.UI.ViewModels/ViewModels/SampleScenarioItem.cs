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
using HeapScope.Application.Tutorial;

namespace HeapScope.UI.ViewModels;

/// <summary>One scenario of the tutorial's sample, to choose before starting it (sprint 3.13).</summary>
public sealed partial class SampleScenarioItem(SampleScenario scenario) : ObservableObject
{
    [ObservableProperty]
    private bool _isChosen;

    public SampleScenario Scenario { get; } = scenario ?? throw new ArgumentNullException(nameof(scenario));

    public string Name => Scenario.Name;

    public string Title => Scenario.Title;

    /// <summary>What it plants, for the tooltip.</summary>
    public string Plants => Scenario.Plants;
}
