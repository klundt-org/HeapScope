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
using HeapScope.Application.Presentation;
using HeapScope.Application.Snapshots;
using HeapScope.Contracts;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>One row of the comparison grid, filled when its page arrives (one notification per row, as in the types grid).</summary>
public sealed class DiffRowItem(int index) : ObservableObject, IIndexedRow
{
    public int Index { get; } = index;

    public string Name { get; private set; } = string.Empty;

    /// <summary>"new", "gone" or empty.</summary>
    public string Change { get; private set; } = string.Empty;

    public long BaselineObjects { get; private set; }

    public long CurrentObjects { get; private set; }

    public long ObjectsDelta { get; private set; }

    public long BaselineBytes { get; private set; }

    public long CurrentBytes { get; private set; }

    public long BytesDelta { get; private set; }

    /// <summary>Distinct types (method tables) summed into the row in the baseline; 0 when it has none.</summary>
    public int BaselineTypes { get; private set; }

    /// <summary>Distinct types summed into the row in the current snapshot ("copies" when it differs from the baseline).</summary>
    public int CurrentTypes { get; private set; }

    /// <summary>The raw type name that opens the row's instances; null for namespace and assembly rows.</summary>
    public string? TypeName { get; private set; }

    /// <summary>The module file name of <see cref="TypeName"/> (empty when none).</summary>
    public string? Module { get; private set; }

    /// <summary>Current objects not at their address in the baseline (sprint 3.3), when matched.</summary>
    public long? NewObjects { get; private set; }

    /// <summary>Baseline objects not at their address in the current snapshot, when matched.</summary>
    public long? DeadObjects { get; private set; }

    public long? BaselineRetained { get; private set; }

    public long? CurrentRetained { get; private set; }

    public long? RetainedDelta { get; private set; }

    public long? Gen0Delta { get; private set; }

    public long? Gen1Delta { get; private set; }

    public long? Gen2Delta { get; private set; }

    public long? LohDelta { get; private set; }

    public long? PohDelta { get; private set; }

    /// <summary>Sampled bytes allocated between the snapshots (sprint 4.4, an estimate); null without a sample.</summary>
    public long? AllocatedBytes { get; private set; }

    public long? AllocatedSamples { get; private set; }

    /// <summary>The trace's name of the row's type, when it has one name there.</summary>
    public string? AllocatedType { get; private set; }

    /// <summary>-1, 0 or 1: the sign of the bytes delta, for the growth color.</summary>
    public int Trend => Math.Sign(BytesDelta);

    public bool IsLoaded { get; private set; }

    /// <summary>A row list over a diff result set; fails at once when a column is missing.</summary>
    public static VirtualRowList<DiffRowItem> CreateList(ResultSetHandle? rows, ResultSetInfo info, Action<Exception>? onError = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        int[] layout =
        [
            ResultColumns.Require(info.Columns, TypeDiffColumns.Name),
            ResultColumns.Require(info.Columns, TypeDiffColumns.Change),
            ResultColumns.Require(info.Columns, TypeDiffColumns.BaselineObjects),
            ResultColumns.Require(info.Columns, TypeDiffColumns.CurrentObjects),
            ResultColumns.Require(info.Columns, TypeDiffColumns.ObjectsDelta),
            ResultColumns.Require(info.Columns, TypeDiffColumns.BaselineBytes),
            ResultColumns.Require(info.Columns, TypeDiffColumns.CurrentBytes),
            ResultColumns.Require(info.Columns, TypeDiffColumns.BytesDelta),
            ResultColumns.Find(info.Columns, TypeDiffColumns.BaselineTypes),
            ResultColumns.Find(info.Columns, TypeDiffColumns.CurrentTypes),
            ResultColumns.Find(info.Columns, TypeDiffColumns.TypeName),
            ResultColumns.Find(info.Columns, TypeDiffColumns.Module),
            ResultColumns.Find(info.Columns, TypeDiffColumns.NewObjects),
            ResultColumns.Find(info.Columns, TypeDiffColumns.DeadObjects),
            ResultColumns.Find(info.Columns, TypeDiffColumns.BaselineRetained),
            ResultColumns.Find(info.Columns, TypeDiffColumns.CurrentRetained),
            ResultColumns.Find(info.Columns, TypeDiffColumns.RetainedDelta),
            ResultColumns.Find(info.Columns, TypeDiffColumns.Gen0Delta),
            ResultColumns.Find(info.Columns, TypeDiffColumns.Gen1Delta),
            ResultColumns.Find(info.Columns, TypeDiffColumns.Gen2Delta),
            ResultColumns.Find(info.Columns, TypeDiffColumns.LohDelta),
            ResultColumns.Find(info.Columns, TypeDiffColumns.PohDelta),
            ResultColumns.Find(info.Columns, TypeDiffColumns.AllocatedBytes),
            ResultColumns.Find(info.Columns, TypeDiffColumns.AllocatedSamples),
            ResultColumns.Find(info.Columns, TypeDiffColumns.AllocatedType),
        ];
        return new VirtualRowList<DiffRowItem>(rows, info.TotalCount, i => new DiffRowItem(i), (row, cells) => row.Fill(cells, layout), onError, logger);
    }

    private void Fill(IReadOnlyList<string?> cells, int[] layout)
    {
        Name = Cells.Text(cells, layout[0]);
        Change = Cells.Text(cells, layout[1]);
        BaselineObjects = Cells.Long(cells, layout[2]);
        CurrentObjects = Cells.Long(cells, layout[3]);
        ObjectsDelta = Cells.Long(cells, layout[4]);
        BaselineBytes = Cells.Long(cells, layout[5]);
        CurrentBytes = Cells.Long(cells, layout[6]);
        BytesDelta = Cells.Long(cells, layout[7]);
        BaselineTypes = (int)Cells.Long(cells, layout[8]);
        CurrentTypes = (int)Cells.Long(cells, layout[9]);
        TypeName = layout[10] >= 0 ? Cells.Text(cells, layout[10]) : null;
        Module = layout[11] >= 0 ? Cells.Text(cells, layout[11]) : null;
        long? Optional(int at) => layout[at] >= 0 ? Cells.Long(cells, layout[at]) : null;
        (NewObjects, DeadObjects) = (Optional(12), Optional(13));
        (BaselineRetained, CurrentRetained, RetainedDelta) = (Optional(14), Optional(15), Optional(16));
        (Gen0Delta, Gen1Delta, Gen2Delta, LohDelta, PohDelta) = (Optional(17), Optional(18), Optional(19), Optional(20), Optional(21));
        // Empty without a sample, which is not the same as zero bytes.
        long? Sampled(int at) => layout[at] >= 0 && cells[layout[at]] is not null ? Cells.Long(cells, layout[at]) : null;
        (AllocatedBytes, AllocatedSamples) = (Sampled(22), Sampled(23));
        AllocatedType = layout[24] >= 0 ? cells[layout[24]] : null;
        IsLoaded = true;
        OnPropertyChanged(string.Empty);
    }
}
