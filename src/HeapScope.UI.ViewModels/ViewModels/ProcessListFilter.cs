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
/// The rule both process lists follow (sprint 4.1.1, owner 2026-11-20): a process HeapScope cannot capture as it runs now
/// (<see cref="ProcessRow.NeedsAdministrator"/>: elevated or another user's, while HeapScope is not elevated or root) is
/// hidden unless <see cref="ShowHidden"/>. Run elevated, HeapScope can capture them all, so no row needs more and nothing
/// is hidden. The rows keep their Elevation mark, so a shown one says what it needs.
/// </summary>
public sealed class ProcessListFilter
{
    private List<ProcessRow> _all = [];

    /// <summary>Whether the hidden processes are listed too (the page's check box; off at each start).</summary>
    public bool ShowHidden { get; set; }

    /// <summary>Every process listed, hidden or not.</summary>
    public IReadOnlyList<ProcessRow> All => _all;

    /// <summary>The rows the list shows.</summary>
    public IReadOnlyList<ProcessRow> Visible => ShowHidden ? _all : [.. _all.Where(static r => !r.NeedsAdministrator)];

    /// <summary>The rows hidden now, by pid (for the note and the log).</summary>
    public IReadOnlyList<int> Hidden => ShowHidden ? [] : [.. _all.Where(static r => r.NeedsAdministrator).Select(static r => r.ProcessId)];

    /// <summary>Whether any process needs more rights than HeapScope has: only then is there a check box to offer.</summary>
    public bool AnyNeedMoreRights => _all.Exists(static r => r.NeedsAdministrator);

    /// <summary>Replaces the rows with a new listing.</summary>
    public void Replace(IEnumerable<ProcessRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _all = [.. rows];
    }

    /// <summary>Replaces one row (the sample's mark changed), keeping its place.</summary>
    public void Update(ProcessRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var at = _all.FindIndex(r => r.ProcessId == row.ProcessId);
        if (at >= 0)
        {
            _all[at] = row;
        }
    }
}
