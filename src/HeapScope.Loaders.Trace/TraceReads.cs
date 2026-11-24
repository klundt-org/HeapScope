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
using System.Runtime.CompilerServices;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace HeapScope.Loaders.Trace;

/// <summary>
/// What the reader asks TraceEvent while it builds its samples, each call in a method of its own that is never inlined: an
/// error thrown here, or in TraceEvent's own code, comes from the file, and one thrown anywhere else is HeapScope's and
/// never reads as a bad file (the rule of the gcdump loader, 3.7 review H5). Without it the JIT may inline TraceEvent's
/// accessors into the reader, and its errors would look like HeapScope's (as the gcdump loader's did, 4.2.1).
/// </summary>
internal static class TraceReads
{
    /// <summary>Whether <paramref name="ex"/> was thrown by TraceEvent reading the file: the first frame that is not the runtime's decides.</summary>
    public static bool IsFileError(Exception ex)
    {
        foreach (var frame in new StackTrace(ex, fNeedFileInfo: false).GetFrames())
        {
            var method = frame.GetMethod();
            var assembly = method?.Module.Assembly.GetName().Name;
            if (assembly is null || assembly.StartsWith("System.", StringComparison.Ordinal))
            {
                continue;
            }

            return assembly is "Microsoft.Diagnostics.Tracing.TraceEvent" or "Microsoft.Diagnostics.FastSerialization" || method!.DeclaringType == typeof(TraceReads);
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TraceLog Open(string etlx) => new(etlx);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TraceSample Sample(GCAllocationTickTraceData data) =>
        new(data.TypeName, data.CallStackIndex(), data.TimeStampRelativeMSec, data.AllocationAmount64, data.AllocationKind);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (int Id, string? Name) Process(GCAllocationTickTraceData data) => (data.ProcessID, data.ProcessName);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static CallStackIndex Caller(TraceLog log, CallStackIndex stack) => log.CallStacks.Caller(stack);

    /// <summary>The name of a stack's innermost method, and when it has none, of its module.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (string? Method, string? Module) Frame(TraceLog log, CallStackIndex stack)
    {
        var address = log.CallStacks.CodeAddressIndex(stack);
        var method = log.CodeAddresses.MethodIndex(address);
        var name = method == MethodIndex.Invalid ? null : log.CodeAddresses.Methods.FullMethodName(method);
        return string.IsNullOrEmpty(name) ? (null, log.CodeAddresses.ModuleFile(address)?.Name) : (name, null);
    }
}
