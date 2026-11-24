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

/// <summary>The rules of a system HeapScope has no capture for: it explains nothing, since capture refuses there first.</summary>
internal sealed class NoCaptureRules : ICaptureRules
{
    public static NoCaptureRules Instance { get; } = new();

    public NotFoundExplanation WhyNotFound(int processId) => NotFoundExplanation.None;

    public WriteFailureExplanation WhyNotWritten(string message, string account, string directory) => new(null, null);

    public string? LibraryRefusal(string name, int processId) => null;
}
