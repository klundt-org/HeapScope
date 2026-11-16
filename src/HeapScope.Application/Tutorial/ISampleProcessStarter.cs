// HeapScope
// Copyright (C) 2026 Ray Klundt
// https://github.com/rklundt/HeapScope
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

namespace HeapScope.Application.Tutorial;

/// <summary>Starts and finds sample processes (a seam: tests replace it).</summary>
public interface ISampleProcessStarter
{
    /// <summary>Starts <paramref name="fileName"/> with its output redirected.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">It could not be started (blocked, missing, not executable).</exception>
    ISampleProcess Start(string fileName, IReadOnlyList<string> arguments);

    /// <summary>When the process <paramref name="processId"/> started, in UTC; null when no such process runs.</summary>
    DateTime? StartTimeUtcOf(int processId);

    /// <summary>Stops the process <paramref name="processId"/> and waits a few seconds for it to exit.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">It could not be stopped (another user's, access denied).</exception>
    void Kill(int processId);
}
