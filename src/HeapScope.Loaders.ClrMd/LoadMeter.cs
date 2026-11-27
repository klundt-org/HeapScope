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
using HeapScope.Core.Loading;

namespace HeapScope.Loaders.ClrMd;

/// <summary>
/// Measures a load phase by phase: wall time, the process's CPU time, GC pause time and hard page faults
/// (pages read from disk), so a slow load can be told apart as work, collections or reading the dump from disk. CPU, GC and
/// faults are the whole process's: a query on another snapshot during a load counts in them.
/// </summary>
internal sealed partial class LoadMeter
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<LoadPhaseCost> _costs = [];
    private Mark _last;

    public LoadMeter() => _last = Now();

    /// <summary>The phases measured so far.</summary>
    public IReadOnlyList<LoadPhaseCost> Costs => _costs;

    /// <summary>Ends the phase running since the last call and returns its wall time.</summary>
    public TimeSpan End(string phase)
    {
        var now = Now();
        var cost = new LoadPhaseCost(phase, now.Elapsed - _last.Elapsed, now.Cpu - _last.Cpu, now.GcPause - _last.GcPause, Delta(_last.Faults, now.Faults));
        _costs.Add(cost);
        _last = now;
        return cost.Elapsed;
    }

    private Mark Now()
    {
        using var process = Process.GetCurrentProcess();
        return new Mark(_clock.Elapsed, process.TotalProcessorTime, GC.GetTotalPauseDuration(), HardFaults());
    }

    /// <summary>
    /// The faults between two readings: Windows counts them in 32 bits, so a count that wrapped is read as the distance forward
    /// null when either reading is missing.
    /// </summary>
    internal static long? Delta(ulong? before, ulong? after) => before is { } b && after is { } a
        ? (a >= b ? (long)(a - b) : (long)(uint)(a - b))
        : null;

    /// <summary>
    /// The process's hard page faults so far, the pages read from disk: Windows' per-process hard fault count (the memory counters' page faults count soft faults too), Linux's major faults; null elsewhere or when not readable.
    /// </summary>
    internal static ulong? HardFaults()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return WindowsHardFaults();
            }

            if (OperatingSystem.IsLinux())
            {
                // /proc/self/stat: the 12th field, after the parenthesized command name, is majflt.
                var stat = File.ReadAllText("/proc/self/stat");
                var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                return ulong.TryParse(fields[9], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var major) ? major : null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or IndexOutOfRangeException or EntryPointNotFoundException or DllNotFoundException)
        {
            // A measure, not a result: a load goes on without it.
        }

        return null;
    }

    // SYSTEM_PROCESS_INFORMATION on 64-bit Windows: NextEntryOffset at 0, HardFaultCount at 0x10, UniqueProcessId at 0x50.
    private const int SystemProcessInformation = 5;
    private const int HardFaultCountOffset = 0x10;
    private const int ProcessIdOffset = 0x50;
    private const uint InfoLengthMismatch = 0xC0000004;

    /// <summary>This process's hard fault count from the system's process list; null on 32-bit Windows or when it cannot be read.</summary>
    private static unsafe ulong? WindowsHardFaults()
    {
        if (IntPtr.Size != 8)
        {
            return null;
        }

        var length = 512u * 1024;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var buffer = NativeMemory.Alloc(length);
            try
            {
                var status = (uint)NtQuerySystemInformation(SystemProcessInformation, (nint)buffer, length, out var needed);
                if (status == InfoLengthMismatch)
                {
                    length = Math.Max(needed, length) * 2;
                    continue;
                }

                if (status != 0)
                {
                    return null;
                }

                var pid = (nint)Environment.ProcessId;
                var entry = (byte*)buffer;
                while (true)
                {
                    if (*(nint*)(entry + ProcessIdOffset) == pid)
                    {
                        return *(uint*)(entry + HardFaultCountOffset);
                    }

                    var next = *(uint*)entry;
                    if (next == 0)
                    {
                        return null;
                    }

                    entry += next;
                }
            }
            finally
            {
                NativeMemory.Free(buffer);
            }
        }

        return null;
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtQuerySystemInformation(int informationClass, nint information, uint length, out uint returnLength);

    private readonly record struct Mark(TimeSpan Elapsed, TimeSpan Cpu, TimeSpan GcPause, ulong? Faults);
}
