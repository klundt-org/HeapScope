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
using HeapScope.Capture;
using HeapScope.Core;
using HeapScope.Integration.Tests.Infrastructure;
using HeapScope.Loaders.Trace;

namespace HeapScope.Integration.Tests;

/// <summary>
/// A real allocations trace mutated at random (4.2.1): the reader answers with a trace or a format error within a bounded
/// time and a bounded amount of memory. A short pass in every run; the nightly job raises
/// <c>HEAPSCOPE_FUZZ_ITERATIONS</c> and varies <c>HEAPSCOPE_FUZZ_SEED</c>. The trace is recorded live, so a seed replays a
/// failure only with the same base: with <c>HEAPSCOPE_FUZZ_KEEP</c> set to a folder, the base and a failing input are kept
/// there, and <c>HEAPSCOPE_FUZZ_TRACE</c> names a kept base to replay from. Fixture data only.
/// </summary>
[Collection(nameof(HeavyTestsCollection))]
[Trait("Category", "Integration")]
public sealed class CraftedTraceTests : IDisposable
{
    /// <summary>What reading one damaged copy may allocate: far more than a whole one takes (measured below 60 MB).</summary>
    private const long MostBytes = 1L << 30;

    private readonly TempFolder _folder = new();

    [Fact]
    public async Task A_mutated_trace_is_read_or_refused_in_bounded_time_and_memory()
    {
        var token = TestContext.Current.CancellationToken;
        var iterations = int.TryParse(Environment.GetEnvironmentVariable("HEAPSCOPE_FUZZ_ITERATIONS"), CultureInfo.InvariantCulture, out var n) ? n : 20;
        var seed = int.TryParse(Environment.GetEnvironmentVariable("HEAPSCOPE_FUZZ_SEED"), CultureInfo.InvariantCulture, out var s) ? s : 1;
        Directory.CreateDirectory(_folder.Path);
        var basePath = Environment.GetEnvironmentVariable("HEAPSCOPE_FUZZ_TRACE") is { Length: > 0 } given ? given : await RecordAsync(token);
        var original = await File.ReadAllBytesAsync(basePath, token);
        var keep = Environment.GetEnvironmentVariable("HEAPSCOPE_FUZZ_KEEP");
        if (!string.IsNullOrEmpty(keep))
        {
            Directory.CreateDirectory(keep);
            await File.WriteAllBytesAsync(Path.Combine(keep, "base.nettrace"), original, token);
        }

        // The base itself must read, or every copy's refusal would say nothing.
        AllocationTraceReader.Read(basePath, null, _folder.File("etlx"), token).SampleCount.ShouldBeGreaterThan(0);

        var (opened, refused) = (0, 0);
        for (var i = 0; i < iterations; i++)
        {
            // Alone in the process (its collection), since the memory counted is the process's. Each iteration has its own numbers, so one replays alone.
            var random = new Random(HashCode(seed, i));
            var bytes = (byte[])original.Clone();
            for (var changes = random.Next(1, 8); changes > 0; changes--)
            {
                // The event descriptions sit near the start: favor the first kilobytes.
                var at = random.Next(3) == 0 ? random.Next(bytes.Length) : random.Next(Math.Min(bytes.Length, 16_384));
                bytes[at] = (byte)random.Next(256);
            }

            if (random.Next(8) == 0)
            {
                Array.Resize(ref bytes, random.Next(bytes.Length));
            }

            // A file of its own: TraceEvent leaves one it fails to open in use until the next collection (the worker collects
            // after every read).
            var mutated = _folder.File($"mutated-{i}.nettrace");
            await File.WriteAllBytesAsync(mutated, bytes, token);
            if (!string.IsNullOrEmpty(keep))
            {
                await File.WriteAllTextAsync(Path.Combine(keep, "trace-in-progress.txt"), $"seed {seed}, iteration {i}", token);
            }

            var before = GC.GetTotalAllocatedBytes();
            var read = Task.Run(
                () =>
                {
                    try
                    {
                        var trace = AllocationTraceReader.Read(mutated, null, _folder.File("etlx"), token);

                        // What opening a trace does with it: damaged amounts may not overflow the total.
                        trace.SampleBytes.Sum().ShouldBeGreaterThanOrEqualTo(0);
                        opened++;
                    }
                    catch (SnapshotFormatException ex)
                    {
                        // Refused with a reason: the expected answer for many mutations.
                        ex.Message.ShouldContain("record", Case.Insensitive);
                        refused++;
                    }
                },
                token);

            var ended = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(60), token)) == read;
            var allocated = GC.GetTotalAllocatedBytes() - before;
            if ((!ended || read.IsFaulted || allocated > MostBytes) && !string.IsNullOrEmpty(keep))
            {
                File.Copy(mutated, Path.Combine(keep, $"failing-seed{seed}-iteration{i}.nettrace"), overwrite: true);
            }

            ended.ShouldBeTrue($"seed {seed}, iteration {i}: the read did not end within 60 s");
            try
            {
                await read;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException($"seed {seed}, iteration {i}: {ex.GetType().Name}: {ex.Message}", ex);
            }

            allocated.ShouldBeLessThan(MostBytes, $"seed {seed}, iteration {i}: the read allocated {allocated:N0} bytes");
            Delete(mutated);
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{opened} read, {refused} refused");
        if (iterations >= 20)
        {
            // Refusing everything, or nothing, would pass the rest.
            opened.ShouldBeGreaterThan(0);
            refused.ShouldBeGreaterThan(0);
        }
    }

    public void Dispose()
    {
        Release();
        _folder.Dispose();
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            Release();
            File.Delete(path);
        }
    }

    private static void Release()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private static int HashCode(int seed, int iteration) => unchecked((seed * 1_000_003) + iteration);

    private async Task<string> RecordAsync(CancellationToken cancellationToken)
    {
        var path = _folder.File("base.nettrace");
        using var api = await FixtureProcess.StartApiAsync();
        var recording = TraceRecorder.RecordAsync(api.ProcessId, path, TraceProfile.Allocations, TimeSpan.FromSeconds(6), cancellationToken);
        while (!recording.IsCompleted)
        {
            await api.PostWorkAsync(20, cancellationToken);
        }

        await recording;
        return path;
    }
}
