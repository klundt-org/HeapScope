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
using System.Runtime.InteropServices;

namespace HeapScope.Capture;

/// <summary>
/// A process's facts on Windows, asked of the kernel with limited query rights, so an elevated process's image path and
/// command line still read (moved from <see cref="ProcessDiscovery"/> in sprint 4.1).
/// </summary>
internal sealed partial class WindowsProcessFacts : IProcessFacts
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint MemCommit = 0x1000;
    private const uint PageNoAccess = 0x01;
    private const uint PageGuard = 0x100;
    private const int ProcessCommandLineInformation = 60;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    public static WindowsProcessFacts Instance { get; } = new();

    /// <summary>The executable path through QueryFullProcessImageName (limited query rights).</summary>
    public string? ImagePath(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new char[32768];
            var length = buffer.Length;
            return QueryFullProcessImageName(handle, 0, buffer, ref length) ? new string(buffer, 0, length) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>The command line through NtQueryInformationProcess (works with limited query rights).</summary>
    public string? CommandLine(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var length = 4096;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var buffer = Marshal.AllocHGlobal(length);
                try
                {
                    var status = NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, length, out var needed);
                    if (status == StatusInfoLengthMismatch && needed > length)
                    {
                        length = needed;
                        continue;
                    }

                    if (status != 0)
                    {
                        return null;
                    }

                    // UNICODE_STRING { ushort Length; ushort MaximumLength; IntPtr Buffer } followed by the characters.
                    var bytes = Marshal.ReadInt16(buffer) & 0xFFFF;
                    var chars = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                    return bytes == 0 ? null : Marshal.PtrToStringUni(chars, bytes / 2);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// The file version and folder of the process's coreclr.dll. Single-file apps link the runtime into their executable
    /// and have no coreclr.dll: their version stays unknown.
    /// </summary>
    public RuntimeFacts? Runtime(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            RuntimeFacts? found = null;
            foreach (ProcessModule module in process.Modules)
            {
                using (module)
                {
                    if (found is null && string.Equals(module.ModuleName, "coreclr.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        var version = module.FileVersionInfo;
                        found = new RuntimeFacts(
                            $"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}.{version.FilePrivatePart}",
                            Path.GetDirectoryName(module.FileName));
                    }
                }
            }

            return found;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    public int? EffectiveUserId(int processId) => null;

    public long? PrivateBytes(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            return process.PrivateMemorySize64;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The committed pages that are neither no-access nor guard pages, walked with VirtualQueryEx; null when the walk is denied.</summary>
    public long? ReadableBytes(int processId)
    {
        var handle = OpenProcess(ProcessQueryInformation, false, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            long total = 0;
            var info = default(MemoryBasicInformation);
            var size = (nuint)Marshal.SizeOf<MemoryBasicInformation>();
            ulong address = 0;
            while (VirtualQueryEx(handle, (nuint)address, out info, size) != 0)
            {
                if (info.State == MemCommit && (info.Protect & PageNoAccess) == 0 && (info.Protect & PageGuard) == 0)
                {
                    total += (long)info.RegionSize;
                }

                var next = (ulong)info.BaseAddress + (ulong)info.RegionSize;
                if (next <= address)
                {
                    break;
                }

                address = next;
            }

            return total;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public nuint BaseAddress;
        public nuint AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageName(IntPtr process, uint flags, [Out] char[] name, ref int size);

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr information, int length, out int returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nuint VirtualQueryEx(IntPtr process, nuint address, out MemoryBasicInformation buffer, nuint length);
}
