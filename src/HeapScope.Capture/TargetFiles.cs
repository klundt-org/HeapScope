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
/// Files whose paths a target process names (its runtime folder, from its mappings): another, possibly less
/// privileged user chose them, so HeapScope reads or copies one only when it is a regular file of a sane size (sprint 4.1
/// review H5). A pipe would block the read and a device would never end; both report a length of 0.
/// </summary>
public static class TargetFiles
{
    /// <summary>The largest runtime file read or copied: a runtime library or a DAC is a few MB.</summary>
    public const long MaxBytes = 64L * 1024 * 1024;

    /// <summary>True for a regular file (links followed) at least a header long and at most <see cref="MaxBytes"/>.</summary>
    public static bool IsReadable(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            FileSystemInfo info = new FileInfo(path);
            if (info.LinkTarget is not null)
            {
                info = info.ResolveLinkTarget(returnFinalTarget: true) ?? info;
            }

            return info is FileInfo { Exists: true, Length: >= 64 and <= MaxBytes };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
