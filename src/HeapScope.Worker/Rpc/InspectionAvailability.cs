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

using HeapScope.Analysis.Inspections;
using HeapScope.Contracts;
using HeapScope.Core;

namespace HeapScope.Worker.Rpc;

/// <summary>
/// Whether an inspection can run on a snapshot, and why not (sprint 1.6, ADR-020): a missing capability is explained as
/// for any feature; a missing loader fact on a full dump means its index cache predates the fact.
/// </summary>
internal static class InspectionAvailability
{
    /// <summary>
    /// Why <paramref name="inspection"/> cannot run on <paramref name="snapshot"/>, or null when it can. A missing capability
    /// is worded by the feature rules, as the descriptor's list is (sprint 3.12: an inspection that reads fields of a dump
    /// whose DAC was declined used to be listed as runnable).
    /// </summary>
    public static string? Explain(IInspection inspection, ISnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(snapshot);
        var info = snapshot.Info;
        var missing = snapshot.Capabilities.Missing(inspection.Requires);
        if (missing != SnapshotCapabilities.None)
        {
            return FeatureRules.ExplainMissing(missing, snapshot);
        }

        var facts = inspection.RequiresFacts & ~info.Facts;
        if (facts == SnapshotFacts.None)
        {
            return null;
        }

        // A full dump whose statics could not be read is not an older cache: say what it lacks (the review of 4.7, H5).
        if (facts.HasFlag(SnapshotFacts.StaticsChecked) && Snapshots.SnapshotKinds.Describe(info.Kind).Unchecked(info) is { } roots && roots != Snapshots.UncheckedRoots.Handles)
        {
            return $"{inspection.Title} finds collections by the static fields that hold them, and {Snapshots.UncheckedRoots.Statics.Reason} (see its notes).";
        }

        return Explain(inspection.Title, info);
    }

    /// <summary>Why a feature named <paramref name="title"/> lacks a loader fact on this snapshot.</summary>
    public static string Explain(string title, SnapshotInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Snapshots.SnapshotKinds.Describe(info.Kind).ExplainMissingFact(title);
    }
}
