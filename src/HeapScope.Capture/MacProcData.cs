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

using System.Buffers.Binary;
using System.Text;

namespace HeapScope.Capture;

/// <summary>
/// Parsers of what macOS returns about a process (sprint 4.2): the structs of XNU's <c>bsd/sys/proc_info.h</c> (from
/// <c>proc_pidinfo</c>) and the buffer of <c>sysctl KERN_PROCARGS2</c>. They take the bytes, not the process, so they run and
/// are tested on every system; <see cref="MacProcessFacts"/> makes the calls. Nobody has run them on a Mac, so each checks
/// the size the system returned and gives nothing when it is not the one expected.
/// </summary>
internal static class MacProcData
{
    /// <summary>The size of <c>proc_bsdshortinfo</c>.</summary>
    public const int ShortInfoSize = 64;

    /// <summary>The size of <c>proc_regionwithpathinfo</c>.</summary>
    public const int RegionSize = 1272;

    /// <summary>The library that holds the runtime, with the separator before it.</summary>
    public const string RuntimeLibraryFile = "/libcoreclr.dylib";

    private const int UserIdOffset = 36;
    private const int ProtectionOffset = 0;
    private const int ShareModeOffset = 60;
    private const int AddressOffset = 80;
    private const int SizeOffset = 88;
    private const int PathOffset = 248;
    private const uint ProtectionRead = 1;
    private const uint ShareModeEmpty = 3;

    /// <summary>More regions than any process has: a walk that does not end stops here.</summary>
    private const int MostRegions = 1_000_000;

    /// <summary>The effective user id in a <c>proc_bsdshortinfo</c> (<c>pbsi_uid</c>); null when it is another size.</summary>
    public static int? EffectiveUserId(ReadOnlySpan<byte> shortInfo) =>
        shortInfo.Length == ShortInfoSize ? BinaryPrimitives.ReadInt32LittleEndian(shortInfo[UserIdOffset..]) : null;

    /// <summary>The region in a <c>proc_regionwithpathinfo</c>; null when it is another size.</summary>
    public static MacRegion? Region(ReadOnlySpan<byte> region)
    {
        if (region.Length != RegionSize)
        {
            return null;
        }

        var path = region[PathOffset..];
        var end = path.IndexOf((byte)0);
        path = end < 0 ? path : path[..end];
        return new MacRegion(
            BinaryPrimitives.ReadUInt64LittleEndian(region[AddressOffset..]),
            BinaryPrimitives.ReadUInt64LittleEndian(region[SizeOffset..]),
            (BinaryPrimitives.ReadUInt32LittleEndian(region[ProtectionOffset..]) & ProtectionRead) != 0
                && BinaryPrimitives.ReadUInt32LittleEndian(region[ShareModeOffset..]) != ShareModeEmpty,
            path.IsEmpty ? null : Encoding.UTF8.GetString(path));
    }

    /// <summary>
    /// The regions of a process in address order: <paramref name="read"/> gives the first at or after an address, or null
    /// when there is none or it is not allowed. The walk ends there, or when a region does not move it on.
    /// </summary>
    public static IEnumerable<MacRegion> Walk(Func<ulong, MacRegion?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        ulong address = 0;
        for (var i = 0; i < MostRegions; i++)
        {
            if (read(address) is not { } region)
            {
                yield break;
            }

            yield return region;
            var next = unchecked(region.Address + region.Size);
            if (next <= address)
            {
                yield break;
            }

            address = next;
        }
    }

    /// <summary>The bytes of the readable regions, what a full dump holds; null when there is no region to count.</summary>
    public static long? ReadableBytes(IEnumerable<MacRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        long? total = null;
        foreach (var region in regions)
        {
            var bytes = region.Readable ? (long)Math.Min(region.Size, long.MaxValue) : 0;
            total = long.MaxValue - (total ?? 0) < bytes ? long.MaxValue : (total ?? 0) + bytes;
        }

        return total;
    }

    /// <summary>The runtime from the first region that maps <c>libcoreclr.dylib</c>; null when none does (a single-file app).</summary>
    public static RuntimeFacts? Runtime(IEnumerable<MacRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        foreach (var region in regions)
        {
            if (region.Path is { } path && RuntimeLibrary.FromPath(path, RuntimeLibraryFile) is { } runtime)
            {
                return runtime;
            }
        }

        return null;
    }

    /// <summary>
    /// The command line from <c>KERN_PROCARGS2</c>: the number of arguments, the executable's path, NULs, the arguments, then
    /// the environment. Null when the buffer is cut before the arguments end.
    /// </summary>
    public static string? CommandLine(ReadOnlySpan<byte> procArgs) =>
        TryArguments(procArgs, out var arguments, out _) ? ProcText.CommandLine(string.Concat(arguments.Select(a => a + '\0'))) : null;

    /// <summary>
    /// What the environment after the arguments says about the diagnostics socket. Null when there is none to read: macOS
    /// leaves it out for a protected process, which reads the same as a process without one.
    /// </summary>
    public static TargetEnvironment? Environment(ReadOnlySpan<byte> procArgs)
    {
        if (!TryArguments(procArgs, out _, out var at))
        {
            return null;
        }

        List<string> entries = [];

        // An empty string ends the environment; what macOS keeps after it (its own loader strings) is not the process's.
        while (NextString(procArgs, ref at) is { Length: > 0 } entry)
        {
            entries.Add(entry);
        }

        return entries.Count == 0 ? null : new TargetEnvironment(entries);
    }

    private static bool TryArguments(ReadOnlySpan<byte> procArgs, out List<string> arguments, out int at)
    {
        arguments = [];
        at = sizeof(int);
        if (procArgs.Length < sizeof(int))
        {
            return false;
        }

        // A hostile or misread count is bounded by the bytes there are: every argument takes at least its NUL.
        var count = BinaryPrimitives.ReadInt32LittleEndian(procArgs);
        if (count < 0 || count > procArgs.Length || NextString(procArgs, ref at) is null)
        {
            return false;
        }

        while (at < procArgs.Length && procArgs[at] == 0)
        {
            at++;
        }

        for (var i = 0; i < count; i++)
        {
            if (NextString(procArgs, ref at) is not { } argument)
            {
                return false;
            }

            arguments.Add(argument);
        }

        return true;
    }

    /// <summary>The string at <paramref name="at"/>, which moves past its NUL; null at the end of the buffer or when it is cut.</summary>
    private static string? NextString(ReadOnlySpan<byte> procArgs, ref int at)
    {
        var rest = procArgs[at..];
        var end = rest.IndexOf((byte)0);
        if (end < 0)
        {
            return null;
        }

        at += end + 1;
        return Encoding.UTF8.GetString(rest[..end]);
    }
}
