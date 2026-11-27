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

using System.Globalization;
using System.Runtime.InteropServices;

namespace HeapScope.Core;

/// <summary>
/// The machine's memory as the system says it now: Windows' GlobalMemoryStatusEx, Linux's
/// <c>/proc/meminfo</c> (MemAvailable) capped by the process's cgroup limit and use, macOS's <c>sysctl</c> counts (untested). Only
/// when none can be read is it the runtime's reading at the last collection, 0 before the first, which <see cref="Source"/> says.
/// The worker's budget and the front ends' open warning both read it.
/// </summary>
public readonly partial record struct SystemMemory(long TotalBytes, long AvailableBytes, string Source)
{
    public static SystemMemory Current()
    {
        try
        {
            if (OperatingSystem.IsWindows() && Native.TryGetMemoryStatus(out var total, out var available))
            {
                return new SystemMemory((long)total, (long)available, "GlobalMemoryStatusEx");
            }

            if (OperatingSystem.IsLinux() && Linux() is { } linux)
            {
                return linux;
            }

            if (OperatingSystem.IsMacOS() && Mac() is { } mac)
            {
                return mac;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            // The runtime's reading below.
        }

        var info = GC.GetGCMemoryInfo();
        return new SystemMemory(info.TotalAvailableMemoryBytes, Math.Max(0, info.TotalAvailableMemoryBytes - info.MemoryLoadBytes), "the last garbage collection");
    }

    private static SystemMemory? Linux()
    {
        if (!File.Exists("/proc/meminfo"))
        {
            return null;
        }

        var (limitFile, usageFile) = CgroupFiles(File.Exists("/proc/self/cgroup") ? File.ReadAllText("/proc/self/cgroup") : string.Empty);

        // In a container without a cgroup namespace of its own the path names the host's: its files are then at the mount's root.
        if (limitFile is not null && !File.Exists(limitFile))
        {
            (limitFile, usageFile) = (Root(limitFile), Root(usageFile!));
        }

        var usage = ReadValue(usageFile);
        if (usage is { } used && usageFile is not null && Path.Combine(Path.GetDirectoryName(usageFile)!, "memory.stat") is var stat && File.Exists(stat))
        {
            // Page cache the kernel gives back counts as use: the mapped dump being read would shrink what is left.
            usage = Math.Max(0, used - InactiveFile(File.ReadAllText(stat)));
        }

        var memory = FromLinux(File.ReadAllText("/proc/meminfo"), ReadValue(limitFile), usage);

        // The runtime reads the cgroup limit through the mounts, as this cannot always: its total caps this one.
        var runtimeTotal = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return memory is { } m && runtimeTotal > 0 && runtimeTotal < m.TotalBytes
            ? new SystemMemory(runtimeTotal, Math.Min(m.AvailableBytes, Math.Max(0, runtimeTotal - GC.GetGCMemoryInfo().MemoryLoadBytes)), m.Source + " and the runtime's limit")
            : memory;

        static long? ReadValue(string? path) => path is not null && File.Exists(path) ? CgroupValue(File.ReadAllText(path)) : null;

        static string Root(string file) => file.StartsWith("/sys/fs/cgroup/memory/", StringComparison.Ordinal)
            ? "/sys/fs/cgroup/memory/" + Path.GetFileName(file)
            : "/sys/fs/cgroup/" + Path.GetFileName(file);
    }

    /// <summary>
    /// The machine's memory from <c>/proc/meminfo</c> (MemTotal, MemAvailable), within a cgroup's <paramref name="limit"/> and
    /// <paramref name="usage"/> when it sets a limit below the machine's; null when meminfo lacks MemTotal.
    /// </summary>
    internal static SystemMemory? FromLinux(string meminfo, long? limit, long? usage)
    {
        long? Field(string name)
        {
            foreach (var line in meminfo.Split('\n'))
            {
                if (line.StartsWith(name + ":", StringComparison.Ordinal)
                    && long.TryParse(line.AsSpan(name.Length + 1).Trim().TrimEnd("kB").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var kb))
                {
                    return kb * 1024;
                }
            }

            return null;
        }

        if (Field("MemTotal") is not { } total || total <= 0)
        {
            return null;
        }

        var available = Field("MemAvailable") ?? Field("MemFree") ?? 0;
        if (limit is { } max && max > 0 && max < total)
        {
            return new SystemMemory(max, Math.Max(0, Math.Min(available, max - (usage ?? 0))), "/proc/meminfo and the cgroup's limit");
        }

        return new SystemMemory(total, available, "/proc/meminfo");
    }

    /// <summary>
    /// The files of the memory limit and use of this process's cgroup, from <c>/proc/self/cgroup</c>: v2 (<c>0::/path</c>,
    /// memory.max and memory.current) or v1 (the memory controller's limit_in_bytes and usage_in_bytes); nulls when it names none.
    /// </summary>
    internal static (string? Limit, string? Usage) CgroupFiles(string selfCgroup)
    {
        foreach (var line in selfCgroup.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(':', 3);
            if (parts.Length != 3)
            {
                continue;
            }

            if (parts[0] == "0" && parts[1].Length == 0)
            {
                var folder = "/sys/fs/cgroup" + parts[2].TrimEnd('/');
                return (folder + "/memory.max", folder + "/memory.current");
            }

            if (parts[1].Split(',').Contains("memory"))
            {
                var folder = "/sys/fs/cgroup/memory" + parts[2].TrimEnd('/');
                return (folder + "/memory.limit_in_bytes", folder + "/memory.usage_in_bytes");
            }
        }

        return (null, null);
    }

    /// <summary>The inactive file pages of a cgroup's memory.stat (v2 <c>inactive_file</c>, v1 <c>total_inactive_file</c>), 0 when it names none.</summary>
    internal static long InactiveFile(string memoryStat)
    {
        // v1 lists the cgroup's own inactive_file before total_inactive_file, which counts its children as usage_in_bytes does.
        long? own = null, total = null;
        foreach (var line in memoryStat.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', 2);
            if (parts.Length == 2 && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var bytes))
            {
                (own, total) = parts[0] switch
                {
                    "inactive_file" => (bytes, total),
                    "total_inactive_file" => (own, bytes),
                    _ => (own, total),
                };
            }
        }

        return total ?? own ?? 0;
    }

    /// <summary>A cgroup file's bytes; null for "max" (no limit) or text that is not a number.</summary>
    internal static long? CgroupValue(string text) =>
        long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) ? bytes : null;

    // macOS (untested, like the rest of macOS): the machine's bytes, and the free, purgeable and file backed pages the system can
    // give back without paging out.
    private static SystemMemory? Mac()
    {
        if (Native.SysCtl("hw.memsize") is not { } total || Native.SysCtl("hw.pagesize") is not { } page
            || Native.SysCtl("vm.page_free_count") is not { } free)
        {
            return null;
        }

        var reclaimable = (Native.SysCtl("vm.page_purgeable_count") ?? 0) + (Native.SysCtl("vm.page_pageable_external_count") ?? 0);
        return new SystemMemory(total, Math.Min(total, (free + reclaimable) * page), "sysctl");
    }

    private static partial class Native
    {
        public static bool TryGetMemoryStatus(out ulong totalPhysical, out ulong availablePhysical)
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status))
            {
                totalPhysical = status.TotalPhys;
                availablePhysical = status.AvailPhys;
                return true;
            }

            totalPhysical = availablePhysical = 0;
            return false;
        }

        /// <summary>A numeric sysctl by name (4 or 8 bytes); null when it cannot be read.</summary>
        public static long? SysCtl(string name)
        {
            var buffer = new byte[8];
            nuint length = 8;
            if (SysCtlByName(name, buffer, ref length, IntPtr.Zero, 0) != 0)
            {
                return null;
            }

            return length switch
            {
                4 => BitConverter.ToUInt32(buffer, 0),
                8 => (long)BitConverter.ToUInt64(buffer, 0),
                _ => null,
            };
        }

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

        [LibraryImport("/usr/lib/libSystem.dylib", EntryPoint = "sysctlbyname", StringMarshalling = StringMarshalling.Utf8)]
        private static partial int SysCtlByName(string name, [Out] byte[] old, ref nuint oldLength, IntPtr newValue, nuint newLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }
    }
}
