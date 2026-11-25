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
using System.Reflection;
using System.Runtime.CompilerServices;
using HeapScope.Core.Allocations;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace HeapScope.Loaders.Trace;

/// <summary>
/// What the reader asks TraceEvent of an opened trace, each call in a method of its own that is never inlined: an
/// error thrown here, or in TraceEvent's own code, comes from the file, and one thrown anywhere else is HeapScope's and
/// never reads as a bad file (the rule of the gcdump loader, 3.7 review H5). Without it the JIT may inline TraceEvent's
/// accessors into the reader, and its errors would look like HeapScope's (as the gcdump loader's did, 4.2.1).
/// </summary>
internal static class TraceReads
{
    private static readonly Assembly s_traceEvent = typeof(TraceLog).Assembly;

    private static readonly Assembly s_fastSerialization = typeof(FastSerialization.IOStreamStreamReader).Assembly;

    /// <summary>
    /// Whether <paramref name="ex"/> was thrown by TraceEvent reading the file. The disk, access and memory are not the file's
    /// fault: the temporary file is HeapScope's own.
    /// </summary>
    public static bool IsFileError(Exception ex) =>
        CanBeTheFiles(ex)
        && Deciding(ex) is { } method
        && (method.Module.Assembly == s_traceEvent || method.Module.Assembly == s_fastSerialization || method.DeclaringType == typeof(TraceReads));

    /// <summary>The types of error a file can cause.</summary>
    internal static bool CanBeTheFiles(Exception ex) =>
        ex is not (OperationCanceledException or OutOfMemoryException or InsufficientExecutionStackException or UnauthorizedAccessException)
        && ex is not IOException or EndOfStreamException;

    /// <summary>Where an error was thrown, for the log: its type and the first method that is not the runtime's. No values.</summary>
    public static string Origin(Exception ex) =>
        $"{ex.GetType().Name} at {(Deciding(ex) is { } method ? $"{method.Module.Assembly.GetName().Name}, {method.DeclaringType?.Name}.{method.Name}" : "an unknown method")}";

    /// <summary>The first frame that is not the runtime's.</summary>
    private static MethodBase? Deciding(Exception ex)
    {
        foreach (var frame in new StackTrace(ex, fNeedFileInfo: false).GetFrames())
        {
            var method = frame.GetMethod();
            var assembly = method?.Module.Assembly.GetName().Name;
            if (assembly is not null && !assembly.StartsWith("System.", StringComparison.Ordinal))
            {
                return method;
            }
        }

        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Convert(string path, string etlx, TraceLogOptions options) => TraceLog.CreateFromEventPipeDataFile(path, etlx, options);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TraceLog Open(string etlx) => new(etlx);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TraceLogEventSource Source(TraceLog log) => log.Events.GetSource();

    /// <summary>How many events of a provider's kind the trace counts.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long Count(TraceLog log, int eventId)
    {
        long count = 0;
        foreach (var counts in log.Stats)
        {
            if (counts.ProviderGuid == ClrTraceEventParser.ProviderGuid && (int)counts.EventID == eventId)
            {
                count += counts.Count;
            }
        }

        return count;
    }

    /// <summary>When the recording started (UTC), how long it ran in milliseconds, and the events lost.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (DateTime StartUtc, double DurationMs, int Lost) Session(TraceLog log) =>
        (log.SessionStartTime.ToUniversalTime(), log.SessionDuration.TotalMilliseconds, log.EventsLost);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool IsRundown(TraceEvent e) => e.ProviderGuid == ClrRundownTraceEventParser.ProviderGuid;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TraceSample Sample(GCAllocationTickTraceData data) =>
        new(data.TypeName, data.CallStackIndex(), data.TimeStampRelativeMSec, data.AllocationAmount64, data.AllocationKind, data.TypeID);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ProcessId(GCAllocationTickTraceData data) => data.ProcessID;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string? ProcessName(GCAllocationTickTraceData data) => data.ProcessName;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static CallStackIndex Caller(TraceLog log, CallStackIndex stack) => log.CallStacks.Caller(stack);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static EventPipeEventSource OpenLive(Stream stream) => new(stream);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Process(EventPipeEventSource source) => source.Process();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Lost(EventPipeEventSource source) => source.EventsLost;

    /// <summary>What a live trace is read for (sprint 4.3): its ticks, its methods' code and names, and how far it got.</summary>
    public static void Subscribe(EventPipeEventSource source, Action<TraceEvent, LiveSample>? tick, Action<ulong, uint, string> method, Action<ulong>? unload, Action<LiveEvent>? any)
    {
        if (tick is not null)
        {
            source.Clr.GCAllocationTick += data => tick(data, Live(data));
        }

        void OnMethod(MethodLoadUnloadVerboseTraceData data)
        {
            var (start, size, name) = Method(data);
            method(start, size, name);
        }

        source.Clr.MethodLoadVerbose += OnMethod;
        var rundown = new ClrRundownTraceEventParser(source);
        rundown.MethodDCStartVerbose += OnMethod;
        rundown.MethodDCStopVerbose += OnMethod;
        if (unload is not null)
        {
            source.Clr.MethodUnloadVerbose += data => unload(Start(data));
        }

        if (any is not null)
        {
            source.AllEvents += data => any(Any(source, data));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static LiveSample Live(GCAllocationTickTraceData data) => new(
        data.TypeName,
        data.TimeStampRelativeMSec,
        data.TimeStamp.ToUniversalTime(),
        data.AllocationAmount64,
        data.AllocationKind switch
        {
            GCAllocationKind.Large => AllocationKind.Large,
            (GCAllocationKind)2 => AllocationKind.Pinned,
            _ => AllocationKind.Small,
        });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong Start(MethodLoadUnloadVerboseTraceData data) => data.MethodStartAddress;

    /// <summary>The process an event is of; TraceEvent makes the name anew each time.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (int ProcessId, string? Name) ProcessOf(TraceEvent data) => (Math.Max(0, data.ProcessID), string.IsNullOrEmpty(data.ProcessName) ? null : data.ProcessName);

    /// <summary>A method's code and its name, put together as TraceLog does, so that a live stack reads as the finished one.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ulong Start, uint Size, string Name) Method(MethodLoadUnloadVerboseTraceData data)
    {
        var signature = data.MethodSignature;
        var arguments = signature.IndexOf('(', StringComparison.Ordinal);
        return (data.MethodStartAddress, (uint)data.MethodSize, $"{data.MethodNamespace}.{data.MethodName}{(arguments >= 0 ? signature[arguments..] : string.Empty)}");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static LiveEvent Any(EventPipeEventSource source, TraceEvent data) =>
        new(source.SessionStartTime.ToUniversalTime(), data.TimeStampRelativeMSec, IsRundown(data), source.EventsLost);

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
