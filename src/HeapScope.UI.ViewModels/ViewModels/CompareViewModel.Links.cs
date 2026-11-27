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

using HeapScope.Application.Snapshots;
using HeapScope.Contracts;
using Microsoft.Extensions.Logging;

namespace HeapScope.UI.ViewModels;

/// <summary>The comparison's links: a row's objects, its changes and the selected rows as sets, and a row's subjects in either snapshot.</summary>
public sealed partial class CompareViewModel
{
    /// <summary>
    /// A row's new, dead or survived objects as a set (decision 2): new and survived in the current snapshot against the
    /// baseline, dead in the baseline against the current snapshot.
    /// </summary>
    private void OpenChange(DiffRowItem? row, ObjectChange change, INavigator? navigator)
    {
        if (row is not { IsLoaded: true } || Baseline is not { } baseline || Current is not { } current)
        {
            return;
        }

        if (row.TypeName is null)
        {
            Status = "Group by type to open a row's new, dead or survived objects.";
            return;
        }

        if (!IdentityShown)
        {
            Status = IdentityNote ?? "Tick New and dead objects first: objects are matched by address only for two snapshots of one process.";
            return;
        }

        var (side, other) = change == ObjectChange.Dead ? (baseline, current) : (current, baseline);
        var spec = ObjectSetSpec.ComparedOf(ObjectSetSpec.OfType(row.TypeName, row.Module), change);
        var title = string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{change} by address: {row.Name}");
        Logger.LogInformation("Compare: opened the {Change} objects of a row as a set in the {Side} snapshot", change, change == ObjectChange.Dead ? "baseline" : "current");
        navigator?.Navigate(NavigationTarget.ObjectSet(side.Handle, new ObjectSetQuery(spec, other.Handle), title));
    }

    private void OpenAsSet(System.Collections.IList? rows, INavigator? navigator)
    {
        if (rows is null || Current is not { } current)
        {
            return;
        }

        var selected = rows.OfType<DiffRowItem>().ToList();
        if (selected.Any(r => !r.IsLoaded))
        {
            Status = "Some of the selected rows are not loaded yet. Scroll through them, or filter the grid, and open the set again.";
            return;
        }

        var keyed = selected.Where(r => r.TypeName is not null).ToList();
        if (keyed.Count == 0)
        {
            Status = GroupBy == TypeGrouping.Type ? "Select the rows of types to open them as a set." : "Group by type to open rows as a set.";
            return;
        }

        if (keyed.Count > ObjectSetLimits.MaxTypes)
        {
            Status = string.Create(System.Globalization.CultureInfo.CurrentCulture, $"A set takes at most {ObjectSetLimits.MaxTypes:N0} types; {keyed.Count:N0} are selected.");
            return;
        }

        // One snapshot's objects: the current one's, or the baseline's when none of the types is left (the 3.1 review: types
        // only in the baseline are left out of a current set, and the status says so).
        var inBaselineOnly = Baseline is { } baseline && keyed.All(r => r.CurrentObjects == 0);
        var side = inBaselineOnly ? Baseline! : current;
        var used = inBaselineOnly ? keyed : [.. keyed.Where(r => r.CurrentObjects > 0)];
        var title = used.Count == 1 ? used[0].Name : string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{used.Count:N0} compared types");
        Logger.LogInformation("Compare: opened {Count} types as a set in the {Side} snapshot ({Left} only in the baseline left out)", used.Count, inBaselineOnly ? "baseline" : "current", keyed.Count - used.Count);
        navigator?.Navigate(NavigationTarget.ObjectSet(side.Handle, new ObjectSetQuery(ObjectSetSpec.OfTypes(used.Select(r => new TypeKeyDto(r.TypeName!, r.Module)))), title));
        if (used.Count < keyed.Count)
        {
            Status = string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{keyed.Count - used.Count:N0} of the selected types are only in the baseline, so the current snapshot's set leaves them out.");
        }
    }

    // Retention is the current snapshot's: a type it no longer has retains nothing there.
    private void Link(DiffRowItem? row, INavigator? navigator, Func<SnapshotHandle, DiffRowItem, NavigationTarget> target)
    {
        if (row is not { IsLoaded: true } || Current is not { } current)
        {
            return;
        }

        if (row.TypeName is null || row.CurrentObjects == 0)
        {
            Status = row.TypeName is null ? "Group by type to follow a row's objects." : "The current snapshot has no objects of this type.";
            return;
        }

        navigator?.Navigate(target(current.Handle, row));
    }

    /// <summary>
    /// Opens a row's type on the Snapshot tab: its objects in the current snapshot, or in the baseline for a type the
    /// current snapshot no longer has (ADR-018; sprint 1.5 review).
    /// </summary>
    private void OpenRow(DiffRowItem? row, INavigator? navigator)
    {
        if (row is not { IsLoaded: true } || Current is not { } current)
        {
            return;
        }

        if (row.TypeName is null)
        {
            // In Type grouping a row has no key only when one side merged several of its types under one name: a full
            // dump's nested types compared with a gcdump, which records them without their declaring type (sprint 1.8).
            Status = GroupBy == TypeGrouping.Type
                ? "This row joins several types of the full dump under the one name the gcdump records, so it cannot open one type's objects. Find the type in the full dump's own summary."
                : "Group by type to open a row's objects.";
            return;
        }

        var side = row.CurrentObjects == 0 && Baseline is { } baseline ? baseline : current;
        navigator?.Navigate(NavigationTarget.Instances(side.Handle, row.TypeName, row.Module));
    }
}
