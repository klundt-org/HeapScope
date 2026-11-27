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
using Microsoft.Diagnostics.NETCore.Client;

namespace HeapScope.Capture;

/// <summary>Lists running .NET processes (ARCHITECTURE.md 2). Faked in front end tests.</summary>
public interface IProcessDiscovery
{
    IReadOnlyList<DotNetProcess> List(CancellationToken cancellationToken = default);

    /// <summary>One process, or null when it is not a running .NET process with a diagnostics port.</summary>
    DotNetProcess? Find(int processId);

    /// <summary>
    /// What the last <see cref="List"/> left out: HeapScope's own processes and the ones it could not
    /// describe (they exited meanwhile), by pid, so the log can say why a process is not listed.
    /// </summary>
    DiscoveryOmissions LastOmitted => DiscoveryOmissions.None;

    /// <summary>
    /// Why a process that was asked for by pid is not found, with what to do, where the system can tell (on
    /// Linux and macOS, another user's process, diagnostics turned off, its own /tmp, or a socket in another temp folder), and what
    /// that rests on, for the log.
    /// </summary>
    NotFoundExplanation WhyNotFound(int processId) => NotFoundExplanation.None;
}

/// <summary>
/// Discovers .NET processes through their diagnostics ports (<c>DiagnosticsClient.GetPublishedProcesses</c>) and reads
/// what it can about each one. A detail that cannot be read is left empty; it never fails the list.
/// </summary>
public sealed class ProcessDiscovery(IElevationProbe? elevation = null) : IProcessDiscovery
{
    /// <summary>HeapScope's own executables live under this folder (the worker in its worker subfolder).</summary>
    private static readonly string s_ownFolder = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    private readonly IElevationProbe _elevation = elevation ?? CapturePlatform.ElevationProbe();

    // The image path, command line and runtime per system (IProcessFacts).
    private readonly IProcessFacts _facts = CapturePlatform.ProcessFacts();

    public DiscoveryOmissions LastOmitted { get; private set; } = DiscoveryOmissions.None;

    public IReadOnlyList<DotNetProcess> List(CancellationToken cancellationToken = default)
    {
        CapturePlatform.EnsureSupported();
        var result = new List<DotNetProcess>();
        List<int> own = [];
        List<int> notDescribed = [];
        foreach (var processId in DiagnosticsClient.GetPublishedProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsOwn(processId))
            {
                own.Add(processId);
            }
            else if (Describe(processId) is { } process)
            {
                result.Add(process);
            }
            else
            {
                notDescribed.Add(processId);
            }
        }

        LastOmitted = new DiscoveryOmissions(own, notDescribed);

        result.Sort(static (a, b) =>
        {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.ProcessId.CompareTo(b.ProcessId);
        });
        return result;
    }

    public DotNetProcess? Find(int processId)
    {
        CapturePlatform.EnsureSupported();
        return FindOnThisSystem(processId);
    }

    public NotFoundExplanation WhyNotFound(int processId) => CapturePlatform.Rules().WhyNotFound(processId);

    private DotNetProcess? FindOnThisSystem(int processId) =>
        !IsOwn(processId) && DiagnosticsClient.GetPublishedProcesses().Contains(processId) ? Describe(processId) : null;

    /// <summary>
    /// HeapScope itself (this process, and the UI, CLI and worker started from this installation) is never listed or
    /// captured: a dump of the worker would contain the snapshots it holds. Matched by pid and executable path, not by
    /// name, so a user's own app called HeapScope is still listed. In the CLI's dotnet tool the worker runs as the dotnet
    /// host, so its command line names the worker's assembly in this installation (ADR-039). The tutorial's
    /// sample (ADR-042) is HeapScope's too, but it is there to be captured, so it is listed.
    /// </summary>
    private bool IsOwn(int processId) =>
        processId == Environment.ProcessId
        || (_facts.ImagePath(processId) is { } image && IsOwnImage(image, s_ownFolder))
        || RunsOwnWorkerAssembly(_facts.CommandLine(processId), s_ownFolder);

    /// <summary>
    /// Whether <paramref name="image"/> is one of the installation's own executables in <paramref name="ownFolder"/>: any
    /// under it except the tutorial's sample in its <c>sample</c> subfolder.
    /// </summary>
    internal static bool IsOwnImage(string image, string ownFolder) =>
        HeapScope.Shared.PathIdentity.IsUnder(image, ownFolder)
        && !HeapScope.Shared.PathIdentity.IsUnder(image, Path.Combine(ownFolder, "sample"));

    /// <summary>Whether <paramref name="commandLine"/> runs the worker assembly of the installation in <paramref name="ownFolder"/>.</summary>
    internal static bool RunsOwnWorkerAssembly(string? commandLine, string ownFolder) =>
        commandLine is not null
        && commandLine.Contains(Path.Combine(ownFolder, "worker", "HeapScope.Worker.dll"), HeapScope.Shared.PathIdentity.Comparison());

    private DotNetProcess? Describe(int processId)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return null; // Exited since the list was taken.
        }

        using (process)
        {
            var elevation = _elevation.GetState(processId);
            var runtime = elevation == ElevationState.NotElevated || _elevation.IsCurrentProcessElevated ? _facts.Runtime(process) : null;
            return new DotNetProcess(
                processId,
                Try(() => process.ProcessName) ?? $"pid {processId}",
                _facts.CommandLine(processId),
                runtime?.Version,
                elevation,
                TryValue(() => process.WorkingSet64),
                _facts.PrivateBytes(process),
                TryValue(() => new DateTimeOffset(process.StartTime)),
                runtime?.Directory);
        }
    }

    private static T? Try<T>(Func<T?> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    private static T? TryValue<T>(Func<T> read)
        where T : struct
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }
}
