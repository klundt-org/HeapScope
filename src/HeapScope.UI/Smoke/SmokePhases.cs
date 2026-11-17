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
using HeapScope.UI.Services;

namespace HeapScope.UI.Smoke;

/// <summary>
/// The measured stretches of a smoke run (sprint 3.12, R3): each begins with the watchdog cleared and ends with its maxima
/// and its slow operations kept, so the checked maxima, the cold costs and the count of slow first shows (task 5's ceiling)
/// come from one place, and every phase's log line says them the same way.
/// </summary>
internal sealed class SmokePhases
{
    private readonly List<SmokePhase> _phases = [];
    private readonly Func<(TimeSpan Lag, TimeSpan Operation, int Slow)> _read;
    private readonly Action _reset;

    public SmokePhases(DispatcherWatchdog watchdog)
        : this(() => (watchdog.MaxLag, watchdog.MaxOperationBound, watchdog.SlowOperations), watchdog.Reset)
    {
    }

    /// <summary>Phases over any watchdog's readings (tests).</summary>
    internal SmokePhases(Func<(TimeSpan Lag, TimeSpan Operation, int Slow)> read, Action reset)
    {
        _read = read;
        _reset = reset;
    }

    /// <summary>The current phase's measures so far, for a line logged inside a phase that its caller ends.</summary>
    public SmokePhase SoFar(string name)
    {
        var (lag, operation, slow) = _read();
        return new SmokePhase(name, false, lag, operation, slow);
    }

    /// <summary>The names of the checks that did not pass, or null when all did (a red run says which view failed).</summary>
    public static string? Failed(params (string Check, bool Passed)[] checks) =>
        checks.Where(c => !c.Passed).Select(c => c.Check).ToArray() is { Length: > 0 } failed ? string.Join(", ", failed) : null;

    /// <summary>The phases ended so far, in order.</summary>
    public IReadOnlyList<SmokePhase> All => _phases;

    /// <summary>The checked (warm) phases' longest lag and longest operation; cold first shows are logged, not checked.</summary>
    public (TimeSpan Lag, TimeSpan Operation) Checked => (
        _phases.Where(p => !p.Cold).Select(p => p.MaxLag).DefaultIfEmpty().Max(),
        _phases.Where(p => !p.Cold).Select(p => p.MaxOperation).DefaultIfEmpty().Max());

    /// <summary>UI operations over the 100 ms budget in every phase, cold first shows included.</summary>
    public int SlowOperations => _phases.Sum(p => p.SlowOperations);

    /// <summary>Starts a phase: the watchdog's maxima and slow operations count from now.</summary>
    public void Begin() => _reset();

    /// <summary>Ends the phase begun last and keeps its measures.</summary>
    public SmokePhase End(string name, bool cold)
    {
        var phase = SoFar(name) with { Cold = cold };
        _phases.Add(phase);
        return phase;
    }
}

/// <summary>One measured stretch of a smoke run (sprint 3.12, R3).</summary>
/// <param name="Cold">A first show, logged and counted but not checked against the budget.</param>
/// <param name="SlowOperations">UI operations over the 100 ms budget in it.</param>
internal sealed record SmokePhase(string Name, bool Cold, TimeSpan MaxLag, TimeSpan MaxOperation, int SlowOperations)
{
    /// <summary>The phase's measures as every smoke log line has said them since sprint 1.2 (the A/B script reads them).</summary>
    public string Measured => string.Create(
        CultureInfo.InvariantCulture,
        $"maximum dispatcher lag {MaxLag.TotalMilliseconds:N0} ms, longest dispatcher operation at most {MaxOperation.TotalMilliseconds:N0} ms");
}
