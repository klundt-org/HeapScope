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
using System.Globalization;
using Microsoft.Diagnostics.NETCore.Client;

namespace HeapScope.Benchmarks;

/// <summary>
/// How long a process stops for each kind of snapshot (sprint 4.3 task 1): a full dump, a dump with the heap only
/// (<c>DumpType.WithHeap</c>) and a gcdump (taken by the command line, <c>--cli</c>) of a copy of this program holding a heap
/// of a given size. A thread of that copy ticks every millisecond; its longest gap is the stop. Each kind's first file is
/// opened by the command line; every file is deleted. Set HEAPSCOPE_DATA_FOLDER, so the command line's logs and caches stay
/// out of the user's. Measurement only.
/// </summary>
internal static class DumpKindsBenchmark
{
    private const string Answer = "answer: ";

    private static readonly string[] s_kinds = ["full", "heap", "gcdump"];

    public static int Run(BenchmarkArgs args)
    {
        if (args.Get("hold") is not null)
        {
            return Hold(args.GetInt("mb", 500));
        }

        var mb = args.GetInt("mb", 500);
        var runs = args.GetInt("runs", 3);
        if (args.Get("cli") is not { } cli || args.Get("folder") is not { } folder || Environment.GetEnvironmentVariable("HEAPSCOPE_DATA_FOLDER") is null)
        {
            Console.WriteLine("dump-kinds needs --cli <heapscope-cli.exe>, --folder <folder for the dumps, which are deleted> and HEAPSCOPE_DATA_FOLDER.");
            return 2;
        }

        Directory.CreateDirectory(folder);
        using var target = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, ["dump-kinds", "--hold", "true", "--mb", Text(mb)]) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true })!;
        try
        {
            Console.WriteLine("dump-kinds: " + target.StandardOutput.ReadLine());
            for (var run = 1; run <= runs; run++)
            {
                // The order turns each run, so none always comes first.
                for (var i = 0; i < s_kinds.Length; i++)
                {
                    Take(s_kinds[(i + run) % s_kinds.Length], run, target, cli, folder, args.Get("open") != "no");
                }
            }

            return 0;
        }
        finally
        {
            target.Kill();
            target.WaitForExit(5000);
        }
    }

    private static void Take(string kind, int run, Process target, string cli, string folder, bool open)
    {
        var path = Path.Combine(folder, kind + Text(run) + (kind == "gcdump" ? ".gcdump" : ".dmp"));
        Ask(target, "reset");
        var watch = Stopwatch.StartNew();
        var failure = string.Empty;
        try
        {
            if (kind == "gcdump")
            {
                var (code, text) = Cli(cli, "capture", "--pid", Text(target.Id), "--kind", "gcdump", "--out", path);
                failure = code == 0 ? string.Empty : "failed: " + text;
            }
            else
            {
                new DiagnosticsClient(target.Id).WriteDump(kind == "full" ? DumpType.Full : DumpType.WithHeap, path);
            }
        }
        catch (Exception ex) when (ex is ServerErrorException or ServerNotAvailableException or IOException)
        {
            failure = "failed: " + ex.Message;
        }

        var taken = watch.Elapsed;
        var stops = Ask(target, "get");
        var size = File.Exists(path) ? new FileInfo(path).Length / 1024.0 / 1024.0 : 0;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  run {run} {kind,-6} {size,8:N1} MB, taken in {taken.TotalMilliseconds,7:N0} ms, {stops} {failure}"));
        if (!File.Exists(path))
        {
            return;
        }

        if (run == 1 && open)
        {
            watch.Restart();
            var (code, text) = Cli(cli, "open", path);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"    opened with exit code {code} in {watch.Elapsed.TotalSeconds:N1} s"));
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(12))
            {
                Console.WriteLine("      " + line.TrimEnd());
            }
        }

        File.Delete(path);
        File.Delete(path + ".hsidx");
    }

    private static string Ask(Process target, string what)
    {
        target.StandardInput.WriteLine(what);
        target.StandardInput.Flush();
        // createdump writes to the target's output too: only its answers count.
        while (target.StandardOutput.ReadLine() is { } line)
        {
            if (line.StartsWith(Answer, StringComparison.Ordinal))
            {
                return line[Answer.Length..];
            }
        }

        return "the target is gone";
    }

    private static (int Code, string Text) Cli(string cli, params string[] arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(cli, arguments) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        var error = process.StandardError.ReadToEndAsync();
        var text = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, text + error.Result);
    }

    /// <summary>The target: a list of small objects and strings of about the size asked for, and the ticking thread.</summary>
    private static int Hold(int mb)
    {
        var kept = new List<Node>();
        while (GC.GetTotalMemory(false) < (long)mb * 1024 * 1024)
        {
            for (var i = 0; i < 1000; i++)
            {
                kept.Add(new Node(new string((char)('a' + (i % 26)), 40), new byte[(i % 8) * 64]));
            }
        }

        GC.Collect();
        long longest = 0, together = 0, stops = 0;
        var ticker = new Thread(() =>
        {
            var last = Stopwatch.GetTimestamp();
            while (true)
            {
                Thread.Sleep(1);
                var now = Stopwatch.GetTimestamp();
                var ms = (long)Stopwatch.GetElapsedTime(last, now).TotalMilliseconds;
                last = now;
                if (ms >= 50)
                {
                    Interlocked.Increment(ref stops);
                    Interlocked.Add(ref together, ms);
                }

                if (ms > Interlocked.Read(ref longest))
                {
                    Interlocked.Exchange(ref longest, ms);
                }
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest,
        };
        ticker.Start();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"a heap of {GC.GetTotalMemory(false) / 1024.0 / 1024.0:N0} MB, about {kept.Count * 3L:N0} objects"));
        while (Console.ReadLine() is { } line)
        {
            if (line == "reset")
            {
                Interlocked.Exchange(ref longest, 0);
                Interlocked.Exchange(ref together, 0);
                Interlocked.Exchange(ref stops, 0);
                Console.WriteLine(Answer + "ok");
            }
            else
            {
                // The ticker may not have run since the stop ended.
                Thread.Sleep(50);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{Answer}longest stop {Interlocked.Read(ref longest):N0} ms ({Interlocked.Read(ref stops)} of 50 ms or more, {Interlocked.Read(ref together):N0} ms together)"));
            }
        }

        return kept.Count > 0 ? 0 : 1;
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private sealed record Node(string Name, byte[] Data);
}
