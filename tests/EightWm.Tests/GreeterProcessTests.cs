using Basin.Greetd;
using BasinGreeter.Tests;
using Xunit;

namespace EightWm.Tests;

public sealed class GreeterProcessTests
{
    private const uint LeftMeta = 125;
    private const uint LeftAlt = 56;
    private const uint KeyS = 31;
    private const uint Space = 57;

    private static GreetdAuthMessage Secret => new(GreetdAuthMessageType.Secret, "Password: ");

    private static async Task<string> Ready(EightSession session)
    {
        Assert.NotNull(await session.WaitForAsync(line => line == "GREETD connected"));
        return (await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)))!.Split(' ')[1];
    }

    private static async Task Expect(EightSession session, string exact, int since = 0) =>
        Assert.True(await session.WaitForAsync(line => line == exact, since: since) is not null, $"never saw [{exact}]");

    private static async Task<string> Where(EightSession session, string prefix)
    {
        var mark = session.Mark();
        var line = await session.WaitForAsync(line => line.StartsWith(prefix, StringComparison.Ordinal), poke: "where", since: mark);
        Assert.NotNull(line);
        return line!;
    }

    private static Dictionary<string, string> With(Dictionary<string, string> environment, AccessibilityBus bus)
    {
        var applied = bus.Apply(environment.ToDictionary(pair => pair.Key, pair => (string?)pair.Value));
        return applied.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value!);
    }

    [Fact]
    public async Task A_key_raises_the_curtain_and_the_first_user_logs_in()
    {
        using var fixture = new EightGreeterFixture();
        fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
        using var session = fixture.Start();
        await Ready(session);
        await Expect(session, "USERS 2");
        await Expect(session, "AUTH secret Password: ");
        Assert.StartsWith("CURTAIN down", await Where(session, "CURTAIN "), StringComparison.Ordinal);
        await session.SendAsync($"key {Space} 1");
        await session.SendAsync($"key {Space} 0");
        await Expect(session, "CURTAIN up key");
        Assert.Contains("user=ada", await Where(session, "VIEW password "), StringComparison.Ordinal);
        await session.SendAsync("type hunter2");
        await session.SendAsync("submit");
        await Expect(session, "START session=sway");
        Assert.True(session.WaitForExit(5000), "the greeter did not exit");
        Assert.Equal(0, session.Process.ExitCode);
        Assert.DoesNotContain(session.Lines(), line => line.Contains("hunter2", StringComparison.Ordinal));
        var requests = fixture.Greetd.Requests();
        Assert.Equal(new GreetdCreateSession("ada"), requests[0]);
        Assert.Equal(new GreetdPostAuthMessageResponse("hunter2"), requests[1]);
        Assert.Contains("last_user = \"ada\"", File.ReadAllText(fixture.StatePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_password_shows_the_windows_8_error_and_asks_again()
    {
        using var fixture = new EightGreeterFixture();
        fixture.Greetd.Respond(Secret, new GreetdError(GreetdErrorType.AuthError, "pam_authenticate: AUTH_ERR"), new GreetdSuccess(), Secret, new GreetdSuccess(), new GreetdSuccess());
        using var session = fixture.Start();
        await Ready(session);
        await Expect(session, "AUTH secret Password: ");
        await session.SendAsync("curtain up");
        await session.SendAsync("type wrong");
        await session.SendAsync("submit");
        await Expect(session, "LOGIN failed auth_error");
        Assert.Contains("error=\"The password is incorrect. Try again.\"", await Where(session, "VIEW password "), StringComparison.Ordinal);
        await session.SendAsync("type right");
        await session.SendAsync("submit");
        await Expect(session, "START session=sway");
        var requests = fixture.Greetd.Requests();
        Assert.IsType<GreetdCancelSession>(requests[2]);
        Assert.Equal(new GreetdPostAuthMessageResponse("right"), requests[4]);
    }

    [Fact]
    public async Task Cancel_shows_every_user_and_a_chosen_session_is_remembered()
    {
        using var fixture = new EightGreeterFixture();
        fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
        using var session = fixture.Start();
        await Ready(session);
        await Expect(session, "AUTH secret Password: ");
        await session.SendAsync("curtain up");
        await session.SendAsync("cancel");
        Assert.StartsWith("VIEW users", await Where(session, "VIEW users"), StringComparison.Ordinal);
        await session.SendAsync("select charles");
        Assert.Contains("user=charles", await Where(session, "VIEW password "), StringComparison.Ordinal);
        await session.SendAsync("options");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("OPTIONS open sessions=2", StringComparison.Ordinal)));
        await session.SendAsync("session tinycomp");
        await session.SendAsync("type pw");
        await session.SendAsync("submit");
        await Expect(session, "START session=tinycomp");
        var memory = File.ReadAllText(fixture.StatePath);
        Assert.Contains("last_user = \"charles\"", memory, StringComparison.Ordinal);
        Assert.Contains("session = \"tinycomp\"", memory, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Demo_mode_logs_in_with_the_demo_password_and_is_refused_under_greetd()
    {
        using var fixture = new EightGreeterFixture();
        using (var demo = new EightSession("pixman", EightSession.Compositor(), fixture.ConfigPath, fixture.Environment, "--greeter", "--demo", "--state", fixture.StatePath))
        {
            await Expect(demo, "GREETD demo");
            Assert.NotNull(await demo.WaitForAsync(line => line == "AUTH secret Password: "));
            await demo.SendAsync("curtain up");
            await demo.SendAsync("type demo");
            await demo.SendAsync("submit");
            Assert.NotNull(await demo.WaitForAsync(line => line.StartsWith("START session=", StringComparison.Ordinal)));
        }

        var environment = fixture.Environment;
        environment["GREETD_SOCK"] = fixture.Greetd.SocketPath;
        using var refused = new EightSession("pixman", EightSession.Compositor(), fixture.ConfigPath, environment, "--greeter", "--demo");
        Assert.True(refused.WaitForExit(10_000), "the refused greeter did not exit");
        Assert.Equal(1, refused.Process.ExitCode);
    }

    [Fact]
    public async Task The_sign_in_view_follows_the_pointer_and_the_curtain_rises_on_every_output()
    {
        using var fixture = new EightGreeterFixture();
        using var session = fixture.Start("pixman", "--outputs", "2");
        await Ready(session);
        Assert.StartsWith("BOX HEADLESS-1 placed=True", await Where(session, "BOX HEADLESS-1 placed"), StringComparison.Ordinal);
        await session.SendAsync("move 2000 300");
        await Expect(session, "BOX HEADLESS-2");
        Assert.Contains("X = 1366", await Where(session, "BOX HEADLESS-2 placed=True"), StringComparison.Ordinal);
        await session.SendAsync("curtain up");
        await Expect(session, "CURTAIN up command");
        await session.SendAsync("move 100 100");
        await Expect(session, "BOX HEADLESS-1");
        Assert.StartsWith("CURTAIN up", await Where(session, "CURTAIN "), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_touch_swipe_raises_the_curtain_and_the_touch_keyboard_types_the_password()
    {
        using var fixture = new EightGreeterFixture();
        fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
        using var session = fixture.Start();
        await Ready(session);
        await Expect(session, "AUTH secret Password: ");
        await session.SendAsync("touchdrag 0.5 0.8 0 -0.5 300");
        await Expect(session, "CURTAIN up swipe");
        var mark = session.Mark();
        var located = await session.WaitForAsync(line => line.StartsWith("LOCATE Password ", StringComparison.Ordinal) && !line.EndsWith(" none", StringComparison.Ordinal), poke: "locate Password", since: mark);
        Assert.NotNull(located);
        var parts = located!.Split(' ');
        await session.SendAsync($"touch {parts[2]} {parts[3]}");
        await Expect(session, "KEYBOARD on");
        await session.SendAsync("keyboard-type hunter2");
        mark = session.Mark();
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("KEYAT return ", StringComparison.Ordinal) && !line.EndsWith(" none", StringComparison.Ordinal), poke: "keyboard-locate return", since: mark));
        await session.SendAsync("keyboard-tap return");
        await Expect(session, "START session=sway");
        Assert.Equal(new GreetdPostAuthMessageResponse("hunter2"), fixture.Greetd.Requests()[1]);
    }

    private static async Task TouchAt(EightSession session, string command, string reply)
    {
        var mark = session.Mark();
        var located = await session.WaitForAsync(line => line.StartsWith(reply, StringComparison.Ordinal) && !line.EndsWith(" none", StringComparison.Ordinal), poke: command, since: mark);
        Assert.NotNull(located);
        var parts = located!.Split(' ');
        await session.SendAsync($"touch {parts[2]} {parts[3]}");
    }

    [Fact]
    public async Task A_tap_off_the_password_box_hides_the_keyboard_and_only_a_tap_on_it_brings_it_back()
    {
        using var fixture = new EightGreeterFixture();
        fixture.Greetd.Respond(Secret);
        using var session = fixture.Start();
        await Ready(session);
        await Expect(session, "AUTH secret Password: ");
        await session.SendAsync("touchdrag 0.5 0.8 0 -0.5 300");
        await Expect(session, "CURTAIN up swipe");
        await TouchAt(session, "locate Password", "LOCATE Password ");
        await Expect(session, "KEYBOARD on");

        var mark = session.Mark();
        await TouchAt(session, "keyboard-locate a", "KEYAT a ");
        Assert.Null(await session.WaitForAsync(line => line == "KEYBOARD off", since: mark, attempts: 15));

        await session.SendAsync("touch 0.5 0.08");
        await Expect(session, "KEYBOARD off", mark);
        mark = session.Mark();
        await session.SendAsync("touch 0.5 0.08");
        await session.SendAsync("touch 0.2 0.5");
        Assert.Null(await session.WaitForAsync(line => line == "KEYBOARD on", since: mark, attempts: 15));

        await TouchAt(session, "locate Password", "LOCATE Password ");
        await Expect(session, "KEYBOARD on", mark);
        mark = session.Mark();
        await TouchAt(session, "keyboard-locate hide", "KEYAT hide ");
        await Expect(session, "KEYBOARD off", mark);
        mark = session.Mark();
        await session.SendAsync("touch 0.5 0.08");
        Assert.Null(await session.WaitForAsync(line => line == "KEYBOARD on", since: mark, attempts: 15));
    }

    [Fact]
    public async Task High_contrast_text_size_and_reveal_answer_their_commands()
    {
        using var fixture = new EightGreeterFixture();
        fixture.Greetd.Respond(Secret);
        using var session = fixture.Start();
        await Ready(session);
        await session.SendAsync("contrast on");
        await Expect(session, "CONTRAST on");
        await session.SendAsync("text-scale 1.5");
        await Expect(session, "TEXTSCALE 1.5");
        Assert.Contains("contrast=on text=1.5", await Where(session, "ACCESS "), StringComparison.Ordinal);
        await session.SendAsync("reveal on");
        await Expect(session, "REVEAL on allowed=yes");
        await session.SendAsync("ease open");
        await Expect(session, "EASE open");
        await session.SendAsync("power open");
        await Expect(session, "POWERMENU open");
        await session.SendAsync("power restart");
        await Expect(session, "POWER restart skipped");
    }

    [Fact]
    public async Task The_password_box_is_masked_on_the_accessibility_bus_even_while_revealed()
    {
        using var bus = await AccessibilityBus.StartAsync();
        using var fixture = new EightGreeterFixture();
        fixture.Greetd.Respond(Secret);
        using var session = new EightSession("pixman", EightSession.Compositor(), fixture.ConfigPath, With(fixture.Environment, bus), fixture.Arguments());
        await Ready(session);
        await Expect(session, "A11Y on");
        using var a11y = await bus.ConnectAsync();
        await Expect(session, "AUTH secret Password: ");
        await session.SendAsync("curtain up");
        await session.SettleAsync();
        await AtSpiTree.Where(a11y, nodes => nodes.Any(node => node.Role == "entry" && node.Name == "Password"));
        await session.SendAsync("type hunter2");
        await session.SendAsync("reveal on");
        await Expect(session, "REVEAL on allowed=yes");
        await session.SettleAsync();
        var texts = await AtSpiTree.Texts(a11y);
        Assert.Contains("●●●●●●●", texts);
        Assert.DoesNotContain(texts, text => text.Contains("hunter2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_reader_chord_starts_narrator_and_turns_the_reveal_off()
    {
        using var bus = await AccessibilityBus.StartAsync();
        using var fixture = new EightGreeterFixture();
        var stub = fixture.Write("reader.sh", "#!/bin/sh\ntrap 'exit 0' TERM\nwhile :; do sleep 0.1; done\n");
        File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        fixture.Rewrite($"[accessibility]\nscreen_reader = \"toggle\"\norca = [\"{stub}\"]\n");
        using var session = new EightSession("pixman", EightSession.Compositor(), fixture.ConfigPath, With(fixture.Environment, bus), fixture.Arguments());
        await Ready(session);
        await Expect(session, "A11Y on");
        foreach (var (code, pressed) in new[] { (LeftMeta, 1), (LeftAlt, 1), (KeyS, 1), (KeyS, 0), (LeftAlt, 0), (LeftMeta, 0) })
        {
            await session.SendAsync($"key {code} {pressed}");
        }

        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("READER on pid=", StringComparison.Ordinal)));
        Assert.Contains("reveal=off", await Where(session, "ACCESS "), StringComparison.Ordinal);
        await session.SendAsync("reveal on");
        await Expect(session, "REVEAL off allowed=no");
        Assert.StartsWith("CURTAIN down", await Where(session, "CURTAIN "), StringComparison.Ordinal);
    }
}
