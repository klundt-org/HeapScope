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

namespace HeapScope.UI.Tests;

/// <summary>
/// The app's gen 0 budget (sprint 3.12 review H2, M2): only the environment variable sets it, so the app starts itself again
/// with it, once, never headless, under dotnet or when told not to; a smoke or screenshot run waits for its exit code.
/// </summary>
public sealed class Gen0BudgetTests
{
    private static Func<string, string?> Environment(params (string Name, string Value)[] set) =>
        name => set.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void The_app_relaunches_once_with_the_budget_and_only_where_it_should()
    {
        const string App = "HeapScope.exe";
        Gen0Budget.ShouldRelaunch(false, Environment(), App, supportedSystem: true).ShouldBeTrue();

        Gen0Budget.ShouldRelaunch(false, Environment((Gen0Budget.LaunchedAtVariable, "1")), App, true).ShouldBeFalse("never twice");
        Gen0Budget.ShouldRelaunch(false, Environment((Gen0Budget.Variable, "8000000")), App, true).ShouldBeFalse("a budget set by the user stays");
        Gen0Budget.ShouldRelaunch(false, Environment(("COMPlus_GCgen0size", "8000000")), App, true).ShouldBeFalse();
        Gen0Budget.ShouldRelaunch(false, Environment((Gen0Budget.SettingVariable, "default")), App, true).ShouldBeFalse("A/B runs");
        Gen0Budget.ShouldRelaunch(true, Environment(), App, true).ShouldBeFalse("headless");
        Gen0Budget.ShouldRelaunch(false, Environment(), "/usr/share/dotnet/dotnet", true).ShouldBeFalse("under dotnet");
        Gen0Budget.ShouldRelaunch(false, Environment(), null, true).ShouldBeFalse();
        Gen0Budget.ShouldRelaunch(false, Environment(), App, supportedSystem: false).ShouldBeFalse("not on macOS");
    }

    [Fact]
    public void The_relaunched_app_keeps_the_budget_to_itself()
    {
        var launched = new DateTime(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc);
        System.Environment.SetEnvironmentVariable(Gen0Budget.Variable, Gen0Budget.Value);
        System.Environment.SetEnvironmentVariable(Gen0Budget.LaunchedAtVariable, launched.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            Gen0Budget.Settle().ShouldBe("0x4000000 (64 MB)");
            System.Environment.GetEnvironmentVariable(Gen0Budget.Variable).ShouldBeNull("what the app starts does not inherit it (review M21)");
            System.Environment.GetEnvironmentVariable(Gen0Budget.LaunchedAtVariable).ShouldBeNull();

            // The first window is timed from the first process's start, read before the variable went (the review).
            Gen0Budget.LaunchedAt().ToUniversalTime().ShouldBe(launched);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(Gen0Budget.Variable, null);
            System.Environment.SetEnvironmentVariable(Gen0Budget.LaunchedAtVariable, null);
            Gen0Budget.ForgetLaunchForTests();
        }

        Gen0Budget.Value.ShouldBe("4000000", "64 MB in hexadecimal, as the runtime reads GC settings");
        Convert.ToInt64(Gen0Budget.Value, 16).ShouldBe(64L * 1024 * 1024);
    }
}
