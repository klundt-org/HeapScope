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
/// What HeapScope reads about a process besides its diagnostics channel, per system: Windows asks the kernel
/// (<see cref="WindowsProcessFacts"/>), Linux reads <c>/proc</c> (<see cref="LinuxProcessFacts"/>), macOS asks libproc
/// (<see cref="MacProcessFacts"/>). Each read returns null
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

    /// <summary>The user the process acts as (its effective user id); null on Windows, where rights go by elevation.</summary>
    int? EffectiveUserId(int processId);

    /// <summary>The bytes of the process's committed, readable memory: what a full dump holds. Null when it cannot be walked.</summary>
    long? ReadableBytes(int processId);

    /// <summary>The process's private bytes, as this system counts them; null when not readable.</summary>
    long? PrivateBytes(Process process);

    /// <summary>Whether <see cref="ReadableBytes"/> can end early without a sign, so an estimate also asks the private bytes.</summary>
    bool WalkMayEndEarly => false;

    /// <summary>The bytes a full dump holds that <see cref="ReadableBytes"/> leaves out.</summary>
    long UncountedBytes => 0;

    /// <summary>How every read does on HeapScope's own process, for the log, where nobody has run them; null elsewhere.</summary>
    string? SelfCheck() => null;
}
