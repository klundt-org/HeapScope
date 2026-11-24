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
using System.Runtime.InteropServices;
using System.Text;

namespace HeapScope.Capture;

/// <summary>
/// A process's facts on macOS (sprint 4.2, untested: nobody has run it on a Mac), through libproc and sysctl, parsed by
/// <see cref="MacProcData"/>. The image path and the user id are readable for any process; the command line and the regions
/// only for the caller's own user's or as root, so those reads return null for another user's. Elsewhere the libraries are
/// missing and every read returns null.
/// </summary>
internal sealed partial class MacProcessFacts : IProcessFacts
{
    private const int PidShortInfo = 13; // PROC_PIDT_SHORTBSDINFO
    private const int PidRegionPathInfo = 8; // PROC_PIDREGIONPATHINFO
    private const int MostPathBytes = 4 * 1024; // PROC_PIDPATHINFO_MAXSIZE
    private const int ControlKernel = 1; // CTL_KERN
    private const int KernelArgumentsMax = 8; // KERN_ARGMAX
    private const int KernelProcessArguments = 49; // KERN_PROCARGS2
    private const int DefaultArgumentsMax = 1024 * 1024;

    private readonly Lazy<int> _argumentsMax = new(ReadArgumentsMax);

    public static MacProcessFacts Instance { get; } = new();

    public string? ImagePath(int processId) => Native(() =>
    {
        var buffer = new byte[MostPathBytes];
        var length = ProcPidPath(processId, buffer, (uint)buffer.Length);
        return length > 0 && length <= buffer.Length ? Encoding.UTF8.GetString(buffer, 0, length) : null;
    });

    public string? CommandLine(int processId) => FromProcArgs(processId, static bytes => MacProcData.CommandLine(bytes));

    /// <summary>What the process's environment says about its diagnostics socket; null when macOS does not give it.</summary>
    public TargetEnvironment? Environment(int processId) => FromProcArgs(processId, static bytes => MacProcData.Environment(bytes));

    public RuntimeFacts? Runtime(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return RuntimeLibrary.WithFileVersion(MacProcData.Runtime(Regions(process.Id)), MacProcData.RuntimeLibraryFile);
    }

    public int? EffectiveUserId(int processId)
    {
        var buffer = new byte[MacProcData.ShortInfoSize];
        return Info(processId, PidShortInfo, 0, buffer) ? MacProcData.EffectiveUserId(buffer) : null;
    }

    public long? ReadableBytes(int processId) => MacProcData.ReadableBytes(Regions(processId));

    /// <summary>The process's regions, one call each; none for another user's process.</summary>
    internal static IEnumerable<MacRegion> Regions(int processId)
    {
        var buffer = new byte[MacProcData.RegionSize];
        return MacProcData.Walk(address => Info(processId, PidRegionPathInfo, address, buffer) ? MacProcData.Region(buffer) : null);
    }

    /// <summary>Fills <paramref name="buffer"/>, whole: macOS returns the bytes it wrote, and another count is another layout.</summary>
    private static bool Info(int processId, int flavor, ulong argument, byte[] buffer)
    {
        try
        {
            return ProcPidInfo(processId, flavor, argument, buffer, buffer.Length) == buffer.Length;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private T? FromProcArgs<T>(int processId, Func<ReadOnlySpan<byte>, T?> parse)
        where T : class =>
        Native(() =>
        {
            var buffer = ArrayPool<byte>.Shared.Rent(_argumentsMax.Value);
            try
            {
                var length = (nuint)buffer.Length;
                return SysCtl([ControlKernel, KernelProcessArguments, processId], 3, buffer, ref length, IntPtr.Zero, 0) == 0 && length <= (nuint)buffer.Length
                    ? parse(buffer.AsSpan(0, (int)length))
                    : null;
            }
            finally
            {
                // It held the process's environment.
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        });

    private static int ReadArgumentsMax()
    {
        var buffer = new byte[sizeof(int)];
        var length = (nuint)buffer.Length;
        var most = SysCtl([ControlKernel, KernelArgumentsMax], 2, buffer, ref length, IntPtr.Zero, 0) == 0 && length == sizeof(int) ? BitConverter.ToInt32(buffer) : 0;
        return most is >= 4096 and <= 16 * 1024 * 1024 ? most : DefaultArgumentsMax;
    }

    private static T? Native<T>(Func<T?> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null; // Not macOS, or a macOS without the call.
        }
    }

    [LibraryImport("/usr/lib/libproc.dylib", EntryPoint = "proc_pidpath")]
    private static partial int ProcPidPath(int processId, [Out] byte[] buffer, uint size);

    [LibraryImport("/usr/lib/libproc.dylib", EntryPoint = "proc_pidinfo")]
    private static partial int ProcPidInfo(int processId, int flavor, ulong argument, [Out] byte[] buffer, int size);

    [LibraryImport("/usr/lib/libSystem.dylib", EntryPoint = "sysctl")]
    private static partial int SysCtl(int[] name, uint nameLength, [Out] byte[] old, ref nuint oldLength, IntPtr newValue, nuint newLength);
}
