using System.Diagnostics;
using Basin.Greetd;
using BasinGreeter.Tests;
using Xunit;

namespace LunaWm.Tests;

public sealed class GreeterChromeTests
{
    private const uint LeftMeta = 125;
    private const uint LeftAlt = 56;
    private const uint KeyS = 31;

    private static GreetdAuthMessage Secret => new(GreetdAuthMessageType.Secret, "Password: ");

    private static async Task<string> Ready(LunaSession session)
    {
        Assert.NotNull(await session.WaitForAsync(line => line == "GREETD connected"));
        return (await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)))!.Split(' ')[1];
    }

    private static async Task Expect(LunaSession session, string exact, int since = 0) =>
        Assert.True(await session.WaitForAsync(line => line == exact, since: since) is not null, $"never saw [{exact}]");

    private static Dictionary<string, string> With(Dictionary<string, string> environment, AccessibilityBus bus)
    {
        var applied = bus.Apply(environment.ToDictionary(pair => pair.Key, pair => (string?)pair.Value));
        return applied.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value!);
    }

    [Fact]
    public async Task A_session_chosen_from_the_pad_and_a_layout_from_the_device_menu_reach_greetd_and_are_remembered()
    {
        using var fixture = new GreeterFixture("[keyboard]\nlayouts = [\"us\", \"de\"]\n");
        fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
        using var session = fixture.Start();
        await Ready(session);
        await session.SendAsync("menu session");
        await Expect(session, "MENU session on");
        var mark = session.Mark();
        await session.SendAsync("menu-choose session:tinycomp");
        Assert.NotNull(await session.WaitForAsync(line => line == "MENU session off", since: mark));

        await session.SettleAsync();
        mark = session.Mark();
        var located = await session.WaitForAsync(line => line.StartsWith("LOCATE Session TinyComp ", StringComparison.Ordinal) && !line.EndsWith(" none", StringComparison.Ordinal), poke: "locate Session TinyComp", since: mark);
        Assert.NotNull(located);
        var at = located.Split(' ');
        await session.SendAsync($"touch {at[^2]} {at[^1]}");
        Assert.NotNull(await session.WaitForAsync(line => line == "MENU session on", since: mark));
        Assert.NotNull(await session.WaitForAsync(line => line == "  row action session:tinycomp TinyComp checked", poke: "where", since: mark));
        await session.SendAsync("menu session close");

        await session.SendAsync("menu device");
        await Expect(session, "MENU device on");
        mark = session.Mark();
        await session.SendAsync("where");
        await Expect(session, "  row header Accessibility | ");
        Assert.DoesNotContain(session.Lines().Skip(mark), line => line.StartsWith("  row action session:", StringComparison.Ordinal));
        await session.SendAsync("menu-choose layout:de");
        await Expect(session, "LAYOUT de");
        await session.SendAsync("menu device close");
        await session.SendAsync("select ada");
        await Expect(session, "AUTH secret Password: ");
        await session.SendAsync("type pw");
        await session.SendAsync("submit");
        await Expect(session, "START session=tinycomp");

        var start = Assert.IsType<GreetdStartSession>(fixture.Greetd.Requests()[^1]);
        Assert.Contains("XKB_DEFAULT_LAYOUT=de", start.Env);
        Assert.Contains("tinycomp", start.Cmd);
        var memory = File.ReadAllText(fixture.StatePath);
        Assert.Contains("session = \"tinycomp\"", memory, StringComparison.Ordinal);
        Assert.Contains("layout = \"de\"", memory, StringComparison.Ordinal);
    }

    private static async Task TapAvatar(LunaSession session, string name)
    {
        await session.SettleAsync();
        var mark = session.Mark();
        var located = await session.WaitForAsync(line => line.StartsWith($"LOCATE {name} ", StringComparison.Ordinal) && !line.EndsWith(" none", StringComparison.Ordinal), poke: $"locate {name}", since: mark);
        Assert.NotNull(located);
        var at = located!.Split(' ');
        await session.SendAsync($"touch {at[^2]} {at[^1]}");
    }

    [Fact]
    public async Task A_tap_on_another_avatar_while_the_pad_is_up_switches_to_that_user_without_reaching_greetd()
    {
        using var fixture = new GreeterFixture();
        fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
        using var session = fixture.Start();
        await Ready(session);
        await session.SendAsync("select ada");
        await Expect(session, "AUTH secret Password: ");
        var mark = session.Mark();
        await TapAvatar(session, "Charles Babbage");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("VIEW conversation user=Charles Babbage prompt=\"Password:\"", StringComparison.Ordinal), poke: "where", since: mark));
        await session.SendAsync("type pw");
        await session.SendAsync("submit");
        await Expect(session, "START session=sway");

        var requests = fixture.Greetd.Requests();
        Assert.Equal(new GreetdCreateSession("charles"), requests[0]);
        Assert.DoesNotContain(requests, request => request is GreetdCreateSession { Username: "ada" } or GreetdCancelSession);
    }

    [Fact]
    public async Task A_tap_on_another_avatar_keeps_the_keyboard_up_and_switches_on_the_first_tap()
    {
        using var fixture = new GreeterFixture();
        fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
        using var session = fixture.Start();
        await Ready(session);
        await session.SendAsync("select ada");
        await Expect(session, "AUTH secret Password: ");
        var mark = session.Mark();
        var located = await session.WaitForAsync(line => line.StartsWith("LOCATE Password: ", StringComparison.Ordinal) && !line.EndsWith(" none", StringComparison.Ordinal), poke: "locate Password:", since: mark);
        Assert.NotNull(located);
        var parts = located!.Split(' ');
        await session.SendAsync($"touch {parts[2]} {parts[3]}");
        await Expect(session, "KEYBOARD on");

        mark = session.Mark();
        await TapAvatar(session, "Charles Babbage");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("VIEW conversation user=Charles Babbage prompt=\"Password:\"", StringComparison.Ordinal), poke: "where", since: mark));
        await TapAvatar(session, "Ada Lovelace");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("VIEW conversation user=Ada Lovelace prompt=\"Password:\"", StringComparison.Ordinal), poke: "where", since: mark));
        await session.SettleAsync();
        Assert.DoesNotContain(session.Lines().Skip(mark), line => line == "KEYBOARD off");
        Assert.Empty(fixture.Greetd.Requests());

        await session.SendAsync("keyboard-type Hunter2!");
        await Expect(session, "KBTYPE ok");
        await session.SendAsync("keyboard-tap enter");
        await Expect(session, "START session=sway");
        Assert.Equal(new GreetdCreateSession("ada"), fixture.Greetd.Requests()[0]);
        Assert.Equal(new GreetdPostAuthMessageResponse("Hunter2!"), fixture.Greetd.Requests()[1]);
    }

    [Fact]
    public async Task A_tap_on_other_while_the_pad_is_up_asks_for_a_name_without_reaching_greetd()
    {
        using var fixture = new GreeterFixture();
        using var session = fixture.Start();
        await Ready(session);
        await session.SendAsync("select ada");
        await Expect(session, "AUTH secret Password: ");
        var mark = session.Mark();
        await TapAvatar(session, "Other");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("VIEW username ", StringComparison.Ordinal), poke: "where", since: mark));
        Assert.Empty(fixture.Greetd.Requests());
    }

    [Fact]
    public async Task A_login_typed_on_the_on_screen_keyboard_reaches_greetd()
    {
        using var fixture = new GreeterFixture();
        fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
        using var session = fixture.Start();
        await Ready(session);
        await session.SendAsync("select ada");
        await Expect(session, "AUTH secret Password: ");
        var mark = session.Mark();
        var located = await session.WaitForAsync(line => line.StartsWith("LOCATE Password: ", StringComparison.Ordinal) && !line.EndsWith(" none", StringComparison.Ordinal), poke: "locate Password:", since: mark);
        Assert.NotNull(located);
        var parts = located!.Split(' ');
        await session.SendAsync($"touch {parts[2]} {parts[3]}");
        await Expect(session, "KEYBOARD on");
        await session.SendAsync("keyboard-type Hunter2!");
        await Expect(session, "KBTYPE ok");
        await session.SendAsync("keyboard-tap enter");
        await Expect(session, "START session=sway");
        Assert.Equal(new GreetdPostAuthMessageResponse("Hunter2!"), fixture.Greetd.Requests()[1]);
    }

    [Fact]
    public async Task A_tap_off_the_field_hides_the_keyboard_and_only_a_tap_on_it_brings_it_back()
    {
        using var fixture = new GreeterFixture();
        fixture.Greetd.Respond(Secret);
        using var session = fixture.Start();
        await Ready(session);
        await session.SendAsync("select ada");
        await Expect(session, "AUTH secret Password: ");
        var mark = session.Mark();
        var located = await session.WaitForAsync(line => line.StartsWith("LOCATE Password: ", StringComparison.Ordinal) && !line.EndsWith(" none", StringComparison.Ordinal), poke: "locate Password:", since: mark);
        Assert.NotNull(located);
        var field = located!.Split(' ');
        await session.SendAsync($"touch {field[2]} {field[3]}");
        await Expect(session, "KEYBOARD on");

        mark = session.Mark();
        await session.SendAsync("touch 0.05 0.3");
        await Expect(session, "KEYBOARD off", mark);
        mark = session.Mark();
        await session.SendAsync("touch 0.5 0.06");
        await session.SendAsync("touch 0.1 0.4");
        Assert.Null(await session.WaitForAsync(line => line == "KEYBOARD on", since: mark, attempts: 15));

        await session.SendAsync($"touch {field[2]} {field[3]}");
        await Expect(session, "KEYBOARD on", mark);
    }

    [Fact]
    public async Task High_contrast_and_text_size_apply_from_the_menu_and_the_commands()
    {
        using var fixture = new GreeterFixture();
        using var session = fixture.Start();
        await Ready(session);
        await session.SendAsync("menu device");
        await session.SendAsync("menu-choose contrast");
        await Expect(session, "CONTRAST on");
        await session.SendAsync("menu-choose text:150%");
        await Expect(session, "TEXTSCALE 150%");
        var mark = session.Mark();
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("ACCESS contrast=on text=150% surface=683x512@1.5", StringComparison.Ordinal), poke: "where", since: mark));
        await session.SendAsync("contrast off");
        await Expect(session, "CONTRAST off");
        await session.SendAsync("text-scale 1");
        await Expect(session, "TEXTSCALE 100%");
    }

    [Fact]
    public async Task Restart_asks_twice_and_never_reaches_logind_under_lunawm_no_power()
    {
        using var fixture = new GreeterFixture();
        using var session = fixture.Start();
        await Ready(session);
        await session.SendAsync("menu device");
        await session.SendAsync("menu-choose restart");
        await Expect(session, "MENUCHOSE device restart");
        Assert.DoesNotContain(session.Lines(), line => line.StartsWith("POWER ", StringComparison.Ordinal));
        await session.SendAsync("menu-choose restart");
        await Expect(session, "POWER reboot");
        await Expect(session, "MENU device off");
    }

    [Fact]
    public async Task The_status_menus_open_over_the_login_view()
    {
        using var fixture = new GreeterFixture();
        using var session = fixture.Start();
        await Ready(session);
        foreach (var kind in new[] { "battery", "wifi", "bluetooth", "sound" })
        {
            await session.SendAsync($"menu {kind}");
            await Expect(session, $"MENU {kind} on");
        }

        await session.SendAsync("menu sound close");
        await Expect(session, "MENU sound off");
    }

    [Fact]
    public async Task The_login_view_is_on_the_accessibility_bus_and_the_password_is_masked()
    {
        using var bus = await AccessibilityBus.StartAsync();
        using var fixture = new GreeterFixture();
        fixture.Greetd.Respond(Secret);
        using var session = new LunaSession("pixman", LunaSession.Compositor(), fixture.ConfigPath, With(fixture.Environment, bus), fixture.Arguments());
        await Ready(session);
        await Expect(session, "A11Y on");
        using var a11y = await bus.ConnectAsync();

        var tree = await a11y.WhereAsync(nodes => nodes.Any(node => node.Name == "Ada Lovelace"));
        Assert.Contains(tree, node => node.Role == "push button" && node.Name == "Ada Lovelace");
        Assert.Contains(tree, node => node.Role == "push button" && node.Name == "Other");

        await session.SendAsync("select ada");
        await Expect(session, "AUTH secret Password: ");
        await session.SettleAsync();
        await a11y.WhereAsync(nodes => nodes.Any(node => node.Role == "password text" && node.Name == "Password:"));
        await session.SendAsync("type hunter2");
        await session.SettleAsync();
        var texts = await a11y.TextsAsync();
        var after = await a11y.TreeAsync();
        Assert.Contains("●●●●●●●", texts);
        Assert.DoesNotContain(texts, text => text.Contains("hunter2", StringComparison.Ordinal));
        Assert.DoesNotContain(after, node => node.Name.Contains("hunter2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Accessibility_traffic_from_another_thread_never_wedges_the_loop()
    {
        using var bus = await AccessibilityBus.StartAsync();
        using var fixture = new GreeterFixture();
        fixture.Greetd.Respond(Secret);
        using var session = new LunaSession("pixman", LunaSession.Compositor(), fixture.ConfigPath, With(fixture.Environment, bus), fixture.Arguments());
        await Ready(session);
        await Expect(session, "A11Y on");
        using var a11y = await bus.ConnectAsync();
        await a11y.WhereAsync(nodes => nodes.Any(node => node.Name == "Ada Lovelace"));

        var clock = Stopwatch.StartNew();
        for (var round = 0; round < 40; round++)
        {
            var walk = a11y.TreeAsync();
            await session.SendAsync(round % 2 == 0 ? "key 15 1" : "key 15 0");
            var mark = session.Mark();
            Assert.True(
                await session.WaitForAsync(line => line.StartsWith("FOCUS ", StringComparison.Ordinal), poke: "where", since: mark, attempts: 50) is not null,
                $"the greeter stopped answering in round {round}");
            await walk;
        }

        Assert.True(clock.ElapsedMilliseconds < 6000, $"40 rounds took {clock.ElapsedMilliseconds} ms: a wakeup from the accessibility thread did not reach the loop");
    }

    [Fact]
    public async Task The_screen_reader_starts_by_command_and_chord_and_alone_sees_the_globals()
    {
        Assert.SkipWhen(LunaSession.Which("wayland-info") is null, "wayland-info is not installed");
        using var bus = await AccessibilityBus.StartAsync();
        using var fixture = new GreeterFixture();
        var log = Path.Combine(Path.GetDirectoryName(fixture.ConfigPath)!, "reader.log");
        var stub = fixture.Write("reader.sh", $$"""
            #!/bin/sh
            echo "started $$" >> {{log}}
            wayland-info > {{log}}.globals 2>&1
            trap 'echo stopped >> {{log}}; exit 0' TERM
            while :; do sleep 0.1; done
            """);
        File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        fixture.Rewrite($"[accessibility]\nscreen_reader = \"toggle\"\norca = [\"{stub}\"]\n");
        using var session = new LunaSession("pixman", LunaSession.Compositor(), fixture.ConfigPath, With(fixture.Environment, bus), fixture.Arguments());
        var socket = await Ready(session);
        await Expect(session, "A11Y on");

        await session.SendAsync("reader on");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("READER on pid=", StringComparison.Ordinal)));
        var globals = await WaitFor(log + ".globals", "xdg_wm_base");
        Assert.Contains("wl_compositor", globals, StringComparison.Ordinal);

        var outside = new ProcessStartInfo("wayland-info") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        outside.Environment["WAYLAND_DISPLAY"] = socket;
        outside.Environment["XDG_RUNTIME_DIR"] = bus.Runtime;
        using (var probe = Process.Start(outside)!)
        {
            var seen = await probe.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Assert.True(probe.WaitForExit(5000));
            Assert.DoesNotContain("xdg_wm_base", seen, StringComparison.Ordinal);
        }

        foreach (var (code, pressed) in new[] { (LeftMeta, 1), (LeftAlt, 1), (KeyS, 1), (KeyS, 0), (LeftAlt, 0), (LeftMeta, 0) })
        {
            await session.SendAsync($"key {code} {pressed}");
        }

        await Expect(session, "READER off");
        await WaitFor(log, "stopped");
        var mark = session.Mark();
        foreach (var (code, pressed) in new[] { (LeftMeta, 1), (LeftAlt, 1), (KeyS, 1), (KeyS, 0), (LeftAlt, 0), (LeftMeta, 0) })
        {
            await session.SendAsync($"key {code} {pressed}");
        }

        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("READER on pid=", StringComparison.Ordinal), since: mark));
    }

    private static async Task<string> WaitFor(string path, string text)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (File.Exists(path) && File.ReadAllText(path).Contains(text, StringComparison.Ordinal))
            {
                return File.ReadAllText(path);
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"{path} never held '{text}': {(File.Exists(path) ? File.ReadAllText(path) : "missing")}");
        return "";
    }
}
