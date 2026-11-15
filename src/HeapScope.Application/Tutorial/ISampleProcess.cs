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

namespace HeapScope.Application.Tutorial;

/// <summary>A running sample process, as <see cref="SampleService"/> sees it (a seam: tests start none).</summary>
public interface ISampleProcess : IDisposable
{
    int Id { get; }

    bool HasExited { get; }

    /// <summary>The next line the sample writes to its standard output, or null at its end.</summary>
    Task<string?> ReadLineAsync(CancellationToken cancellationToken);

    /// <summary>What the sample wrote to its standard error so far (its refusal, when it exits at once).</summary>
    string Error { get; }

    void Kill();
}

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

        var process = Process.Start(info) ?? throw new InvalidOperationException($"{fileName} did not start.");
        return new SampleProcess(process);
    }

    private sealed class SampleProcess : ISampleProcess
    {
        private readonly Process _process;
        private readonly System.Text.StringBuilder _error = new();

        public SampleProcess(Process process)
        {
            _process = process;
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is { } line)
                {
                    lock (_error)
                    {
                        _error.AppendLine(line);
                    }
                }
            };
            _process.BeginErrorReadLine();
        }

        public int Id => _process.Id;

        public bool HasExited => _process.HasExited;

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

        public void Kill()
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Exited already.
            }
        }

        public void Dispose() => _process.Dispose();
    }
}
