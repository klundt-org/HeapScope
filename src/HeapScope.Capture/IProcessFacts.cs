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
/// What HeapScope reads about a process besides its diagnostics channel, per system (sprint 4.1): Windows asks the kernel
/// (<see cref="WindowsProcessFacts"/>), Linux reads <c>/proc</c> (<see cref="LinuxProcessFacts"/>). Each read returns null
/// when it is not allowed or the process has gone, so a process is still listed with what could be read.
/// </summary>
internal interface IProcessFacts
{
    /// <summary>The full path of the process's executable.</summary>
    string? ImagePath(int processId);

    /// <summary>The process's command line, its arguments separated by spaces.</summary>
    string? CommandLine(int processId);

    /// <summary>The loaded runtime's version and folder (the runtime ClrMD will need a DAC for).</summary>
    RuntimeFacts? Runtime(Process process);
}
