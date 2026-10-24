using Xunit;

namespace LunaWm.Tests;

public sealed class MenuTests
{
    private static bool Starts(string line, string prefix) => line.StartsWith(prefix, StringComparison.Ordinal);

    private static string WlClient(string name)
    {
        var root = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && root is not null; i++)
        {
            var candidate = Path.Combine(root, "scripts", "wlclients", "bin", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        }

        Assert.Skip($"scripts/wlclients/bin/{name} has not been built");
        return null!;
    }

    private static Dictionary<string, string> Buses(TempConfig config, PrivateBus session, PrivateBus system)
    {
        var environment = config.Environment;
        environment["DBUS_SESSION_BUS_ADDRESS"] = session.Address;
        environment["DBUS_SYSTEM_BUS_ADDRESS"] = system.Address;
        environment["LUNAWM_NO_POWER"] = "1";
        return environment;
    }

    [Theory]
    [InlineData("kde", "dbusmenu")]
    [InlineData("gtk", "gmenu")]
    public async Task A_client_menu_opens_from_the_title_and_activates(string protocol, string kind)
    {
        var appmenu = WlClient("appmenu");
        using var bus = PrivateBus.Start();
        using var system = PrivateBus.Start();
        using var config = new TempConfig();
        using var session = new LunaSession(LunaSession.Compositor(), config.Path, Buses(config, bus, system), "-c", $"{appmenu} {protocol} 60");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CARD + 1 ")));
        var imported = await session.WaitForAsync(line => Starts(line, "APPMENU 1 "));
        Assert.Equal(kind, imported!.Split(' ')[2]);

        await session.SendAsync("appmenu");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, $"APPMENU {kind} titles=2")));
        Assert.NotNull(await session.WaitForAsync(line => line == "    Wrap" || line == "    Wrap (checked)"));

        await session.SettleAsync();
        await session.SendAsync("touch 0.03 0.018");
        Assert.NotNull(await session.WaitForAsync(line => line == "MENU app on"));
        var rows = await session.WaitForAsync(line => Starts(line, "  menu app rows="), poke: "where", fresh: true);
        Assert.Equal("  menu app rows=4", rows);
        await session.SendAsync("gesture back");
        Assert.NotNull(await session.WaitForAsync(line => line == "MENU app off"));

        await session.SendAsync("appmenu-choose Edit/Wrap");
        Assert.NotNull(await session.WaitForAsync(line => line == "APPMENU chose Wrap"));
        Assert.NotNull(await session.WaitForAsync(line => line == "ACTIVATED wrap"));
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "FRAMES ")));
    }

    [Fact]
    public async Task An_app_with_no_menu_gets_close_and_fullscreen()
    {
        var ssdwin = WlClient("ssdwin");
        using var bus = PrivateBus.Start();
        using var system = PrivateBus.Start();
        using var config = new TempConfig();
        using var session = new LunaSession(LunaSession.Compositor(), config.Path, Buses(config, bus, system), "-c", ssdwin);
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CARD + 1 ")));
        await session.SendAsync("appmenu");
        Assert.NotNull(await session.WaitForAsync(line => line == "APPMENU local titles=2"));
        await session.SendAsync("appmenu-choose Fullscreen");
        Assert.NotNull(await session.WaitForAsync(line => line == "FULLSCREEN 1 on"));
        Assert.NotNull(await session.WaitForAsync(line => line == "BAR hidden"));
        await session.SendAsync("appmenu-choose Fullscreen");
        Assert.NotNull(await session.WaitForAsync(line => line == "FULLSCREEN 1 off"));
        await session.SendAsync("appmenu-choose Close");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CLOSE 1 ")));
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CARD - 1 ")));
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "FRAMES ")));
    }

    [Fact]
    public async Task A_help_desktop_action_runs_from_the_local_menu()
    {
        var ssdwin = WlClient("ssdwin");
        using var bus = PrivateBus.Start();
        using var system = PrivateBus.Start();
        using var config = new TempConfig();
        var marker = Path.Combine(config.Directory, "help-ran");
        var applications = Path.Combine(config.Directory, "data", "applications");
        Directory.CreateDirectory(applications);
        File.WriteAllText(Path.Combine(applications, "ssdwin.desktop"), $"[Desktop Entry]\nType=Application\nName=SSD Window\nExec={ssdwin}\nActions=help;\n\n[Desktop Action help]\nName=Help\nExec=touch {marker}\n");
        using var session = new LunaSession(LunaSession.Compositor(), config.Path, Buses(config, bus, system), "-c", ssdwin);
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CARD + 1 ")));
        await session.SendAsync("appmenu");
        Assert.NotNull(await session.WaitForAsync(line => line == "APPMENU local titles=3"));
        await session.SendAsync("appmenu-choose Help");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "LAUNCH Help pid=")));
        for (var i = 0; i < 50 && !File.Exists(marker); i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(marker), "the help action did not run");
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "FRAMES ")));
    }

    [Fact]
    public async Task Without_status_services_the_indicators_hide_and_the_menus_say_so()
    {
        using var bus = PrivateBus.Start();
        using var system = PrivateBus.Start();
        using var config = new TempConfig();
        using var session = new LunaSession(LunaSession.Compositor(), config.Path, Buses(config, bus, system));
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "SOCKET ")));
        var indicators = await session.WaitForAsync(line => Starts(line, "  indicators "), poke: "where", fresh: true);
        Assert.Equal("  indicators battery=none wifi=hidden bluetooth=hidden volume=hidden", indicators);

        await session.SendAsync("menu wifi");
        Assert.NotNull(await session.WaitForAsync(line => line == "MENU wifi on"));
        Assert.NotNull(await session.WaitForAsync(line => line == "  row note org.freedesktop.NetworkManager is not on the system bus", poke: "where", fresh: true));
        await session.SendAsync("menu device");
        Assert.NotNull(await session.WaitForAsync(line => line == "MENU device on"));
        Assert.NotNull(await session.WaitForAsync(line => line == "  row action Shut Down", poke: "where", fresh: true));
        await session.SendAsync("menu device close");
        Assert.NotNull(await session.WaitForAsync(line => line == "MENU device off"));
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "FRAMES ")));
    }

    [Fact]
    public async Task A_wpa_network_asks_for_a_passphrase_and_joins_system_wide()
    {
        using var networkManager = await BasinGreeter.Tests.FakeSystem.StartAsync();
        using var bus = PrivateBus.Start();
        using var config = new TempConfig();
        var environment = config.Environment;
        environment["DBUS_SESSION_BUS_ADDRESS"] = bus.Address;
        environment["DBUS_SYSTEM_BUS_ADDRESS"] = networkManager.Bus.Address;
        environment["LUNAWM_NO_POWER"] = "1";
        using var session = new LunaSession(LunaSession.Compositor(), config.Path, environment);
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "SOCKET ")));
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "  indicators ") && !line.Contains("wifi=hidden", StringComparison.Ordinal), poke: "where", fresh: true));
        await session.SendAsync("menu wifi");
        Assert.NotNull(await session.WaitForAsync(line => line == "MENU wifi on"));
        await session.SendAsync("wifi-join Lab");
        Assert.NotNull(await session.WaitForAsync(line => line == "WIFI ask Lab sae"));
        Assert.NotNull(await session.WaitForAsync(line => line == "  row passphrase Passphrase for Lab", poke: "where", fresh: true));
        await session.SendAsync("wifi-passphrase battery staple");
        Assert.NotNull(await session.WaitForAsync(line => line == "WIFI join Lab secured"));
        Assert.NotNull(await session.WaitForAsync(line => line == "WIFI Lab ok"));
        Assert.NotNull(await session.WaitForAsync(line => line == "  row note Joined Lab", poke: "where", fresh: true));
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "FRAMES ")));
    }

    [Fact]
    public async Task An_entry_with_a_documentation_key_gets_help_in_its_local_menu()
    {
        var ssdwin = WlClient("ssdwin");
        using var bus = PrivateBus.Start();
        using var system = PrivateBus.Start();
        using var config = new TempConfig();
        var applications = Path.Combine(config.Directory, "data", "applications");
        Directory.CreateDirectory(applications);
        File.WriteAllText(Path.Combine(applications, "ssdwin.desktop"), $"[Desktop Entry]\nType=Application\nName=SSD Window\nExec={ssdwin}\nX-DocPath=ssdwin/index.html\n");
        var opened = Path.Combine(config.Directory, "opened.txt");
        var viewer = Path.Combine(config.Directory, "helpview.sh");
        File.WriteAllText(viewer, $"#!/bin/sh\nprintf '%s' \"$1\" > {opened}\n");
        File.SetUnixFileMode(viewer, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(Path.Combine(applications, "helpview.desktop"), $"[Desktop Entry]\nType=Application\nName=Help Viewer\nExec={viewer} %u\nNoDisplay=true\n");
        var configHome = Directory.CreateDirectory(Path.Combine(config.Directory, "config")).FullName;
        File.WriteAllText(Path.Combine(configHome, "mimeapps.list"), "[Default Applications]\nx-scheme-handler/help=helpview.desktop\n");
        var environment = Buses(config, bus, system);
        environment["XDG_CONFIG_HOME"] = configHome;
        environment["XDG_CONFIG_DIRS"] = configHome;
        using var session = new LunaSession(LunaSession.Compositor(), config.Path, environment, "-c", ssdwin);
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "CARD + 1 ")));
        await session.SendAsync("appmenu");
        Assert.NotNull(await session.WaitForAsync(line => line == "APPMENU local titles=3"));
        Assert.NotNull(await session.WaitForAsync(line => line == "  Help"));
        await session.SendAsync("appmenu-choose Help");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "LAUNCH Help Viewer pid=")));
        for (var i = 0; i < 50 && !File.Exists(opened); i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Equal("help:/ssdwin/index.html", File.ReadAllText(opened));
        await session.SendAsync("quit");
        Assert.NotNull(await session.WaitForAsync(line => Starts(line, "FRAMES ")));
    }
}
