using Basin.Greetd;
using Xunit;

namespace LunaWm.Tests;

public sealed class GreeterTests
{
    private static GreetdAuthMessage Secret => new(GreetdAuthMessageType.Secret, "Password: ");

    private static async Task Ready(LunaSession session)
    {
        Assert.NotNull(await session.WaitForAsync(line => line == "GREETD connected"));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_user_from_the_strip_logs_in_and_the_greeter_exits()
    {
        using var fixture = new GreeterFixture();
        fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
        using var session = fixture.Start();
        await Ready(session);
        Assert.NotNull(await session.WaitForAsync(line => line == "USERS 2"));
        await session.SendAsync("users");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("USER ada uid=1000 name=\"Ada Lovelace\"", StringComparison.Ordinal)));
        await session.SendAsync("select ada");
        Assert.NotNull(await session.WaitForAsync(line => line == "AUTH secret Password: "));
        await session.SendAsync("type hunter2");
        await session.SendAsync("submit");
        Assert.NotNull(await session.WaitForAsync(line => line == "START session=sway"));
        Assert.True(session.WaitForExit(5000), "the greeter did not exit");
        Assert.Equal(0, session.Process.ExitCode);
        Assert.DoesNotContain(session.Lines(), line => line.Contains("hunter2", StringComparison.Ordinal));

        var requests = fixture.Greetd.Requests();
        Assert.Equal(new GreetdCreateSession("ada"), requests[0]);
        Assert.Equal(new GreetdPostAuthMessageResponse("hunter2"), requests[1]);
        var start = Assert.IsType<GreetdStartSession>(requests[2]);
        Assert.Contains("sway", start.Cmd);
        var memory = File.ReadAllText(fixture.StatePath);
        Assert.Contains("last_user = \"ada\"", memory, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_password_shakes_the_pad_and_asks_again()
    {
        using var fixture = new GreeterFixture();
        fixture.Greetd.Respond(Secret, new GreetdError(GreetdErrorType.AuthError, "pam_authenticate: AUTH_ERR"), new GreetdSuccess(), Secret, new GreetdSuccess(), new GreetdSuccess());
        using var session = fixture.Start();
        await Ready(session);
        await session.SendAsync("select charles");
        Assert.NotNull(await session.WaitForAsync(line => line == "AUTH secret Password: "));
        await session.SendAsync("type wrong");
        await session.SendAsync("submit");
        Assert.NotNull(await session.WaitForAsync(line => line == "LOGIN failed auth_error"));
        var mark = session.Mark();
        Assert.NotNull(await session.WaitForAsync(
            line => line.StartsWith("VIEW conversation user=Charles Babbage prompt=\"Password:\" error=\"Your username or password is incorrect. Try again.\" secret=yes", StringComparison.Ordinal),
            poke: "where", since: mark));
        await session.SendAsync("type right");
        await session.SendAsync("submit");
        Assert.NotNull(await session.WaitForAsync(line => line == "START session=sway"));
        var requests = fixture.Greetd.Requests();
        Assert.IsType<GreetdCancelSession>(requests[2]);
        Assert.Equal(new GreetdCreateSession("charles"), requests[3]);
        Assert.Equal(new GreetdPostAuthMessageResponse("right"), requests[4]);
    }

    [Fact]
    public async Task Other_asks_for_a_name_and_a_visible_prompt_uses_the_plain_field()
    {
        using var fixture = new GreeterFixture();
        fixture.Greetd.Respond(new GreetdAuthMessage(GreetdAuthMessageType.Visible, "Code:"));
        using var session = fixture.Start();
        await Ready(session);
        await session.SendAsync("other grace");
        Assert.NotNull(await session.WaitForAsync(line => line == "AUTH secret Password: "));
        await session.SendAsync("answer pw");
        Assert.NotNull(await session.WaitForAsync(line => line == "AUTH visible Code:"));
        var mark = session.Mark();
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("VIEW conversation user=grace prompt=\"Code:\"", StringComparison.Ordinal) && line.EndsWith("secret=no", StringComparison.Ordinal), poke: "where", since: mark));
        Assert.Equal(new GreetdCreateSession("grace"), fixture.Greetd.Requests()[0]);
    }

    [Fact]
    public async Task The_remembered_user_and_session_are_selected()
    {
        using var fixture = new GreeterFixture();
        fixture.Write("state/state.toml", "last_user = \"charles\"\n\n[users.charles]\nsession = \"tinycomp\"\n");
        using var session = fixture.Start();
        await Ready(session);
        var mark = session.Mark();
        Assert.NotNull(await session.WaitForAsync(line => line == "SELECTED session=tinycomp user=charles", poke: "where", since: mark));
    }

    [Fact]
    public async Task Demo_mode_logs_in_with_the_demo_password_and_is_refused_under_greetd()
    {
        using var fixture = new GreeterFixture();
        using (var demo = new LunaSession("pixman", LunaSession.Compositor(), fixture.ConfigPath, fixture.Environment, "--greeter", "--demo", "--state", fixture.StatePath))
        {
            Assert.NotNull(await demo.WaitForAsync(line => line == "GREETD demo"));
            Assert.NotNull(await demo.WaitForAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal)));
            await demo.SendAsync("select ada");
            Assert.NotNull(await demo.WaitForAsync(line => line == "AUTH secret Password: "));
            await demo.SendAsync("answer demo");
            Assert.NotNull(await demo.WaitForAsync(line => line.StartsWith("START session=", StringComparison.Ordinal)));
        }

        var environment = fixture.Environment;
        environment["GREETD_SOCK"] = fixture.Greetd.SocketPath;
        using var refused = new LunaSession("pixman", LunaSession.Compositor(), fixture.ConfigPath, environment, "--greeter", "--demo");
        Assert.True(refused.WaitForExit(10_000), "the refused greeter did not exit");
        Assert.Equal(1, refused.Process.ExitCode);
    }

    [Fact]
    public async Task The_login_view_follows_the_pointer_to_another_output()
    {
        using var fixture = new GreeterFixture();
        using var session = fixture.Start("pixman", "--outputs", "2");
        await Ready(session);
        var mark = session.Mark();
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("BOX HEADLESS-1 placed=True", StringComparison.Ordinal), poke: "where", since: mark));
        await session.SendAsync("move 1500 300");
        Assert.NotNull(await session.WaitForAsync(line => line == "BOX HEADLESS-2"));
        mark = session.Mark();
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("BOX HEADLESS-2 placed=True bounds=Box { X = 1024", StringComparison.Ordinal), poke: "where", since: mark));
        await session.SendAsync("move 100 100");
        Assert.NotNull(await session.WaitForAsync(line => line == "BOX HEADLESS-1", since: mark));
    }

    [Fact]
    public async Task Hiding_the_user_list_asks_for_a_name()
    {
        using var fixture = new GreeterFixture("[users]\nlist = false\n\n[look]\nclock = \"24\"\nmystery = 1\n");
        using var session = fixture.Start();
        await Ready(session);
        var mark = session.Mark();
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("VIEW username", StringComparison.Ordinal), poke: "where", since: mark));
    }
}
