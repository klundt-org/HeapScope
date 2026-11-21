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

using HeapScope.Application.Jobs;
using HeapScope.Application.Sessions;
using HeapScope.Capture;

namespace HeapScope.Application.Tests;

/// <summary>
/// Sprint 4.1 review M8 and H1: a pid that is not found carries the system's reason to the front ends, with its evidence in
/// the log, in a capture and in Force GC; and as root on Linux another user's full dump is refused for the library.
/// </summary>
public sealed class CaptureExplanationTests : IDisposable
{
    private const string Reason = "Process 77 turned its diagnostics off (DOTNET_EnableDiagnostics=0), so no tool can capture it.";
    private const string Evidence = "Linux check of pid 77: running yes; effective user 1000, HeapScope's 1000";

    private readonly string _root = Directory.CreateTempSubdirectory("heapscope-explain-").FullName;
    private readonly RecordingLogger<CaptureService> _logger = new();
    private readonly CountingWriter _writer = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_capture_of_a_pid_that_is_not_found_says_why_and_logs_what_that_rests_on()
    {
        var service = Service(new Discovery(found: null, new NotFoundExplanation(Reason, Evidence)));

        var failure = await Should.ThrowAsync<CaptureFailedException>(() => service.StartCapture(77).Result);

        failure.Reason.ShouldBe(CaptureFailure.ProcessNotFound);
        failure.Message.ShouldBe(Reason);
        _logger.Lines.ShouldContain($"Pid 77 was not found: {Evidence}");
    }

    [Fact]
    public async Task Force_gc_of_a_pid_that_is_not_found_says_why_too()
    {
        Assert.SkipUnless(CapturePlatform.IsSupported, "Force GC refuses first where capture is not built (ADR-025).");
        var service = Service(new Discovery(found: null, new NotFoundExplanation(Reason, Evidence)));

        var failure = await Should.ThrowAsync<CaptureFailedException>(() => service.ForceGcAsync(77, Ct));

        failure.Reason.ShouldBe(CaptureFailure.ProcessNotFound);
        failure.Message.ShouldBe(Reason);
    }

    [Fact]
    public async Task Without_a_reason_the_generic_text_stays_and_nothing_extra_is_logged()
    {
        var service = Service(new Discovery(found: null, NotFoundExplanation.None));

        var failure = await Should.ThrowAsync<CaptureFailedException>(() => service.StartCapture(77).Result);

        failure.Message.ShouldBe("No running .NET process with id 77 was found (or it has no diagnostics port).");
        _logger.Lines.ShouldNotContain(l => l.StartsWith("Pid 77 was not found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task As_root_on_linux_another_users_full_dump_is_refused_for_the_library_before_anything_is_written()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The runtime writes the dump as the target's user on Linux.");
        var other = new DotNetProcess(77, "app", null, "10.0.1", ElevationState.Elevated, 1 << 20, 1 << 20, null);
        var service = Service(new Discovery(other, NotFoundExplanation.None), elevated: true);

        var failure = await Should.ThrowAsync<CaptureFailedException>(() => service.StartCapture(77).Result);

        failure.Reason.ShouldBe(CaptureFailure.WriteFailed);
        failure.Message.ShouldStartWith("app (pid 77) runs as another user, and the runtime writes its full dump as that user");
        _writer.Calls.ShouldBe(0);
        Directory.EnumerateDirectories(_root).ShouldBeEmpty("no session is made for a refused capture");

        // A folder the user names is the user's call: the write's own failure would say why.
        var outside = Path.Combine(_root, "out.dmp");
        await service.StartCapture(77, outside).Result;
        _writer.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Off_windows_the_list_names_the_temp_folder_it_looked_in()
    {
        Assert.SkipUnless(CapturePlatform.IsSupported, "Listing refuses first where capture is not built (ADR-025).");
        var service = Service(new Discovery(found: null, NotFoundExplanation.None));

        await service.ListProcessesAsync(Ct);

        var line = _logger.Lines.ShouldHaveSingleItem();
        line.ShouldStartWith("Listed 0 .NET processes");
        if (OperatingSystem.IsWindows())
        {
            line.ShouldNotContain("diagnostics sockets looked for in");
        }
        else
        {
            line.ShouldContain($"; diagnostics sockets looked for in {Path.GetTempPath()} only");
            if (OperatingSystem.IsLinux())
            {
                // Review H6: the ids behind the marks.
                line.ShouldContain("; HeapScope's effective user ");
                line.ShouldEndWith("; another user's: none");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_process_whose_rights_are_unknown_never_needs_more_so_no_list_hides_it(bool heapScopeElevated)
    {
        // Sprint 4.1.1 review M3: the lists hide only what is known to need more rights than HeapScope has.
        Assert.SkipUnless(CapturePlatform.IsSupported, "Listing refuses first where capture is not built (ADR-025).");
        var gone = new DotNetProcess(77, "gone", null, "10.0.1", ElevationState.Unknown, null, null, null);
        var service = Service(new Discovery(gone, NotFoundExplanation.None), elevated: heapScopeElevated);

        var entry = (await service.ListProcessesAsync(Ct)).ShouldHaveSingleItem();

        entry.Elevation.ShouldBe(ProcessElevation.Unknown);
        entry.NeedsAdministrator.ShouldBeFalse();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private CaptureService Service(IProcessDiscovery discovery, bool elevated = false)
    {
        var elevation = new Elevation(elevated);
        return new CaptureService(
            discovery,
            new DumpCapturer(elevation, new PlentyOfDisk(), _writer, static _ => 1024),
            new SessionLibrary(Path.Combine(_root, "sessions")),
            new JobTracker(TimeProvider.System),
            elevation,
            logger: _logger);
    }

    private sealed class Discovery(DotNetProcess? found, NotFoundExplanation explanation) : IProcessDiscovery
    {
        public IReadOnlyList<DotNetProcess> List(CancellationToken cancellationToken = default) => found is null ? [] : [found];

        public DotNetProcess? Find(int processId) => found;

        public NotFoundExplanation WhyNotFound(int processId) => explanation;
    }

    private sealed class Elevation(bool elevated) : IElevationProbe
    {
        public bool IsCurrentProcessElevated => elevated;

        public ElevationState GetState(int processId) => ElevationState.Elevated;
    }

    private sealed class PlentyOfDisk : IDiskSpace
    {
        public long? AvailableBytes(string directory) => 1L << 40;
    }

    private sealed class CountingWriter : IDumpWriter
    {
        public int Calls { get; private set; }

        public Task WriteAsync(int processId, string path)
        {
            Calls++;
            return File.WriteAllBytesAsync(path, [0x4D, 0x44, 0x4D, 0x50]);
        }
    }
}
