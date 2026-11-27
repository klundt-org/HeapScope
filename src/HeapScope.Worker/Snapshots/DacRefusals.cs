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

using HeapScope.Core;
using HeapScope.Loaders.ClrMd;

namespace HeapScope.Worker.Snapshots;

/// <summary>
/// The words for a full dump whose DAC cannot be used (owner's wording of 2026-11-13): the reason value reads are
/// refused, and the cause an older index cache's note names. Short, with the next step that works while the snapshot is open;
/// the full detail (paths, hashes, folders searched) is in the log line of the refusal.
/// </summary>
internal static class DacRefusals
{
    private const string Prefix = "Field values and strings are not read: ";

    /// <summary>The refusal for <paramref name="problem"/>, an <see cref="UntrustedDacException"/> or a <see cref="RuntimeComponentMissingException"/>.</summary>
    public static ValueReadsRefused For(Exception problem) => new(problem switch
    {
        UntrustedDacException { Sha256: { } sha256 } =>
            $"{Prefix}{Cause(problem)}. Trust it to read them (on the command line, add --trust-dac {sha256}).",
        RuntimeComponentMissingException { RuntimeVersion: not null } =>
            $"{Prefix}{Cause(problem)}. Install that runtime or put its DAC in the DAC folder (on the Settings page in the app, or --dac-folder on the command line), then close the snapshot and open the dump again.",
        _ => $"{Prefix}{Cause(problem)}. {problem.Message}",
    });

    /// <summary>Why the DAC cannot be used, as a clause: "the DAC (Data Access Component) ... is not trusted".</summary>
    public static string Cause(Exception problem) => problem switch
    {
        UntrustedDacException => "the DAC (Data Access Component) next to the dump is not trusted",
        RuntimeComponentMissingException { RuntimeVersion: { } version } =>
            $"the DAC (Data Access Component) of this dump's {Runtime(version)} runtime was not found",
        _ => "the dump's DAC (Data Access Component) cannot be used on this machine",
    };

    // ClrMD reads some core files' runtime version as 0.0; such a version is not named.
    private static string Runtime(string version) => version.StartsWith("0.0", StringComparison.Ordinal) ? ".NET" : ".NET " + version;
}
