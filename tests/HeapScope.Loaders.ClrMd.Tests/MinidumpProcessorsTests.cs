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


namespace HeapScope.Loaders.ClrMd.Tests;

/// <summary>The processors a Windows dump records (peer review 4.5), from a file that can be damaged.</summary>
public sealed class MinidumpProcessorsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("heapscope-processors-").FullName;

    [Theory]
    [InlineData(24, 24)]
    [InlineData(1, 1)]
    [InlineData(0, null)]
    [InlineData(255, null)]
    public void They_are_read_from_the_system_stream(byte recorded, int? expected) =>
        Processors(Dump(recorded, type: 7)).ShouldBe(expected);

    [Fact]
    public void A_dump_without_the_stream_or_cut_short_records_none()
    {
        var whole = Dump(24, type: 7);

        Processors(Dump(24, type: 9)).ShouldBeNull();
        Processors(whole[..20]).ShouldBeNull();
        Processors(whole[..40]).ShouldBeNull();
        Processors(whole[..^1]).ShouldBeNull();
        Processors([]).ShouldBeNull();
    }

    [Fact]
    public void A_directory_of_many_entries_can_be_cancelled()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var path = Path.Combine(_folder, "many.dmp");
        File.WriteAllBytes(path, Dump(24, type: 7));

        Should.Throw<OperationCanceledException>(() => MinidumpHeader.Processors(path, cancelled.Token));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private int? Processors(byte[] bytes)
    {
        var path = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".dmp");
        File.WriteAllBytes(path, bytes);
        return MinidumpHeader.Processors(path, TestContext.Current.CancellationToken);
    }

    // MINIDUMP_HEADER (32 bytes), one directory entry (12 bytes) and the start of a MINIDUMP_SYSTEM_INFO (8 bytes).
    private static byte[] Dump(byte processors, uint type)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(MinidumpHeader.MinidumpSignature);
        writer.Write(0xA793u);
        writer.Write(1u);
        writer.Write(32u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(MinidumpHeader.WithFullMemory);
        writer.Write(type);
        writer.Write(8u);
        writer.Write(44u);
        writer.Write([9, 0, 6, 0, 1, 2, processors, 1]);
        writer.Flush();
        return stream.ToArray();
    }
}
