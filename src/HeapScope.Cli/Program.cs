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

using HeapScope.Cli;

// Last chances (peer review 4.5 H16), as the app and the worker have: logged before the process ends.
AppDomain.CurrentDomain.UnhandledException += (_, e) => CliApp.LogFatal("heapscope-cli ends: an exception nothing handled", e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString()));
TaskScheduler.UnobservedTaskException += (_, e) => CliApp.LogFatal("A task's exception nobody observed (the command goes on)", e.Exception);

return await CliApp.RunAsync(args, Console.Out, Console.Error, CliHost.Build, errorIsConsole: !Console.IsErrorRedirected).ConfigureAwait(false);
