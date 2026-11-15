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
using System.Globalization;

namespace HeapScope.UI;

/// <summary>
/// The app's 64 MB gen 0 budget (sprint 3.12, task 6; the review's H2 and M2). It halved the GC time inside the UI's lags
/// under load, but the runtime reads it only from the <c>DOTNET_GCgen0size</c> environment variable, before any code runs:
/// the runtime configuration's <c>System.GC.Gen0Size</c> is ignored. So the app, started without it, starts itself again with
/// it and waits for that process, returning its exit code: from outside it is still one launch, whose lifetime, exit code and
/// output are the app's (the review's integration run hung on a first process that exited while its copy held the output
/// open). The relaunched app removes the variable from its own environment once started, so nothing it starts inherits the
/// budget (the review's M21). Not on macOS, where relaunching
/// a bundle's executable shows a second Dock icon; not headless, under <c>dotnet</c>, or with <c>Ui__Gen0Budget=default</c>
/// (A/B runs). The worker never inherits it (it has its own GC settings).
/// </summary>
internal static class Gen0Budget
{
    /// <summary>The runtime's variable; GC settings are read as hexadecimal.</summary>
    public const string Variable = "DOTNET_GCgen0size";

    /// <summary>64 MB, in hexadecimal.</summary>
    public const string Value = "4000000";

    /// <summary>Set to "default" to run without the budget and without the relaunch.</summary>
    public const string SettingVariable = "Ui__Gen0Budget";

    /// <summary>Set by the first process to its start time (UTC ticks), so the first window is timed from the user's launch.</summary>
    public const string LaunchedAtVariable = "HEAPSCOPE_LAUNCHED_AT";

    /// <summary>What this process runs with, for the start line.</summary>
    public static string Current => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } value
        ? $"0x{value} ({Convert.ToInt64(value, 16) / (1024 * 1024)} MB)"
        : "the runtime's default";

    // Read by Settle before it removes the variable (the review: a lazily initialized field read it only after).
    private static DateTime? s_launchedAt;

    /// <summary>When the user started HeapScope: the first process's start for a relaunched one, else this process's.</summary>
    public static DateTime LaunchedAt() => s_launchedAt ??= ReadLaunchedAt();

    /// <summary>Forgets the launch time a test made <see cref="Settle"/> read.</summary>
    internal static void ForgetLaunchForTests() => s_launchedAt = null;

    private static DateTime ReadLaunchedAt()
    {
        if (long.TryParse(Environment.GetEnvironmentVariable(LaunchedAtVariable), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
        {
            return new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
        }

        using var self = Process.GetCurrentProcess();
        return self.StartTime;
    }

    /// <summary>Whether this process should start itself again with the budget.</summary>
    internal static bool ShouldRelaunch(bool headless, Func<string, string?> environment, string? processPath, bool supportedSystem) =>
        supportedSystem
        && !headless
        && string.IsNullOrEmpty(environment(Variable))
        && string.IsNullOrEmpty(environment("COMPlus_GCgen0size"))
        && string.IsNullOrEmpty(environment(LaunchedAtVariable))
        && !string.Equals(environment(SettingVariable), "default", StringComparison.OrdinalIgnoreCase)
        && processPath is not null
        && !Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Removes the budget's variables from this process's environment once the runtime has read them (the review's M21), so
    /// the processes the app starts (Explorer, a sample app, the worker) run with their own GC settings. Returns the budget
    /// this process runs with, for its start line.
    /// </summary>
    public static string Settle()
    {
        var current = Current;
        s_launchedAt = ReadLaunchedAt();
        Environment.SetEnvironmentVariable(Variable, null);
        Environment.SetEnvironmentVariable(LaunchedAtVariable, null);
        return current;
    }

    /// <summary>
    /// Starts this executable again with the budget: null when it should not or could not (this process then runs the app
    /// itself, with the default budget), else the exit code to return.
    /// </summary>
    public static int? Relaunch(string[] args, bool headless)
    {
        var path = Environment.ProcessPath;
        if (!ShouldRelaunch(headless, Environment.GetEnvironmentVariable, path, OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
        {
            return null;
        }

        try
        {
            using var self = Process.GetCurrentProcess();
            var info = new ProcessStartInfo(path!) { UseShellExecute = false };
            foreach (var arg in args)
            {
                info.ArgumentList.Add(arg);
            }

            info.Environment[Variable] = Value;
            info.Environment[LaunchedAtVariable] = self.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
            using var app = Process.Start(info);
            if (app is null)
            {
                return null;
            }

            app.WaitForExit();
            return app.ExitCode;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // The app runs here, without the budget; its start line says so.
            return null;
        }
    }
}
