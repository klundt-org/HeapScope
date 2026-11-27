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

using System.Diagnostics;

namespace HeapScope.Capture;

/// <summary>
/// The facts on a system HeapScope has no reader for: none. Capture refuses there
/// (<see cref="CapturePlatform.EnsureSupported()"/>) before any is asked for.
/// </summary>
internal sealed class NoProcessFacts : IProcessFacts
{
    public static NoProcessFacts Instance { get; } = new();

    public string? ImagePath(int processId) => null;

    public string? CommandLine(int processId) => null;

    public RuntimeFacts? Runtime(Process process) => null;

    public int? EffectiveUserId(int processId) => null;

    public long? ReadableBytes(int processId) => null;

    public long? PrivateBytes(Process process) => null;
}
