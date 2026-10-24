using System.Globalization;
using Basin.Testing;
using Xunit;
using static TabWm.Tests.Prerequisites;

namespace TabWm.Tests;

public sealed class AccessibilityTests
{
    private static async Task<Session> StartAsync(AccessibilityBus bus, string home, string config)
    {
        var environment = bus.Apply(new Dictionary<string, string?>())
            .ToDictionary(pair => pair.Key, pair => pair.Value!);
        var session = new Session(Compositor(), home: home, environment: environment, extra: ["--no-xwayland", "--no-restore", "--config", config]);
        await session.DisplayAsync();
        Assert.NotNull(await session.WaitForPrefixAsync("LAUNCHER "));
        return session;
    }

    private static string Config(string home, string enabled)
    {
        var path = Path.Combine(home, "tab-wm.toml");
        File.WriteAllText(path, $"[accessibility]\nenabled = \"{enabled}\"\n[session]\nrestore = false\n");
        return path;
    }

    [Fact]
    public async Task The_strip_is_on_the_bus_where_it_is_drawn_and_a_reader_opens_a_tab_and_changes_a_setting()
    {
        using var bus = await AccessibilityBus.StartAsync();
        var home = Directory.CreateTempSubdirectory("tab-wm-tests-").FullName;
        try
        {
            using var session = await StartAsync(bus, home, Config(home, "on"));
            Assert.NotNull(await session.WaitForAsync(l => l == "A11Y on", 0));
            using var reader = await bus.ConnectAsync();

            var add = await reader.FindAsync("New tab", "push button");
            await reader.FindAsync("Menu", "push button");
            var tree = await reader.WhereAsync(nodes =>
                nodes.Any(n => n.Role == "page tab list" && n.Name == "Tabs")
                && nodes.Any(n => n.Role == "page tab" && n.Name == "New Tab")
                && nodes.Any(n => n.Role == "entry" && n.Name == "Search apps")
                && nodes.Any(n => n.Role == "list item" && n.Name == "Foot"));
            Assert.DoesNotContain(tree, n => n.Name.Contains("TabWm.", StringComparison.Ordinal));
            Assert.Contains(tree, n => n.Role == "frame" && n.Name == "Tab strip");

            var line = (await session.AskAsync("element NewTabButton", "ELEMENT ")).Single();
            var box = line.Split(' ')[2].Split(',', 'x').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            Assert.Equal(new AtSpiRect(box[0], box[1], box[2], box[3]), await reader.ExtentsAsync(add));

            var mark = session.Mark();
            Assert.True(await reader.DoActionAsync(add, "click"));
            for (var i = 0; i < 100 && (await session.TabsAsync()).Count != 2; i++)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            Assert.Equal(2, (await session.TabsAsync()).Count);

            await session.SendAsync("settings show general");
            Assert.NotNull(await session.WaitForAsync(l => l.StartsWith("SETTINGS shown section=general", StringComparison.Ordinal), mark));
            await reader.FindAsync("Light or dark", "combo box");
            await reader.FindAsync("Appearance", "heading");
            var close = await reader.FindAsync("Close a tab when its last window closes", "check box");
            Assert.True(await reader.DoActionAsync(close, "click"));
            Assert.NotNull(await session.WaitForAsync(l => l == "SETTINGS saved tabs.close-when-empty", mark));
            Assert.Contains("close-when-empty = true", File.ReadAllText(Path.Combine(home, "tab-wm.toml")), StringComparison.Ordinal);
            await session.QuitCleanlyAsync();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task Auto_stays_off_on_the_headless_backend_and_nothing_reaches_the_bus()
    {
        using var bus = await AccessibilityBus.StartAsync();
        var home = Directory.CreateTempSubdirectory("tab-wm-tests-").FullName;
        try
        {
            using var session = await StartAsync(bus, home, Config(home, "auto"));
            Assert.NotNull(await session.WaitForAsync(l => l == "A11Y off", 0));
            using var reader = await bus.ConnectAsync();
            await session.SettleAsync();
            Assert.Empty(await reader.TreeAsync());
            await session.QuitCleanlyAsync();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    private static Tmds.DBus.Protocol.MessageBuffer KeyGrabs(Tmds.DBus.Protocol.DBusConnection connection, uint keysym, uint mask)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.a11y.Manager", "/org/freedesktop/a11y/Manager", "org.freedesktop.a11y.KeyboardMonitor", "SetKeyGrabs", "aua(uu)");
        writer.WriteArray(Array.Empty<uint>());
        var array = writer.WriteArrayStart(Tmds.DBus.Protocol.DBusType.Struct);
        if (keysym != 0)
        {
            writer.WriteStructureStart();
            writer.WriteUInt32(keysym);
            writer.WriteUInt32(mask);
        }

        writer.WriteArrayEnd(array);
        return writer.CreateMessage();
    }

    private static async Task ChordAsync(Session session)
    {
        await session.SendAsync("key 125 1");
        await session.SendAsync("key 20 1");
        await session.SendAsync("key 20 0");
        await session.SendAsync("key 125 0");
        await session.FrameCountAsync();
    }

    [Fact]
    public async Task A_reader_grab_takes_super_t_from_the_bindings_and_hears_it_until_it_lets_go()
    {
        using var bus = await AccessibilityBus.StartAsync();
        var home = Directory.CreateTempSubdirectory("tab-wm-tests-").FullName;
        try
        {
            using var session = await StartAsync(bus, home, Config(home, "on"));
            Assert.NotNull(await session.WaitForAsync(l => l == "A11Y manager on", 0));
            using var reader = new Tmds.DBus.Protocol.DBusConnection(bus.SessionAddress);
            await reader.ConnectAsync();
            var heard = new List<string>();
            await reader.AddMatchAsync(
                new Tmds.DBus.Protocol.MatchRule { Type = Tmds.DBus.Protocol.MessageType.Signal, Member = "KeyEvent" },
                static (Tmds.DBus.Protocol.Message m, object? _) =>
                {
                    var body = m.GetBodyReader();
                    var released = body.ReadBool();
                    var state = body.ReadUInt32();
                    return $"{(released ? "up" : "down")} {body.ReadUInt32():x} {state:x}";
                },
                (Tmds.DBus.Protocol.Notification<string> n) =>
                {
                    if (n.HasValue)
                    {
                        lock (heard)
                        {
                            heard.Add(n.Value);
                        }
                    }
                },
                false,
                Tmds.DBus.Protocol.ObserverFlags.None,
                null);
            await reader.CallMethodAsync(KeyGrabs(reader, 0x74, 0x40));

            await ChordAsync(session);
            Assert.Single(await session.TabsAsync());
            for (var i = 0; i < 100 && !Heard(heard, "down 74 40"); i++)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.True(Heard(heard, "down 74 40"), string.Join(", ", Snapshot(heard)));

            await reader.CallMethodAsync(KeyGrabs(reader, 0, 0));
            await ChordAsync(session);
            Assert.Equal(2, (await session.TabsAsync()).Count);
            await session.QuitCleanlyAsync();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    private static List<string> Snapshot(List<string> heard)
    {
        lock (heard)
        {
            return [.. heard];
        }
    }

    private static bool Heard(List<string> heard, string line) => Snapshot(heard).Contains(line);

    [Fact]
    public async Task A_tab_switch_is_announced_with_its_number_and_label()
    {
        using var bus = await AccessibilityBus.StartAsync();
        var home = Directory.CreateTempSubdirectory("tab-wm-tests-").FullName;
        try
        {
            using var session = await StartAsync(bus, home, Config(home, "on"));
            using var reader = await bus.ConnectAsync();
            await reader.ListenAsync();
            await reader.FindAsync("tab-wm", "frame");
            await ChordAsync(session);
            Assert.Equal(2, (await session.TabsAsync()).Count);
            await reader.WaitForAnnouncementAsync("Tab 2, New Tab");
            await session.QuitCleanlyAsync();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    private static async Task ReaderChordAsync(Session session)
    {
        foreach (var (code, pressed) in new[] { (125, 1), (56, 1), (31, 1), (31, 0), (56, 0), (125, 0) })
        {
            await session.SendAsync($"key {code} {pressed}");
        }

        await session.FrameCountAsync();
    }

    [Fact]
    public async Task Super_alt_s_starts_and_stops_the_screen_reader()
    {
        using var bus = await AccessibilityBus.StartAsync();
        var home = Directory.CreateTempSubdirectory("tab-wm-tests-").FullName;
        try
        {
            var log = Path.Combine(home, "reader.log");
            var stub = Path.Combine(home, "reader.sh");
            File.WriteAllText(stub, $"#!/bin/sh\necho \"started $$\" >> {log}\ntrap 'echo stopped >> {log}; exit 0' TERM\nwhile :; do sleep 0.1; done\n");
            File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var config = Path.Combine(home, "tab-wm.toml");
            File.WriteAllText(config, $"[accessibility]\nenabled = \"on\"\nscreen-reader = \"toggle\"\nscreen-reader-command = [\"{stub}\"]\n[session]\nrestore = false\n");
            using var session = await StartAsync(bus, home, config);
            Assert.NotNull(await session.WaitForAsync(l => l == "A11Y on", 0));
            var mark = session.Mark();
            await ReaderChordAsync(session);
            Assert.NotNull(await session.WaitForAsync(l => l.StartsWith("READER on pid=", StringComparison.Ordinal), mark));
            for (var i = 0; i < 100 && !(File.Exists(log) && File.ReadAllText(log).Contains("started", StringComparison.Ordinal)); i++)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            mark = session.Mark();
            await ReaderChordAsync(session);
            Assert.NotNull(await session.WaitForAsync(l => l == "READER off", mark));
            Assert.Contains("stopped", File.ReadAllText(log), StringComparison.Ordinal);
            await session.QuitCleanlyAsync();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task Super_alt_8_magnifies_the_outputs_but_a_capture_stays_whole()
    {
        var home = Directory.CreateTempSubdirectory("tab-wm-tests-").FullName;
        try
        {
            var config = Path.Combine(home, "tab-wm.toml");
            File.WriteAllText(config, "[accessibility]\nenabled = \"off\"\n[session]\nrestore = false\n");
            using var session = new Session(Compositor(), home: home, extra: ["--no-xwayland", "--no-restore", "--config", config]);
            await session.DisplayAsync();
            Assert.NotNull(await session.WaitForPrefixAsync("LAUNCHER "));
            async Task ChordAsync(int key)
            {
                foreach (var (code, pressed) in new[] { (125, 1), (56, 1), (key, 1), (key, 0), (56, 0), (125, 0) })
                {
                    await session.SendAsync($"key {code} {pressed}");
                }

                await session.FrameCountAsync();
            }

            async Task<byte[]> ShotAsync(string name)
            {
                await session.SettleAsync();
                var path = Path.Combine(home, $"{name}.png");
                var mark = session.Mark();
                await session.SendAsync($"shot {path}");
                Assert.NotNull(await session.WaitForAsync(l => l == $"SHOT {path}", mark));
                return File.ReadAllBytes(path);
            }

            var plain = await ShotAsync("plain");
            var mark = session.Mark();
            await ChordAsync(9);
            Assert.NotNull(await session.WaitForAsync(l => l == "ZOOM factor=2", mark));
            Assert.Equal(plain, await ShotAsync("zoomed"));
            mark = session.Mark();
            await ChordAsync(12);
            Assert.NotNull(await session.WaitForAsync(l => l == "ZOOM factor=1.6", mark));
            mark = session.Mark();
            await ChordAsync(9);
            Assert.NotNull(await session.WaitForAsync(l => l == "ZOOM factor=1", mark));
            await session.QuitCleanlyAsync();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task Super_alt_c_blacks_out_the_outputs_and_a_shot_while_keys_still_work()
    {
        var home = Directory.CreateTempSubdirectory("tab-wm-tests-").FullName;
        try
        {
            var config = Path.Combine(home, "tab-wm.toml");
            File.WriteAllText(config, "[accessibility]\nenabled = \"off\"\n[session]\nrestore = false\n");
            using var session = new Session(Compositor(), home: home, extra: ["--no-xwayland", "--no-restore", "--config", config]);
            await session.DisplayAsync();
            Assert.NotNull(await session.WaitForPrefixAsync("LAUNCHER "));
            async Task ChordAsync(int key)
            {
                foreach (var (code, pressed) in new[] { (125, 1), (56, 1), (key, 1), (key, 0), (56, 0), (125, 0) })
                {
                    await session.SendAsync($"key {code} {pressed}");
                }

                await session.FrameCountAsync();
            }

            async Task<bool> BlackAsync(string name)
            {
                await session.SettleAsync();
                var path = Path.Combine(home, $"{name}.png");
                var mark = session.Mark();
                await session.SendAsync($"shot {path}");
                Assert.NotNull(await session.WaitForAsync(l => l == $"SHOT {path}", mark));
                var (rgba, _, _) = Basin.Diagnostics.PngCodec.Decode(File.ReadAllBytes(path));
                for (var i = 0; i < rgba.Length; i += 4)
                {
                    if (rgba[i] != 0 || rgba[i + 1] != 0 || rgba[i + 2] != 0)
                    {
                        return false;
                    }
                }

                return true;
            }

            Assert.False(await BlackAsync("before"));
            var mark = session.Mark();
            await ChordAsync(46);
            Assert.NotNull(await session.WaitForAsync(l => l == "CURTAIN on", mark));
            Assert.True(await BlackAsync("curtain"), "a shot shows the screen under the curtain");
            mark = session.Mark();
            await ChordAsync(9);
            Assert.NotNull(await session.WaitForAsync(l => l == "ZOOM factor=2", mark));
            mark = session.Mark();
            await ChordAsync(46);
            Assert.NotNull(await session.WaitForAsync(l => l == "CURTAIN off", mark));
            Assert.False(await BlackAsync("after"));
            await session.QuitCleanlyAsync();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}
