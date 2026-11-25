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
using Microsoft.Diagnostics.NETCore.Client;

namespace HeapScope.Capture;

/// <summary>Free disk space for a folder. Faked in tests.</summary>
public interface IDiskSpace
{
    /// <summary>Bytes available to this user on the volume holding <paramref name="directory"/>; null when unknown.</summary>
    long? AvailableBytes(string directory);
}

/// <summary>
/// Reads free space with <c>GetDiskFreeSpaceEx</c> on the folder itself, so UNC paths and volumes mounted into folders
/// report the right volume (sprint 1.1 review). Elsewhere (sprint 4.1) through <see cref="DriveInfo"/> on the folder, which
/// asks the file system holding it (statfs), so a folder on another mount reports that mount.
/// </summary>
public sealed partial class VolumeDiskSpace : IDiskSpace
{
    public static VolumeDiskSpace Instance { get; } = new();

    public long? AvailableBytes(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                return new DriveInfo(directory).AvailableFreeSpace;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }
        }

        return GetDiskFreeSpaceEx(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, out var available, out _, out _) ? (long)available : null;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceEx(string directory, out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes);
}

/// <summary>The disk holding the dump folder has less free space than the dump will need.</summary>
public sealed class InsufficientDiskSpaceException : Exception
{
    public InsufficientDiskSpaceException()
    {
    }

    public InsufficientDiskSpaceException(string message)
        : base(message)
    {
    }

    public InsufficientDiskSpaceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public InsufficientDiskSpaceException(string directory, long neededBytes, long availableBytes, long reserveBytes)
        : base(string.Create(
            CultureInfo.InvariantCulture,
            $"The dump needs about {Gb(neededBytes)} and HeapScope keeps {Gb(reserveBytes)} free, but only {Gb(availableBytes)} is free on the drive of {directory}. Free some space or choose another folder."))
    {
        NeededBytes = neededBytes;
        AvailableBytes = availableBytes;
    }

    public long NeededBytes { get; }

    public long AvailableBytes { get; }

    private static string Gb(long bytes) => (bytes / (1024.0 * 1024 * 1024)).ToString("N1", CultureInfo.InvariantCulture) + " GB";
}

/// <summary>Writes a full dump of a .NET process through its diagnostics port.</summary>
public interface IDumpWriter
{
    /// <summary>
    /// Asks the runtime to write a full dump and waits until it has. It is never abandoned halfway: the runtime's
    /// createdump keeps writing whatever the caller does (sprint 1.1 review).
    /// </summary>
    Task WriteAsync(int processId, string path);
}

/// <summary>The runtime's own dump writer (createdump), started through the diagnostics IPC.</summary>
public sealed class DiagnosticsDumpWriter : IDumpWriter
{
    public static DiagnosticsDumpWriter Instance { get; } = new();

    public Task WriteAsync(int processId, string path) =>
        new DiagnosticsClient(processId).WriteDumpAsync(DumpType.Full, path, logDumpGeneration: false, CancellationToken.None);
}

/// <summary>Progress of a capture.</summary>
public sealed record CaptureProgress(string Phase, long? EstimatedBytes = null, string? Message = null);

/// <summary>
/// Captures a full dump of a .NET process (sprint 1.1): checks elevation (ADR-014) and free disk space before asking
/// the runtime to write the dump, writes to a temporary name and renames it only on success, and removes the file
/// when the capture fails or is canceled. A canceled capture waits for the runtime to finish writing (the target's
/// createdump cannot be stopped safely) and then deletes the file, so no dump is left behind.
/// </summary>
public sealed class DumpCapturer(
    IElevationProbe? elevation = null,
    IDiskSpace? disk = null,
    IDumpWriter? writer = null,
    Func<DotNetProcess, long>? estimator = null,
    TimeProvider? timeProvider = null)
{
    /// <summary>Free space HeapScope always leaves on the drive (the index cache and the system need room too).</summary>
    public const long ReserveBytes = 1L * 1024 * 1024 * 1024;

    /// <summary>Suffix of the file while the runtime writes it.</summary>
    public const string PartialSuffix = ".partial";

    private readonly IElevationProbe _elevation = elevation ?? CapturePlatform.ElevationProbe();
    private readonly IDiskSpace _disk = disk ?? VolumeDiskSpace.Instance;
    private readonly IDumpWriter _writer = writer ?? DiagnosticsDumpWriter.Instance;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Func<DotNetProcess, DumpEstimate> _estimate = estimator is null
        ? DumpSizeEstimator.Estimate
        : process => new DumpEstimate(estimator(process), null, process.PrivateBytes, 0, FromWalk: false);

    /// <summary>The estimated dump size of <paramref name="process"/>.</summary>
    public long Estimate(DotNetProcess process) => _estimate(process).Bytes;

    /// <summary>
    /// Why a full dump of <paramref name="process"/> cannot go into HeapScope's own folders (review H1 of sprint 4.1): on
    /// Linux and macOS, while HeapScope runs as root, the runtime writes another user's dump as that user, who cannot write to
    /// root's folders. Null when it can. A folder the user names (--out) is not judged: the write's own failure says.
    /// </summary>
    public static string? LibraryRefusal(DotNetProcess process, bool heapScopeElevated)
    {
        ArgumentNullException.ThrowIfNull(process);
        return heapScopeElevated && process.Elevation == ElevationState.Elevated
            ? CapturePlatform.Rules().LibraryRefusal(process.Name, process.ProcessId)
            : null;
    }

    /// <summary>Checks elevation and disk space; throws the specific exception with a next step when one fails.</summary>
    public long Preflight(DotNetProcess process, string path, Action<DumpEstimate, long?>? estimated = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (process.Elevation == ElevationState.Elevated && !_elevation.IsCurrentProcessElevated)
        {
            throw ElevationRequiredException.For(process.ProcessId, process.Name);
        }

        var directory = PartialSnapshotFile.DirectoryOf(path, "dump");
        var estimate = _estimate(process);
        var needed = estimate.Bytes;
        var free = _disk.AvailableBytes(directory);
        estimated?.Invoke(estimate, free);

        // Not needed + reserve, which a huge estimate would wrap (4.2 review).
        if (free is { } available && available - ReserveBytes < needed)
        {
            throw new InsufficientDiskSpaceException(directory, needed, available, ReserveBytes);
        }

        return needed;
    }

    /// <summary>
    /// Captures a full dump of <paramref name="process"/> to <paramref name="path"/>. Throws
    /// <see cref="ElevationRequiredException"/>, <see cref="InsufficientDiskSpaceException"/>,
    /// <see cref="DumpFileExistsException"/>, <see cref="TargetUnavailableException"/>,
    /// <see cref="DumpWriteFailedException"/> or <see cref="OperationCanceledException"/>. Returns when the runtime was done writing:
    /// the target is paused until a moment before, so nothing it allocates lies between (sprint 4.4).
    /// </summary>
    public async Task<DateTimeOffset> CaptureAsync(
        DotNetProcess process,
        string path,
        IProgress<CaptureProgress>? progress,
        CancellationToken cancellationToken,
        Action<DumpEstimate, long?>? estimated = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentException.ThrowIfNullOrEmpty(path);
        CapturePlatform.EnsureSupported();
        progress?.Report(new CaptureProgress("Checking"));
        Directory.CreateDirectory(PartialSnapshotFile.DirectoryOf(path, "dump"));
        var estimate = Preflight(process, path, estimated);
        var file = PartialSnapshotFile.Reserve(path, "dump");
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new CaptureProgress(
            "Writing dump",
            estimate,
            $"{process.Name} is paused while about {estimate / (1024.0 * 1024):N0} MB are written"));

        // Cancellation cannot stop the runtime's createdump; it is honored once the runtime has finished, by deleting
        // the file (sprint 1.1 review H1).
        using var canceling = cancellationToken.Register(() => progress?.Report(new CaptureProgress(
            "Canceling",
            estimate,
            "The runtime finishes writing the dump, then HeapScope deletes it")));
        DateTimeOffset written;
        try
        {
            await _writer.WriteAsync(process.ProcessId, file.PartialPath).ConfigureAwait(false);
            written = _time.GetUtcNow();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            file.Discard();
            throw Translate(process, file.Directory, ex);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            file.Discard();
            cancellationToken.ThrowIfCancellationRequested();
        }

        file.Commit();
        return written;
    }

    private Exception Translate(DotNetProcess process, string directory, Exception ex)
    {
        if (ex is UnauthorizedAccessException || (ex is ServerNotAvailableException && process.Elevation == ElevationState.Elevated))
        {
            return ElevationRequiredException.For(process.ProcessId, process.Name, ex);
        }

        if (ex is ServerNotAvailableException)
        {
            // It was found by its socket a moment ago, so a not-found reason would contradict that: only its evidence is kept (4.2 review).
            return new TargetUnavailableException($"{process.Name} (pid {process.ProcessId}) did not answer on its diagnostics port; it may have exited.", ex)
            {
                Evidence = CapturePlatform.Rules().WhyNotFound(process.ProcessId).Evidence,
            };
        }

        if (ex is DiagnosticsClientException)
        {
            // The dump is written by createdump under the target's own account, not HeapScope's.
            var (reason, evidence) = CapturePlatform.Rules().WhyNotWritten(ex.Message, process.Name, directory);
            return new DumpWriteFailedException(
                $"The dump of {process.Name} (pid {process.ProcessId}) could not be written: {reason ?? "the runtime refused the request"}. The target process writes the dump itself, under its own account; choose a folder it can write to (--out), and check free space. Details: {ex.Message}",
                ex)
            {
                Evidence = evidence,
            };
        }

        return ex is IOException
            ? new DumpWriteFailedException($"The dump could not be written to {directory}: {ex.Message}", ex)
            : ex;
    }
}
