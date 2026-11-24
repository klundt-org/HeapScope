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

    /// <summary>The size of <c>rusage_info_v0</c>.</summary>
    public const int UsageSize = 96;

    /// <summary>The library that holds the runtime, with the separator before it.</summary>
    public const string RuntimeLibraryFile = "/libcoreclr.dylib";

    /// <summary>More readable bytes than any process has: a layout misread, so the walk is not used.</summary>
    public const long MostReadableBytes = 1L << 44;

    private const int UserIdOffset = 36;
    private const int ProtectionOffset = 0;
    private const int ShareModeOffset = 60;
    private const int AddressOffset = 80;
    private const int SizeOffset = 88;
    private const int PathOffset = 248;
    private const int FootprintOffset = 72;
    private const uint ProtectionRead = 1;
    private const uint ShareModeEmpty = 3;

    /// <summary>More regions than any process has: a walk that does not end stops here.</summary>
    private const int MostRegions = 1_000_000;

    /// <summary>The effective user id in a <c>proc_bsdshortinfo</c> (<c>pbsi_uid</c>); null when it is another size.</summary>
    public static int? EffectiveUserId(ReadOnlySpan<byte> shortInfo) =>
        shortInfo.Length == ShortInfoSize ? BinaryPrimitives.ReadInt32LittleEndian(shortInfo[UserIdOffset..]) : null;

    /// <summary>The physical footprint in a <c>rusage_info_v0</c> (Activity Monitor's Memory); null when it is another size or 0.</summary>
    public static long? PhysicalFootprint(ReadOnlySpan<byte> usage) =>
        usage.Length == UsageSize && BinaryPrimitives.ReadUInt64LittleEndian(usage[FootprintOffset..]) is > 0 and <= MostReadableBytes and var bytes ? (long)bytes : null;

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
    /// Walks a process's regions in address order: <paramref name="read"/> gives the first at or after an address, or none
    /// with the outcome of the call. The walk ends there, when a region does not move it on, or (when
    /// <paramref name="stopAtRuntime"/>) at the runtime. One that ended early cannot be told from a whole one, so nothing
    /// is concluded from what it did not hold; how it ended is kept for the log.
    /// </summary>
    public static MacWalk Walk(Func<ulong, (MacRegion? Region, string Outcome)> read, bool stopAtRuntime = false)
    {
        ArgumentNullException.ThrowIfNull(read);
        ulong address = 0;
        var regions = 0;
        var withPath = 0;
        long bytes = 0;
        RuntimeFacts? runtime = null;
        string end;
        while (true)
        {
            var (found, outcome) = read(address);
            if (found is not { } region)
            {
                end = outcome;
                break;
            }

            regions++;
            bytes = region.Readable ? (long)Math.Min((ulong)bytes + Math.Min(region.Size, MostReadableBytes), 2 * (ulong)MostReadableBytes) : bytes;
            if (region.Path is { } path)
            {
                withPath++;
                runtime ??= RuntimeLibrary.FromPath(path, RuntimeLibraryFile);
            }

            var next = unchecked(region.Address + region.Size);
            if (next <= address)
            {
                end = "a region did not move it on";
                break;
            }

            address = next;
            if (stopAtRuntime && runtime is not null)
            {
                end = "the runtime was found";
                break;
            }

            if (regions == MostRegions)
            {
                end = "too many regions";
                break;
            }
        }

        return new MacWalk(regions, withPath, address, end, regions == 0 || bytes > MostReadableBytes ? null : bytes, runtime);
    }

    /// <summary>
    /// The command line from <c>KERN_PROCARGS2</c>: the number of arguments, the executable's path, NULs, the arguments, then
    /// the environment. Null when the buffer is cut before the arguments end.
    /// </summary>
    public static string? CommandLine(ReadOnlySpan<byte> procArgs) =>
        TryArguments(procArgs, out var arguments, out _, out _) ? ProcText.CommandLine(string.Concat(arguments.Select(a => a + '\0'))) : null;

    /// <summary>
    /// What the environment after the arguments says about the diagnostics socket. Null when there is none to read: macOS
    /// leaves it out for a protected process, which reads the same as a process without one.
    /// </summary>
    public static TargetEnvironment? Environment(ReadOnlySpan<byte> procArgs)
    {
        if (!TryArguments(procArgs, out _, out var entries, out var at))
        {
            return null;
        }

        // An empty string ends the environment; what macOS keeps after it (its own loader strings) is not the process's.
        while (NextString(procArgs, ref at) is { Length: > 0 } entry)
        {
            entries.Add(entry);
        }

        return entries.Count == 0 ? null : new TargetEnvironment(entries);
    }

    private static bool TryArguments(ReadOnlySpan<byte> procArgs, out List<string> arguments, out List<string> entries, out int at)
    {
        arguments = [];
        entries = [];
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

        // The padding cannot be told from an empty first argument, which would move an environment entry in as the last
        // argument. No environment value may reach the process list, so one that has an entry's shape counts as an entry.
        while (arguments.Count > 1 && IsEntry(arguments[^1]))
        {
            entries.Insert(0, arguments[^1]);
            arguments.RemoveAt(arguments.Count - 1);
        }

        return true;
    }

    /// <summary>Whether <paramref name="text"/> has the shape of an environment entry: a name of letters, digits and '_', then '='.</summary>
    private static bool IsEntry(string text)
    {
        var equals = text.IndexOf('=', StringComparison.Ordinal);
        return equals > 0 && !char.IsAsciiDigit(text[0]) && text.AsSpan(0, equals).IndexOfAnyExcept(s_nameCharacters) < 0;
    }

    private static readonly System.Buffers.SearchValues<char> s_nameCharacters =
        System.Buffers.SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_");

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
