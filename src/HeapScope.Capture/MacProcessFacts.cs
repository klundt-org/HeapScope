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

using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace HeapScope.Capture;

/// <summary>
/// A process's facts on macOS (sprint 4.2, untested: nobody has run it on a Mac), through libproc and sysctl, parsed by
/// <see cref="MacProcData"/>. The image path and the user id are readable for any process; the command line, the footprint
/// and the regions only for the caller's own user's or as root, so those reads return null for another user's. Elsewhere
/// the libraries are missing and every read returns null. Each read keeps its outcome, for the log.
/// </summary>
internal sealed partial class MacProcessFacts : IProcessFacts
{
    /// <summary>The shared cache's size when macOS does not give it: several GB, so an estimate errs high.</summary>
    public const long DefaultSharedCacheBytes = 5L * 1024 * 1024 * 1024;

    private const int PidShortInfo = 13; // PROC_PIDT_SHORTBSDINFO
    private const int PidRegionPathInfo = 8; // PROC_PIDREGIONPATHINFO
    private const int MostPathBytes = 4 * 1024; // PROC_PIDPATHINFO_MAXSIZE
    private const int ControlKernel = 1; // CTL_KERN
    private const int KernelArgumentsMax = 8; // KERN_ARGMAX
    private const int KernelProcessArguments = 49; // KERN_PROCARGS2
    private const int DefaultArgumentsMax = 1024 * 1024;
    private const string NotThere = "the call is not there";

    private readonly Lazy<int> _argumentsMax = new(ReadArgumentsMax);
    private readonly Lazy<long?> _sharedCache = new(ReadSharedCache);

    public static MacProcessFacts Instance { get; } = new();

    public bool WalkMayEndEarly => true;

    /// <summary>
    /// The dyld shared cache: the region walk reports it as one empty submap and leaves it out, but createdump walks into it
    /// and a full dump holds it (4.2 review). It is the same for every process of this architecture.
    /// </summary>
    public long UncountedBytes => _sharedCache.Value ?? DefaultSharedCacheBytes;

    public string? ImagePath(int processId) => ImagePath(processId, out _);

    public string? CommandLine(int processId) => FromProcArgs(processId, static bytes => MacProcData.CommandLine(bytes), out _);

    /// <summary>What the process's environment says about its diagnostics socket; null when macOS does not give it.</summary>
    public TargetEnvironment? Environment(int processId, out string outcome) =>
        FromProcArgs(processId, static bytes => MacProcData.Environment(bytes), out outcome);

    public RuntimeFacts? Runtime(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return RuntimeLibrary.WithFileVersion(Walk(process.Id, stopAtRuntime: true).Runtime, MacProcData.RuntimeLibraryFile);
    }

    public int? EffectiveUserId(int processId) => EffectiveUserId(processId, out _);

    public int? EffectiveUserId(int processId, out string outcome)
    {
        var buffer = new byte[MacProcData.ShortInfoSize];
        outcome = Info(processId, PidShortInfo, 0, buffer);
        return outcome.Length == 0 ? MacProcData.EffectiveUserId(buffer) : null;
    }

    public long? ReadableBytes(int processId) => Walk(processId, stopAtRuntime: false).ReadableBytes;

    /// <summary>The physical footprint (.NET 10's <c>PrivateMemorySize64</c> is always 0 on macOS, 4.2 review).</summary>
    public long? PrivateBytes(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return PrivateBytes(process.Id, out _);
    }

    /// <summary>The process's regions, one call each; none for another user's process.</summary>
    public static MacWalk Walk(int processId, bool stopAtRuntime)
    {
        var buffer = new byte[MacProcData.RegionSize];
        return MacProcData.Walk(
            address =>
            {
                var outcome = Info(processId, PidRegionPathInfo, address, buffer);
                return (outcome.Length == 0 ? MacProcData.Region(buffer) : null, outcome);
            },
            stopAtRuntime);
    }

    /// <summary>
    /// Every read on HeapScope's own process, for the log: nobody has run these on a Mac, so a first report shows here which
    /// call works, what it returned against what was expected, and its errno.
    /// </summary>
    public string SelfCheck()
    {
        var pid = System.Environment.ProcessId;
        var line = new StringBuilder();
        line.Append(CultureInfo.InvariantCulture, $"macOS reads of HeapScope's own process {pid} ({RuntimeInformation.ProcessArchitecture} on {RuntimeInformation.OSArchitecture}): ");
        line.Append(CultureInfo.InvariantCulture, $"image path {(ImagePath(pid, out var path) is null ? path : "read")}; ");
        line.Append(CultureInfo.InvariantCulture, $"user id {(EffectiveUserId(pid, out var user) is { } id ? (uint)id : user)}; ");
        line.Append(CultureInfo.InvariantCulture, $"command line {(FromProcArgs(pid, static bytes => MacProcData.CommandLine(bytes), out var arguments) is null ? arguments : "read")}; ");
        line.Append(CultureInfo.InvariantCulture, $"environment {(Environment(pid, out var environment) is null ? environment : "read")}; ");
        line.Append(CultureInfo.InvariantCulture, $"footprint {(PrivateBytes(pid, out var footprint) is { } bytes ? bytes : footprint)}; ");
        line.Append(CultureInfo.InvariantCulture, $"shared cache {(_sharedCache.Value is { } cache ? cache : $"not given, {DefaultSharedCacheBytes} assumed")}; ");
        line.Append(CultureInfo.InvariantCulture, $"regions {Walk(pid, stopAtRuntime: false)}");
        return line.ToString();
    }

    private static string? ImagePath(int processId, out string outcome)
    {
        try
        {
            var buffer = new byte[MostPathBytes];
            var length = ProcPidPath(processId, buffer, (uint)buffer.Length);
            outcome = length > 0 && length <= buffer.Length ? string.Empty : Failed(length, buffer.Length);
            return outcome.Length == 0 ? Encoding.UTF8.GetString(buffer, 0, length) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            outcome = NotThere;
            return null;
        }
    }

    private static long? PrivateBytes(int processId, out string outcome)
    {
        try
        {
            // The call fills the size of the version asked for (0) and returns 0; it does not return a size to check.
            var buffer = new byte[MacProcData.UsageSize];
            var result = ProcPidUsage(processId, 0, buffer);
            outcome = result == 0 ? "no plausible footprint" : Failed(result, 0);
            return result == 0 ? MacProcData.PhysicalFootprint(buffer) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            outcome = NotThere;
            return null;
        }
    }

    /// <summary>Fills <paramref name="buffer"/>, whole: empty when it did, or why not (macOS returns the bytes it wrote, and another count is another layout).</summary>
    private static string Info(int processId, int flavor, ulong argument, byte[] buffer)
    {
        try
        {
            var written = ProcPidInfo(processId, flavor, argument, buffer, buffer.Length);
            return written == buffer.Length ? string.Empty : Failed(written, buffer.Length);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return NotThere;
        }
    }

    private static string Failed(int returned, int expected) => returned <= 0
        ? string.Create(CultureInfo.InvariantCulture, $"errno {Marshal.GetLastPInvokeError()}")
        : string.Create(CultureInfo.InvariantCulture, $"{returned} bytes returned, {expected} expected");

    private T? FromProcArgs<T>(int processId, Func<ReadOnlySpan<byte>, T?> parse, out string outcome)
        where T : class
    {
        byte[]? buffer = null;
        try
        {
            // The length macOS is told is its own limit, not the pool's larger array: it refuses more.
            var size = _argumentsMax.Value;
            buffer = ArrayPool<byte>.Shared.Rent(size);
            var length = (nuint)size;
            var result = SysCtl([ControlKernel, KernelProcessArguments, processId], 3, buffer, ref length, IntPtr.Zero, 0);
            if (result != 0 || length > (nuint)size)
            {
                outcome = result != 0 ? Failed(0, size) : Failed((int)Math.Min(length, int.MaxValue), size);
                return null;
            }

            var parsed = parse(buffer.AsSpan(0, (int)length));
            outcome = parsed is null ? string.Create(CultureInfo.InvariantCulture, $"not in the {length} bytes returned") : string.Empty;
            return parsed;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            outcome = NotThere;
            return null;
        }
        finally
        {
            if (buffer is not null)
            {
                // It held the process's environment.
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }
    }

    private static int ReadArgumentsMax()
    {
        var buffer = new byte[sizeof(int)];
        var length = (nuint)buffer.Length;
        var most = SysCtl([ControlKernel, KernelArgumentsMax], 2, buffer, ref length, IntPtr.Zero, 0) == 0 && length == sizeof(int) ? BitConverter.ToInt32(buffer) : 0;
        return most is >= 4096 and <= 16 * 1024 * 1024 ? most : DefaultArgumentsMax;
    }

    private static long? ReadSharedCache()
    {
        try
        {
            return SharedCacheRange(out var length) != IntPtr.Zero && (ulong)length is > 0 and <= 64UL * 1024 * 1024 * 1024 ? (long)length : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [LibraryImport("/usr/lib/libproc.dylib", EntryPoint = "proc_pidpath", SetLastError = true)]
    private static partial int ProcPidPath(int processId, [Out] byte[] buffer, uint size);

    [LibraryImport("/usr/lib/libproc.dylib", EntryPoint = "proc_pidinfo", SetLastError = true)]
    private static partial int ProcPidInfo(int processId, int flavor, ulong argument, [Out] byte[] buffer, int size);

    [LibraryImport("/usr/lib/libproc.dylib", EntryPoint = "proc_pid_rusage", SetLastError = true)]
    private static partial int ProcPidUsage(int processId, int flavor, [Out] byte[] buffer);

    [LibraryImport("/usr/lib/libSystem.dylib", EntryPoint = "sysctl", SetLastError = true)]
    private static partial int SysCtl(int[] name, uint nameLength, [Out] byte[] old, ref nuint oldLength, IntPtr newValue, nuint newLength);

    [LibraryImport("/usr/lib/libSystem.dylib", EntryPoint = "_dyld_get_shared_cache_range")]
    private static partial IntPtr SharedCacheRange(out nuint length);
}
