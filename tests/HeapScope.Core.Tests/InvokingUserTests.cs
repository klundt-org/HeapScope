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

namespace HeapScope.Core.Tests;

/// <summary>
/// Sprint 4.8 (peer review 4.5 H17): HeapScope run with sudo on Linux keeps the invoking user's data folder and gives that user
/// what it writes. The user comes from sudo's variables and the password file; build/check-install.sh checks the owners as root
/// in CI's Linux container.
/// </summary>
public sealed class InvokingUserTests
{
    private const string Passwd = "root:x:0:0:root:/root:/bin/bash\nubuntu:x:1000:1000:Ubuntu:/home/ubuntu:/bin/bash\nsvc:x:998:998::relative:/usr/sbin/nologin\n";

    private static Func<string, string?> Sudo(string? uid, string? gid = "1000", string? user = "ubuntu") => name => name switch
    {
        "SUDO_UID" => uid,
        "SUDO_GID" => gid,
        "SUDO_USER" => user,
        _ => null,
    };

    [Fact]
    public void Sudos_variables_and_the_password_file_name_the_user_and_their_data_folder()
    {
        var user = InvokingUser.From(Sudo("1000"), Passwd).ShouldNotBeNull();

        user.ShouldBe(new InvokingUser(1000, 1000, "ubuntu", "/home/ubuntu"));
        user.DataFolder.Replace('\\', '/').ShouldBe("/home/ubuntu/.local/share/HeapScope", "the folder the user's own runs use");
    }

    [Theory]
    [InlineData(null, "1000", "not started by sudo")]
    [InlineData("0", "0", "sudo ran it for root itself")]
    [InlineData("1000", null, "no group")]
    [InlineData("abc", "1000", "not a number")]
    [InlineData("4242", "4242", "a user the password file does not know")]
    [InlineData("998", "998", "a home that is not a full path")]
    public void Without_a_user_to_give_files_to_there_is_none(string? uid, string? gid, string why)
    {
        InvokingUser.From(Sudo(uid, gid), Passwd).ShouldBeNull(why);
    }

    [Fact]
    public void A_missing_sudo_user_takes_the_name_from_the_password_file()
    {
        InvokingUser.From(Sudo("1000", user: null), "ubuntu:x:1000:1000::/home/ubuntu:/bin/sh\r\n").ShouldNotBeNull().Name.ShouldBe("ubuntu");
    }

    // Sprint 4.8 review H1: what decides which DAC root loads stays in root's own folder.
    [Fact]
    public void Roots_home_comes_from_the_password_file()
    {
        InvokingUser.RootHome(Passwd).ShouldBe("/root");
        InvokingUser.RootHome("root:x:0:0:root:/var/root:/bin/sh\n").ShouldBe("/var/root");
        InvokingUser.RootHome(null).ShouldBe("/root");
    }

    [Fact]
    public void Off_linux_and_when_not_root_no_one_is_given_anything()
    {
        Assert.SkipWhen(OperatingSystem.IsLinux() && Environment.IsPrivilegedProcess, "this run is root on Linux: build/check-install.sh checks that case");

        InvokingUser.Current.ShouldBeNull();
        InvokingUser.TrustFolder.ShouldBeNull();
        HeapScopeFolders.Default.DacCopies.ShouldBe(Path.Combine(HeapScopeFolders.Default.Root, "dac"));
        InvokingUser.Give(Path.GetTempPath()).ShouldBeFalse();
    }
}
