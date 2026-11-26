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

using HeapScope.Core;

namespace HeapScope.Storage;

/// <summary>
/// The worker's memory budget for snapshot data (ARCHITECTURE.md 6.5, ADR-015): a fraction of total physical memory,
/// capped by the memory available now. <see cref="Forced"/> overrides the choice (configuration).
/// </summary>
public sealed partial record MemoryBudget(long TotalPhysicalBytes, long AvailableBytes, double Fraction = 0.5, MemoryMode? Forced = null)
{
    public long BudgetBytes => Math.Max(0, Math.Min((long)(TotalPhysicalBytes * Fraction), AvailableBytes));

    /// <summary>
    /// The budget from live operating system figures (<see cref="SystemMemory"/>: on Windows, Linux and macOS since sprint 4.7,
    /// peer review 4.5 H7), read now rather than at the last garbage collection. A GC heap hard limit, when set, caps both values; it is never used as the total by
    /// itself (gc-specialist review of 0.5).
    /// </summary>
    public static MemoryBudget Current(double fraction = 0.5, MemoryMode? forced = null)
    {
        var info = GC.GetGCMemoryInfo();
        var system = SystemMemory.Current();
        var (total, available) = (system.TotalBytes, system.AvailableBytes);

        // TotalAvailableMemoryBytes is the hard limit when one is configured, otherwise physical memory.
        var gcLimit = info.TotalAvailableMemoryBytes;
        if (gcLimit < total)
        {
            available = Math.Min(available, gcLimit - GC.GetTotalMemory(forceFullCollection: false));
            total = gcLimit;
        }

        return new MemoryBudget(total, Math.Max(0, available), fraction, forced);
    }

}

/// <summary>Chooses in-memory or mapped mode for a graph of a given size (ADR-015).</summary>
public static class MemoryModeSelector
{
    public static MemoryMode Select(long requiredBytes, MemoryBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        if (budget.Forced is { } forced)
        {
            return forced;
        }

        return requiredBytes <= budget.BudgetBytes ? MemoryMode.InMemory : MemoryMode.Mapped;
    }
}
