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

namespace HeapScope.Core;

/// <summary>
/// A request the worker understood and refused for a reason the user can act on (sprint 3.12, B5): an unknown column or
/// inspection, a filter that is not a valid expression, a baseline that is the snapshot itself. Its message is shown as it is.
/// Any other <see cref="ArgumentException"/> that reaches the worker's boundary is a bug: logged as an error with its stack,
/// and the user sees a fixed message with the id that finds it in the log.
/// </summary>
public sealed class RequestRefusedException : ArgumentException
{
    public RequestRefusedException()
    {
    }

    public RequestRefusedException(string message)
        : base(message)
    {
    }

    public RequestRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public RequestRefusedException(string message, string? paramName)
        : base(message, paramName)
    {
    }
}
