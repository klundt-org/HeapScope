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

namespace HeapScope.Capture;

/// <summary>
/// Why this system refuses or fails a capture, and what to do (sprint 4.2; per system, ADR-025): one implementation per
/// system, chosen by <see cref="CapturePlatform.Rules()"/>. Each answer carries the evidence it rests on, for the log.
/// </summary>
internal interface ICaptureRules
{
    /// <summary>Why a process asked for by pid has no diagnostics socket where HeapScope looks, where the system can tell.</summary>
    NotFoundExplanation WhyNotFound(int processId);

    /// <summary>
    /// Why the runtime's createdump could not write a dump, from the <paramref name="message"/> it reported. It writes under
    /// the target's own <paramref name="account"/>, not HeapScope's.
    /// </summary>
    WriteFailureExplanation WhyNotWritten(string message, string account, string directory);

    /// <summary>
    /// Why a full dump of another user's process cannot go into HeapScope's own folders while HeapScope has the rights to
    /// reach it (4.1 review H1). Null when it can.
    /// </summary>
    string? LibraryRefusal(string name, int processId);
}

/// <summary>Why a dump was not written: <see cref="Reason"/> for the user (null leaves the generic text), <see cref="Evidence"/> for the log.</summary>
internal sealed record WriteFailureExplanation(string? Reason, string? Evidence);

internal static class CaptureRulesExtensions
{
    /// <summary>
    /// The failure for a process that did not answer on its diagnostics port (Live and recording, 4.1 review M2 and H6): the
    /// reason a capture of a pid that is not found gives, with its evidence for the log; <paramref name="otherwise"/> when none applies.
    /// </summary>
    public static TargetUnavailableException NotAnswering(this ICaptureRules rules, int processId, string otherwise, Exception inner)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var explanation = rules.WhyNotFound(processId);
        return new TargetUnavailableException(explanation.Reason ?? otherwise, inner) { Evidence = explanation.Evidence };
    }
}
