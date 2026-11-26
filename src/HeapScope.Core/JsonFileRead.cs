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


namespace HeapScope.Core;

/// <summary>What reading a private JSON file found.</summary>
public enum JsonFileRead
{
    /// <summary>No file: nothing saved yet.</summary>
    Missing,

    /// <summary>Read.</summary>
    Read,

    /// <summary>The file is there but is not what was written (damaged): it can be set aside and started again.</summary>
    Damaged,

    /// <summary>The file is there but cannot be read now (held, or not this user's): it must not be written over.</summary>
    Unreadable,
}
