using Basin.Testing;
using Tmds.DBus.Protocol;
using Xunit;

namespace TinyComp.Tests;

public sealed class AccessibilityProcessTests
{
    private static MessageBuffer KeyGrabs(DBusConnection connection, uint keysym, uint mask)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.a11y.Manager", "/org/freedesktop/a11y/Manager", "org.freedesktop.a11y.KeyboardMonitor", "SetKeyGrabs", "aua(uu)");
        writer.WriteArray(Array.Empty<uint>());
        var array = writer.WriteArrayStart(DBusType.Struct);
        if (keysym != 0)
        {
            writer.WriteStructureStart();
            writer.WriteUInt32(keysym);
            writer.WriteUInt32(mask);
        }

        writer.WriteArrayEnd(array);
        return writer.CreateMessage();
    }

    private static MessageBuffer HasOwner(DBusConnection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", "s");
        writer.WriteString("org.freedesktop.a11y.Manager");
        return writer.CreateMessage();
    }

    private static void Chord(QuillFrameTests.QuillSession session)
    {
        session.Send("key 56 1");
        session.Send("key 49 1");
        session.Send("key 49 0");
        session.Send("key 56 0");
    }

    [Fact]
    public async Task A_reader_grab_takes_alt_n_from_the_bindings_until_it_lets_go()
    {
        using var bus = await AccessibilityBus.StartAsync();
        var environment = new Dictionary<string, string>
        {
            ["DBUS_SESSION_BUS_ADDRESS"] = bus.SessionAddress,
            ["AT_SPI_BUS_ADDRESS"] = bus.A11yAddress,
        };
        using var session = QuillFrameTests.QuillSession.Start("pixman", "[accessibility]\nenabled = \"on\"\n", environment: environment);
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        using var reader = new DBusConnection(bus.SessionAddress);
        await reader.ConnectAsync();
        for (var i = 0; i < 100 && !await reader.CallMethodAsync(HasOwner(reader), static (m, _) => m.GetBodyReader().ReadBool(), null); i++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        await reader.CallMethodAsync(KeyGrabs(reader, 0x6e, 0x8));

        Chord(session!);
        Assert.Null(session!.WaitForLine("WORKSPACE + ", 1500));

        await reader.CallMethodAsync(KeyGrabs(reader, 0, 0));
        Chord(session);
        Assert.NotNull(session.WaitForLine("WORKSPACE + "));
    }

    [Fact]
    public async Task A_reader_reads_a_server_side_frame_and_closes_the_window_with_its_close_button()
    {
        using var bus = await AccessibilityBus.StartAsync();
        var environment = new Dictionary<string, string>
        {
            ["DBUS_SESSION_BUS_ADDRESS"] = bus.SessionAddress,
            ["AT_SPI_BUS_ADDRESS"] = bus.A11yAddress,
        };
        using var session = QuillFrameTests.QuillSession.Start("gl", "[accessibility]\nenabled = \"on\"\n[frame]\nstyle = \"quill\"\n", environment: environment);
        Assert.SkipWhen(session is null, "tinycomp, ssdwin or the gl row is not available beside the tests");
        using var reader = await bus.ConnectAsync();
        var frame = await reader.FindAsync("ssdwin");
        var buttons = await reader.ChildrenAsync(frame);
        Assert.Contains(buttons, n => n.Role == "push button" && n.Name == "Maximize");
        var close = buttons.Single(n => n.Role == "push button" && n.Name == "Close");
        var box = await reader.ExtentsAsync(close);
        Assert.True(box.Width > 0 && box.Height > 0, $"{box}");
        Assert.True(await reader.DoActionAsync(close, "click"));
        await reader.WhereAsync(nodes => !nodes.Any(n => n.Name == "ssdwin"));
    }

    [Fact]
    public async Task A_reader_moves_through_the_settings_panel_and_turns_a_switch_on()
    {
        using var bus = await AccessibilityBus.StartAsync();
        var environment = new Dictionary<string, string>
        {
            ["DBUS_SESSION_BUS_ADDRESS"] = bus.SessionAddress,
            ["AT_SPI_BUS_ADDRESS"] = bus.A11yAddress,
        };
        using var session = QuillFrameTests.QuillSession.Start("gl", "[accessibility]\nenabled = \"on\"\n", environment: environment);
        Assert.SkipWhen(session is null, "tinycomp, ssdwin or the gl row is not available beside the tests");
        using var reader = await bus.ConnectAsync();
        await reader.FindAsync("tinycomp", "frame");
        session!.Send("ipc tinycomp/settings {\"state\":\"open\"}");
        Assert.Contains("SETTINGS open=yes", session.WaitForLine("OK tinycomp/settings") ?? string.Empty);

        await reader.FindAsync("Settings");
        var effects = await reader.FindAsync("Effects", "list item");
        Assert.True(await reader.DoActionAsync(effects, "click"));
        var wobbly = await reader.FindAsync("Wobbly windows", "check box");
        Assert.False(await reader.HasStateAsync(wobbly, AtSpiState.Checked));
        Assert.True(await reader.DoActionAsync(wobbly, "click"));
        await reader.WhereAsync(nodes => nodes.Any(n => n.Name == "Wobbly windows" && n.Role == "check box"));
        session.Send("settings-state");
        var state = session.WaitForLine("SETTINGS ") ?? string.Empty;
        Assert.Contains(" wobbly=on", state);
        Assert.Contains(" dirty=1", state);
    }

    [Fact]
    public async Task The_open_inspector_is_a_window_that_names_its_panels()
    {
        using var bus = await AccessibilityBus.StartAsync();
        var environment = new Dictionary<string, string>
        {
            ["DBUS_SESSION_BUS_ADDRESS"] = bus.SessionAddress,
            ["AT_SPI_BUS_ADDRESS"] = bus.A11yAddress,
        };
        using var session = QuillFrameTests.QuillSession.Start("gl", "[accessibility]\nenabled = \"on\"\n", environment: environment);
        Assert.SkipWhen(session is null, "tinycomp, ssdwin or the gl row is not available beside the tests");
        using var reader = await bus.ConnectAsync();
        await reader.FindAsync("tinycomp", "frame");
        session!.Send("inspector open");
        Assert.Contains("open=yes", session.WaitForLine("INSPECTOR ") ?? string.Empty);
        await reader.WhereAsync(nodes => nodes.Any(n => n.Name == "Inspector") && nodes.Any(n => n.Role == "label" && n.Name.StartsWith("Panels: ", StringComparison.Ordinal)));
        session.Send("inspector close");
        await reader.WhereAsync(nodes => !nodes.Any(n => n.Name == "Inspector"));
    }

    [Fact]
    public async Task Super_alt_s_starts_and_stops_the_screen_reader()
    {
        using var bus = await AccessibilityBus.StartAsync();
        var home = Directory.CreateTempSubdirectory("tinycomp-reader-").FullName;
        try
        {
            var log = Path.Combine(home, "reader.log");
            var stub = Path.Combine(home, "reader.sh");
            File.WriteAllText(stub, $"#!/bin/sh\necho \"started $$\" >> {log}\ntrap 'echo stopped >> {log}; exit 0' TERM\nwhile :; do sleep 0.1; done\n");
            File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var environment = new Dictionary<string, string>
            {
                ["DBUS_SESSION_BUS_ADDRESS"] = bus.SessionAddress,
                ["AT_SPI_BUS_ADDRESS"] = bus.A11yAddress,
            };
            using var session = QuillFrameTests.QuillSession.Start("pixman", $"[accessibility]\nenabled = \"on\"\nscreen-reader = \"toggle\"\nscreen-reader-command = [\"{stub}\"]\n", environment: environment);
            Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
            foreach (var (code, pressed) in new[] { (125, 1), (56, 1), (31, 1), (31, 0), (56, 0), (125, 0) })
            {
                session!.Send($"key {code} {pressed}");
            }

            Assert.NotNull(session!.WaitForLine("READER on pid="));
            foreach (var (code, pressed) in new[] { (125, 1), (56, 1), (31, 1), (31, 0), (56, 0), (125, 0) })
            {
                session.Send($"key {code} {pressed}");
            }

            Assert.NotNull(session.WaitForLine("READER off"));
            Assert.Contains("stopped", File.ReadAllText(log), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void Super_alt_8_magnifies_and_super_alt_minus_steps_back_out()
    {
        using var session = QuillFrameTests.QuillSession.Start("pixman", "[input.keyboard]\nlayout = \"us\"\n");
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        void Chord(int key)
        {
            foreach (var (code, pressed) in new[] { (125, 1), (56, 1), (key, 1), (key, 0), (56, 0), (125, 0) })
            {
                session!.Send($"key {code} {pressed}");
            }
        }

        var plain = session!.Shot("plain");
        Chord(9);
        Assert.NotNull(session.WaitForLine("ZOOM factor=2"));
        var zoomed = session.Shot("zoomed");
        Assert.False(plain.Rgba.AsSpan().SequenceEqual(zoomed.Rgba), "the zoomed output looks the same as the plain one");
        Chord(13);
        Assert.NotNull(session.WaitForLine("ZOOM factor=2.5"));
        Chord(12);
        Assert.NotNull(session.WaitForLine("ZOOM factor=2"));
        Chord(9);
        Assert.NotNull(session.WaitForLine("ZOOM factor=1"));
        var back = session.Shot("back");
        Assert.True(plain.Rgba.AsSpan().SequenceEqual(back.Rgba), "the output did not come back to the plain view");
    }

    [Fact]
    public void Super_alt_c_draws_a_black_curtain_while_keys_still_reach_the_bindings()
    {
        using var session = QuillFrameTests.QuillSession.Start("pixman", "[input.keyboard]\nlayout = \"us\"\n");
        Assert.SkipWhen(session is null, "tinycomp or ssdwin is not available beside the tests");
        void Chord(int key)
        {
            foreach (var (code, pressed) in new[] { (125, 1), (56, 1), (key, 1), (key, 0), (56, 0), (125, 0) })
            {
                session!.Send($"key {code} {pressed}");
            }
        }

        static bool Black(byte[] rgba)
        {
            for (var i = 0; i < rgba.Length; i += 4)
            {
                if (rgba[i] != 0 || rgba[i + 1] != 0 || rgba[i + 2] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        Assert.False(Black(session!.Shot("before").Rgba));
        Chord(46);
        Assert.NotNull(session.WaitForLine("CURTAIN on"));
        Assert.True(Black(session.Shot("curtain").Rgba), "the presented frame shows the screen under the curtain");
        Chord(9);
        Assert.NotNull(session.WaitForLine("ZOOM factor=2"));
        Chord(9);
        Assert.NotNull(session.WaitForLine("ZOOM factor=1"));
        Chord(46);
        Assert.NotNull(session.WaitForLine("CURTAIN off"));
        Assert.False(Black(session.Shot("after").Rgba));
    }
}
