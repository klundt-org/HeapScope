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
using HeapScope.Application.Snapshots;
using HeapScope.Contracts;

namespace HeapScope.UI.ViewModels;

/// <summary>One inspection on the overview: its finding, cause, fix and first rows, or why it cannot run.</summary>
public sealed partial class InspectionTileViewModel(InspectionInfo info) : ObservableObject
{
    public InspectionInfo Info { get; } = info ?? throw new ArgumentNullException(nameof(info));

    public string Title => Info.Title;

    public string Cause => Info.Cause;

    public string Fix => Info.Fix;

    public bool IsAvailable => Info.Unavailable is null;

    /// <summary>A leak detector, shown in its own group above the waste inspections.</summary>
    public bool IsDetector => Info.Kind == InspectionKinds.Leak;

    /// <summary>The group's heading on the first tile of each group ("Leak detectors", "Memory waste"); null on the others.</summary>
    public string? GroupHeader { get; internal set; }

    public bool HasGroupHeader => GroupHeader is not null;

    /// <summary>"Waiting", "Running...", an error, or null when the finding is shown.</summary>
    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private string? _summary;

    [ObservableProperty]
    private string? _note;

    /// <summary>The first rows, one line each, for the tile.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _firstRows = [];

    /// <summary>Whether the inspection found anything (tiles with findings stand out).</summary>
    [ObservableProperty]
    private bool _hasFindings;

    public InspectionRun? Run { get; internal set; }

    /// <summary>The baseline <see cref="Run"/> used: a tile run with another one is run again.</summary>
    internal SnapshotHandle? RanWith { get; set; }
}
