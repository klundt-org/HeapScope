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

namespace HeapScope.Worker;

/// <summary>The DAC lookup of a load (<see cref="WorkerOptions.Symbols"/>), with where each value came from, for the log.</summary>
/// <param name="DacFolder">The user's DAC folder, or null.</param>
/// <param name="DacFolderFrom">Where it came from: the command line or appsettings (the client passes both as <c>--dac-folder</c>), <c>Settings</c>, or none.</param>
/// <param name="AllowMicrosoftSymbolServer">Whether a missing DAC may be downloaded from Microsoft's symbol server.</param>
/// <param name="AllowFrom">Where it came from, as <paramref name="DacFolderFrom"/>, or off.</param>
public sealed record WorkerSymbols(string? DacFolder, string DacFolderFrom, bool AllowMicrosoftSymbolServer, string AllowFrom);
