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
using System.Text;

namespace HeapScope.Application.Tutorial;

/// <summary>Starts the real sample with its output redirected, as the user, with no window.</summary>
internal sealed class SampleProcessStarter : ISampleProcessStarter
{
    public ISampleProcess Start(string fileName, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"{fileName} did not start.");
        }

        return new SampleProcess(process);
    }

    public DateTime? StartTimeUtcOf(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited ? null : process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    public void Kill(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill();
            process.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Exited meanwhile.
        }
    }

    private sealed class SampleProcess : ISampleProcess
    {
        private readonly Process _process;
        private readonly StringBuilder _error = new();

        public SampleProcess(Process process)
        {
            _process = process;
            Id = process.Id;
            StartTimeUtc = Read(() => process.StartTime.ToUniversalTime(), DateTime.UtcNow);
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is { } line)
                {
                    lock (_error)
                    {
                        // The sample's own messages, bounded: never more than its last few kilobytes.
                        _error.AppendLine(line);
                        if (_error.Length > 4096)
                        {
                            _error.Remove(0, _error.Length - 4096);
                        }
                    }
                }
            };
            _process.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
            _process.BeginErrorReadLine();
        }

        public event EventHandler? Exited;

        public int Id { get; }

        public DateTime StartTimeUtc { get; }

        public bool HasExited => Read(() => _process.HasExited, true);

        public int? ExitCode => Read<int?>(() => _process.HasExited ? _process.ExitCode : null, null);

        public string Error
        {
            get
            {
                lock (_error)
                {
                    return _error.ToString().Trim();
                }
            }
        }

        public Task<string?> ReadLineAsync(CancellationToken cancellationToken) => _process.StandardOutput.ReadLineAsync(cancellationToken).AsTask();

        // Process.WaitForExitAsync returns once the redirected error stream is read to its end too.
        public Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);

        public void Kill()
        {
            try
            {
                _process.Kill();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Exited already, or exiting (peer review 4.5 M8): a kill a timer fires must not end HeapScope.
            }
        }

        public void Dispose() => _process.Dispose();

        private static T Read<T>(Func<T> read, T fallback)
        {
            try
            {
                return read();
            }
            catch (InvalidOperationException)
            {
                return fallback;
            }
        }
    }
}
