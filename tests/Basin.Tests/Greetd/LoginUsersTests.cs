using Basin.Greetd;
using Tmds.DBus.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class LoginUsersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "basin-greetd-users-" + Guid.NewGuid().ToString("N"));

    public LoginUsersTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "icons"));
        File.WriteAllText(Path.Combine(_root, "passwd"), string.Join('\n',
            "root:x:0:0:root:/root:/bin/bash",
            "greeter:x:956:956:greetd greeter user:/var/lib/greetd:/usr/bin/nologin",
            "da:x:1000:1000:Tim Miller,,,:/home/da:/bin/bash",
            "guest:x:1001:1001::/home/guest:/bin/false",
            "zed:x:1002:1002:Zed:/home/zed:/usr/bin/zsh",
            "huge:x:70000:70000:Huge:/home/huge:/bin/sh",
            "homed:x:60500:60500:Homed User:/home/homed:/bin/bash",
            "broken line",
            "+nis:x:::::",
            ""));
        File.WriteAllText(Path.Combine(_root, "login.defs"), "# comment\nUID_MIN\t\t 1000\nUID_MAX   65000\nMAIL_DIR /var/mail\n");
        File.WriteAllText(Path.Combine(_root, "icons", "da"), "png");
    }

    private LoginUsersOptions Options(string? loginDefs = null) => new()
    {
        PasswdPath = Path.Combine(_root, "passwd"),
        LoginDefsPath = loginDefs ?? Path.Combine(_root, "login.defs"),
        IconDirectory = Path.Combine(_root, "icons"),
    };

    [Fact]
    public void The_uid_range_and_shell_filter_pick_the_people()
    {
        var users = LoginUsers.Read(Options());
        Assert.Equal(["homed", "da", "zed"], users.Select(u => u.Name));
        Assert.Equal("Tim Miller", users[1].RealName);
        Assert.Equal(Path.Combine(_root, "icons", "da"), users[1].IconPath);
        Assert.Null(users[2].IconPath);
    }

    [Fact]
    public void Missing_login_defs_uses_the_defaults_and_overrides_win()
    {
        Assert.Equal((LoginUsers.DefaultMinUid, LoginUsers.DefaultMaxUid), LoginUsers.UidRange(Options(Path.Combine(_root, "missing"))));
        Assert.Equal(["da", "zed"], LoginUsers.Read(Options(Path.Combine(_root, "missing"))).Select(u => u.Name));
        Assert.Equal((1000u, 65000u), LoginUsers.UidRange(Options()));
        var narrowed = LoginUsers.Read(new LoginUsersOptions { PasswdPath = Path.Combine(_root, "passwd"), LoginDefsPath = Path.Combine(_root, "login.defs"), MinUid = 1001 });
        Assert.Equal(["homed", "zed"], narrowed.Select(u => u.Name));
    }

    [Fact]
    public void A_hide_set_and_homed_uids_shape_the_list()
    {
        var users = LoginUsers.Read(new LoginUsersOptions
        {
            PasswdPath = Path.Combine(_root, "passwd"),
            LoginDefsPath = Path.Combine(_root, "missing"),
            Hide = new HashSet<string> { "zed" },
            HomedUids = new HashSet<uint> { 60500, 70000 },
        });
        Assert.Equal(["homed", "huge", "da"], users.Select(u => u.Name));
    }

    [Fact]
    public void Homed_uids_come_from_list_homes_and_a_missing_service_gives_a_reason()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        var missing = PortalBusTests.Await(host, HomedUsers.ListAsync(daemon.Address));
        Assert.Empty(missing.Uids);
        Assert.NotNull(missing.Reason);
        using var homed = PortalBusTests.Await(host, MockSystemService.StartAsync(daemon.Address, HomedUsers.ServiceName));
        homed.OnMethod = context =>
        {
            if (context.Request.MemberAsString != "ListHomes")
            {
                return false;
            }

            using var writer = context.CreateReplyWriter("a(susussso)");
            var array = writer.WriteArrayStart(DBusType.Struct);
            foreach (var (name, uid) in new[] { ("homed", 60500u), ("other", 60600u) })
            {
                writer.WriteStructureStart();
                writer.WriteString(name);
                writer.WriteUInt32(uid);
                writer.WriteString("active");
                writer.WriteUInt32(uid);
                writer.WriteString(name);
                writer.WriteString("/home/" + name);
                writer.WriteString("/bin/bash");
                writer.WriteObjectPath("/org/freedesktop/home1/home/" + name);
            }

            writer.WriteArrayEnd(array);
            context.Reply(writer.CreateMessage());
            return true;
        };
        var found = PortalBusTests.Await(host, HomedUsers.ListAsync(daemon.Address));
        Assert.Null(found.Reason);
        Assert.Equal(new HashSet<uint> { 60500, 60600 }, found.Uids);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
