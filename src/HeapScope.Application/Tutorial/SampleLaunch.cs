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

namespace HeapScope.Application.Tutorial;

/// <summary>
/// How the sample starts: its executable, or its assembly through the dotnet host where no executable ships (the CLI's dotnet
/// tool, as the worker, ADR-039). <see cref="Prefix"/> holds the assembly's path then.
/// </summary>
public sealed record SampleLaunch(string FileName, IReadOnlyList<string> Prefix, string SampleFile)
{
    /// <summary>The sample beside this installation (<c>sample/</c>), or null when it is not there.</summary>
    public static SampleLaunch? Resolve(string baseDirectory, string? dotnetHost, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        var folder = Path.Combine(baseDirectory, "sample");
        var executable = Path.Combine(folder, OperatingSystem.IsWindows() ? "heapscope-sample.exe" : "heapscope-sample");
        if (exists(executable))
        {
            return new SampleLaunch(executable, [], executable);
        }

        var assembly = Path.Combine(folder, "heapscope-sample.dll");
        return dotnetHost is not null && exists(assembly) ? new SampleLaunch(dotnetHost, [assembly], assembly) : null;
    }
}
