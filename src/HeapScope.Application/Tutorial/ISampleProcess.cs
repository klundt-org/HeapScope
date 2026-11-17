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

namespace HeapScope.Application.Tutorial;

/// <summary>A running sample process, as <see cref="SampleService"/> sees it (a seam: tests start none).</summary>
public interface ISampleProcess : IDisposable
{
    int Id { get; }

    /// <summary>When it started, in UTC: with <see cref="Id"/> it names this process even after its pid is reused.</summary>
    DateTime StartTimeUtc { get; }

    bool HasExited { get; }

    /// <summary>Its exit code once it exited, else null.</summary>
    int? ExitCode { get; }

    /// <summary>What the sample wrote to its standard error so far (its refusal, when it exits at once).</summary>
    string Error { get; }

    /// <summary>Raised once when it exits, on any thread.</summary>
    event EventHandler? Exited;

    /// <summary>The next line the sample writes to its standard output, or null at its end.</summary>
    Task<string?> ReadLineAsync(CancellationToken cancellationToken);

    /// <summary>Waits until it exited and its standard error is read to the end.</summary>
    Task WaitForExitAsync(CancellationToken cancellationToken);

    void Kill();
}
