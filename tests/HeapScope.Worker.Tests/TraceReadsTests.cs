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

using HeapScope.Core;
using HeapScope.Loaders.Trace;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace HeapScope.Worker.Tests;

/// <summary>Whose error it is when a trace fails (4.2.1): TraceEvent's on the file, or HeapScope's.</summary>
public sealed class TraceReadsTests
{
    [Fact]
    public void An_error_TraceEvent_throws_is_the_files()
    {
        // Not an .etlx: TraceEvent's own serializer refuses it.
        var path = Path.Combine(Path.GetTempPath(), $"heapscope-reads-{Guid.NewGuid():N}.etlx");
        File.WriteAllBytes(path, new byte[256]);
        try
        {
            var ex = Record.Exception(() => TraceReads.Open(path)).ShouldNotBeNull();

            TraceReads.IsFileError(ex).ShouldBeTrue(ex.ToString());
            TraceReads.Origin(ex).ShouldContain(" at Microsoft.Diagnostics.");
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            File.Delete(path);
        }
    }

    [Fact]
    public void An_accessor_inlined_into_TraceReads_still_reads_as_the_files()
    {
        var ex = Record.Exception(() => TraceReads.Session((TraceLog)null!)).ShouldNotBeNull();

        TraceReads.IsFileError(ex).ShouldBeTrue(ex.ToString());
    }

    [Fact]
    public void HeapScopes_own_errors_are_not_the_files()
    {
        TraceReads.IsFileError(Thrown(() => throw new ArgumentOutOfRangeException("index"))).ShouldBeFalse();
        TraceReads.IsFileError(Thrown(() => throw new SnapshotFormatException("refused"))).ShouldBeFalse();
    }

    [Fact]
    public void The_disk_access_and_memory_are_not_the_files_fault()
    {
        // HeapScope's own temporary file: it is missing here.
        var missing = Record.Exception(() => TraceReads.Open(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "none.etlx"))).ShouldNotBeNull();

        missing.ShouldBeAssignableTo<IOException>();
        TraceReads.IsFileError(missing).ShouldBeFalse();
    }

    [Fact]
    public void Only_some_types_of_error_can_be_the_files()
    {
        TraceReads.CanBeTheFiles(new FormatException()).ShouldBeTrue();

        // TraceEvent's error for a value that ends past its event, since 4.0.2.
        TraceReads.CanBeTheFiles(new InvalidDataException()).ShouldBeTrue();
        TraceReads.CanBeTheFiles(new EndOfStreamException()).ShouldBeTrue();
        TraceReads.CanBeTheFiles(new IndexOutOfRangeException()).ShouldBeTrue();
        TraceReads.CanBeTheFiles(new IOException()).ShouldBeFalse();
        TraceReads.CanBeTheFiles(new UnauthorizedAccessException()).ShouldBeFalse();
        TraceReads.CanBeTheFiles(new InsufficientMemoryException()).ShouldBeFalse();
        TraceReads.CanBeTheFiles(new OperationCanceledException()).ShouldBeFalse();
    }

    [Fact]
    public void Amounts_no_process_allocates_are_refused()
    {
        var total = 0L;
        AllocationTraceReader.RefusedAmount(100_000, ref total).ShouldBeNull();
        total.ShouldBe(100_000);
        AllocationTraceReader.RefusedAmount(-1, ref total).ShouldNotBeNull().ShouldContain("an allocation of");
        AllocationTraceReader.RefusedAmount((1L << 40) + 1, ref total).ShouldNotBeNull().ShouldContain("an allocation of");
        total = (long.MaxValue / 2) - 1;
        AllocationTraceReader.RefusedAmount(1, ref total).ShouldBeNull();
        AllocationTraceReader.RefusedAmount(1, ref total).ShouldNotBeNull().ShouldContain("together");
    }

    [Fact]
    public void Running_out_of_memory_is_told_from_other_problems_of_a_conversion()
    {
        // TraceEvent's words, seen with a memory limit set; a path that names the exception is not one.
        var other = new AllocationTraceReader.IssueWriter(3);
        other.Write("Exception reading C:\\dumps\\System.OutOfMemoryException.nettrace\n");
        (other.Count, other.RanOutOfMemory).ShouldBe((1, false));

        var memory = new AllocationTraceReader.IssueWriter(3);
        memory.Write("***** The following Exception was thrown during processing *****\nSystem.OutOfMemoryException: Exception of type 'System.OutOfMemoryException' was thrown.\n");
        (memory.Count, memory.RanOutOfMemory).ShouldBe((2, true));
    }

    private static Exception Thrown(Action action) => Record.Exception(action).ShouldNotBeNull();
}
