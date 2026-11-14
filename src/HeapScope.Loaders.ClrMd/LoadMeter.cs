// HeapScope
// Copyright (C) 2026 Ray Klundt
// https://github.com/rklundt/HeapScope
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
/// Measures a load phase by phase (sprint 3.12, R5): wall time, the process's CPU time, GC pause time and page faults, so a
/// slow load can be told apart as work, collections or reading the dump from disk.
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
        var cost = new LoadPhaseCost(phase, now.Elapsed - _last.Elapsed, now.Cpu - _last.Cpu, now.GcPause - _last.GcPause, now.Faults - _last.Faults);
        _costs.Add(cost);
        _last = now;
        return cost.Elapsed;
    }

    private Mark Now()
    {
        using var process = Process.GetCurrentProcess();
        return new Mark(_clock.Elapsed, process.TotalProcessorTime, GC.GetTotalPauseDuration(), PageFaults());
    }

    /// <summary>The process's page faults so far: every fault on Windows, major (from disk) faults on Linux; null elsewhere.</summary>
    internal static long? PageFaults()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var counters = new ProcessMemoryCounters { Size = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
                return GetProcessMemoryInfo(-1, ref counters, counters.Size) ? counters.PageFaultCount : null;
            }

            if (OperatingSystem.IsLinux())
            {
                // /proc/self/stat: the 12th field, after the parenthesized command name, is majflt.
                var stat = File.ReadAllText("/proc/self/stat");
                var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                return long.TryParse(fields[9], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var major) ? major : null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or IndexOutOfRangeException or EntryPointNotFoundException or DllNotFoundException)
        {
            // A measure, not a result: a load goes on without it.
        }

        return null;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessMemoryInfo(nint process, ref ProcessMemoryCounters counters, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
    }

    private readonly record struct Mark(TimeSpan Elapsed, TimeSpan Cpu, TimeSpan GcPause, long? Faults);
}
