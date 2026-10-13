using System.Diagnostics;
using Basin.Greetd;
using Xunit;

namespace BasinGreeter.Tests;

public sealed class ProcessTests
{
    private static GreetdAuthMessage Secret => new(GreetdAuthMessageType.Secret, "Password: ");

    private static async Task Ready(GreeterSession session)
    {
        await session.ExpectAsync("GREETD connected");
        await session.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");
    }

    [Fact]
    public async Task A_listed_user_logs_in_and_the_greeter_exits_within_a_second_of_the_reply()
    {
        using var fixture = new Fixture();
        fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
        using var session = fixture.Start();
        await Ready(session);
        await session.ExpectAsync("USERS 2");
        await session.ExpectAsync("SESSIONS 3");
        await session.SendAsync("select ada");
        await session.ExpectAsync("AUTH secret Password: ");
        await session.SendAsync("type hunter2");
        await session.SendAsync("submit");
        await session.ExpectAsync("LOGIN ok");
        await session.ExpectAsync("START session=sway");
        var started = Stopwatch.StartNew();
        Assert.True(session.WaitForExit(5000), "the greeter did not exit");
        Assert.True(started.ElapsedMilliseconds < 1000, $"the greeter took {started.ElapsedMilliseconds} ms to exit");
        Assert.Equal(0, session.Process.ExitCode);
        Assert.Contains("EXIT start", session.Lines());
        Assert.DoesNotContain(session.Lines().Concat(session.Errors), line => line.Contains("hunter2", StringComparison.Ordinal));

        var requests = fixture.Greetd.Requests();
        Assert.Equal(new GreetdCreateSession("ada"), requests[0]);
        Assert.Equal(new GreetdPostAuthMessageResponse("hunter2"), requests[1]);
        var start = Assert.IsType<GreetdStartSession>(requests[2]);
        Assert.Equal(["/usr/bin/env", "XDG_SESSION_TYPE=wayland", "XDG_CURRENT_DESKTOP=sway:wlroots", "XDG_SESSION_DESKTOP=sway", "sway"], start.Cmd);
        Assert.Equal(["XDG_SESSION_TYPE=wayland", "XDG_CURRENT_DESKTOP=sway:wlroots", "XDG_SESSION_DESKTOP=sway"], start.Env);
        var frames = session.Lines().Single(line => line.StartsWith("FRAMES ", StringComparison.Ordinal)).Split(' ');
        if (frames[3] != "untracked")
        {
            Assert.Equal("0", frames[3]);
        }
    }

    [Fact]
    public async Task A_wrong_password_says_so_cancels_and_asks_again()
    {
        using var fixture = new Fixture();
        fixture.Greetd.Respond(Secret, new GreetdError(GreetdErrorType.AuthError, "pam_authenticate: AUTH_ERR"), new GreetdSuccess(), Secret, new GreetdSuccess(), new GreetdSuccess());
        using var session = fixture.Start();
        await Ready(session);
        await session.SendAsync("select charles");
        await session.ExpectAsync("AUTH secret Password: ");
        await session.SendAsync("type wrong");
        await session.SendAsync("submit");
        await session.ExpectAsync("LOGIN failed auth_error");
        var mark = session.Mark();
        await session.ExpectAsync(line => line.StartsWith("VIEW conversation user=Charles Babbage prompt=\"Password:\" error=\"Login incorrect\"", StringComparison.Ordinal), "the error under a fresh prompt", poke: "where", since: mark);
        await session.SendAsync("type right");
        await session.SendAsync("submit");
        await session.ExpectAsync("START session=sway");
        var requests = fixture.Greetd.Requests();
        Assert.IsType<GreetdCancelSession>(requests[2]);
        Assert.Equal(new GreetdCreateSession("charles"), requests[3]);
        Assert.Equal(new GreetdPostAuthMessageResponse("right"), requests[4]);
    }

    [Fact]
    public async Task A_one_time_code_comes_as_visible_then_secret_with_info_acknowledged()
    {
        using var fixture = new Fixture();
        fixture.Greetd.Respond(
            new GreetdAuthMessage(GreetdAuthMessageType.Info, "Use your authenticator"),
            new GreetdAuthMessage(GreetdAuthMessageType.Visible, "Verification code: "),
            Secret,
            new GreetdSuccess(),
            new GreetdSuccess());
        using var session = fixture.Start();
        await Ready(session);
        await session.SendAsync("other grace");
        await session.ExpectAsync("AUTH info Use your authenticator");
        await session.ExpectAsync("AUTH visible Verification code: ");
        await session.SendAsync("type 123456");
        await session.SendAsync("submit");
        await session.ExpectAsync("AUTH secret Password: ");
        await session.SendAsync("type pw");
        await session.SendAsync("submit");
        await session.ExpectAsync("START session=sway");
        var requests = fixture.Greetd.Requests();
        Assert.Equal(new GreetdCreateSession("grace"), requests[0]);
        Assert.Equal(new GreetdPostAuthMessageResponse(null), requests[1]);
        Assert.Equal(new GreetdPostAuthMessageResponse("123456"), requests[2]);
        Assert.Equal(new GreetdPostAuthMessageResponse("pw"), requests[3]);
    }

    [Fact]
    public async Task Cancel_sends_cancel_session_and_returns_to_the_list()
    {
        using var fixture = new Fixture();
        fixture.Greetd.Respond(Secret, new GreetdSuccess());
        using var session = fixture.Start();
        await Ready(session);
        await session.SendAsync("select ada");
        await session.ExpectAsync("AUTH secret Password: ");
        await session.SendAsync("cancel");
        var mark = session.Mark();
        await session.ExpectAsync(line => line.StartsWith("VIEW users ", StringComparison.Ordinal), "the user list", poke: "where", since: mark);
        await session.ExpectAsync(line => line == "LOGIN idle", "an idle login", poke: "where", since: mark);
        Assert.IsType<GreetdCancelSession>(fixture.Greetd.Requests()[^1]);
    }

    [Fact]
    public async Task The_chosen_session_reaches_start_session_and_x11_runs_under_the_wrapper()
    {
        using var fixture = new Fixture(sessions: "x11_wrapper = [\"startx\", \"/usr/bin/env\"]");
        fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
        using var session = fixture.Start();
        await Ready(session);
        await session.SendAsync("session i3");
        await session.SendAsync("select ada");
        await session.ExpectAsync("AUTH secret Password: ");
        await session.SendAsync("type pw");
        await session.SendAsync("submit");
        await session.ExpectAsync("START session=i3");
        var start = Assert.IsType<GreetdStartSession>(fixture.Greetd.Requests()[2]);
        Assert.Equal(["startx", "/usr/bin/env", "/usr/bin/env", "XDG_SESSION_TYPE=x11", "XDG_CURRENT_DESKTOP=i3", "XDG_SESSION_DESKTOP=i3", "i3"], start.Cmd);
    }

    [Fact]
    public async Task A_typed_command_is_used_only_when_the_config_allows_it()
    {
        foreach (var allowed in new[] { false, true })
        {
            using var fixture = new Fixture(sessions: $"allow_command = {(allowed ? "true" : "false")}");
            fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
            using var session = fixture.Start();
            await Ready(session);
            await session.SendAsync("command foot --server");
            await session.SendAsync("select ada");
            await session.ExpectAsync("AUTH secret Password: ");
            await session.SendAsync("type pw");
            await session.SendAsync("submit");
            await session.ExpectAsync(allowed ? "START session=command" : "START session=sway");
            var start = Assert.IsType<GreetdStartSession>(fixture.Greetd.Requests()[2]);
            if (allowed)
            {
                Assert.Equal(["foot --server"], start.Cmd);
            }
            else
            {
                Assert.Equal("sway", start.Cmd[^1]);
            }
        }
    }

    [Fact]
    public async Task The_environments_file_and_the_default_session_key_are_read()
    {
        using var fixture = new Fixture(sessions: "default = \"fish -l\"");
        fixture.Write("environments", "# shells\nfish -l\n");
        fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
        using var session = fixture.Start();
        await Ready(session);
        await session.ExpectAsync("SESSIONS 4");
        await session.SendAsync("select ada");
        await session.ExpectAsync("AUTH secret Password: ");
        await session.SendAsync("type pw");
        await session.SendAsync("submit");
        await session.ExpectAsync("START session=fish -l");
        Assert.Equal(["fish -l"], Assert.IsType<GreetdStartSession>(fixture.Greetd.Requests()[2]).Cmd);
    }

    [Fact]
    public async Task A_greetd_that_closes_the_socket_ends_the_greeter_with_status_one()
    {
        using var fixture = new Fixture();
        fixture.Greetd.Respond(Secret, null);
        using var session = fixture.Start();
        await Ready(session);
        await session.SendAsync("select ada");
        await session.ExpectAsync("AUTH secret Password: ");
        await session.SendAsync("type pw");
        await session.SendAsync("submit");
        await session.ExpectAsync("EXIT greetd-closed");
        Assert.True(session.WaitForExit(5000));
        Assert.Equal(1, session.Process.ExitCode);
    }

    [Fact]
    public async Task Demo_mode_is_refused_under_greetd_and_logs_in_with_demo_otherwise()
    {
        using var fixture = new Fixture();
        var environment = fixture.Environment;
        environment["GREETD_SOCK"] = fixture.Greetd.SocketPath;
        using (var refused = new GreeterSession(GreeterSession.Headless("pixman", fixture.ConfigPath, "--demo"), environment))
        {
            Assert.True(refused.WaitForExit(10000));
            Assert.Equal(1, refused.Process.ExitCode);
            Assert.Contains(refused.Errors, line => line.Contains("--demo is refused under greetd", StringComparison.Ordinal));
        }

        using var demo = new GreeterSession(GreeterSession.Headless("pixman", fixture.ConfigPath, "--demo"), fixture.Environment);
        await demo.ExpectAsync("GREETD demo");
        await demo.SendAsync("other anyone");
        await demo.ExpectAsync("AUTH info Demo mode");
        await demo.ExpectAsync("AUTH secret Password:");
        await demo.SendAsync("type demo");
        await demo.SendAsync("submit");
        await demo.ExpectAsync("START session=sway");
        Assert.True(demo.WaitForExit(5000));
        Assert.Equal(0, demo.Process.ExitCode);
    }

    [Fact]
    public async Task Without_greetd_and_without_demo_the_greeter_says_why_and_stops()
    {
        using var fixture = new Fixture();
        using var session = new GreeterSession(GreeterSession.Headless("pixman", fixture.ConfigPath), fixture.Environment);
        Assert.True(session.WaitForExit(10000));
        Assert.Equal(1, session.Process.ExitCode);
        Assert.Contains(session.Errors, line => line.Contains("not under greetd", StringComparison.Ordinal));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task With_two_outputs_the_box_follows_the_pointer()
    {
        using var fixture = new Fixture();
        using var session = fixture.Start("pixman", "--outputs", "2");
        await Ready(session);
        var mark = session.Mark();
        await session.ExpectAsync(line => line.StartsWith("BOX HEADLESS-1 placed=True", StringComparison.Ordinal), "the box on the first output", poke: "where", since: mark);
        await session.SendAsync("move 1900 300");
        await session.ExpectAsync("BOX HEADLESS-2");
        mark = session.Mark();
        await session.ExpectAsync(line => line.StartsWith("BOX HEADLESS-2 placed=True bounds=Box { X = 1280,", StringComparison.Ordinal), "the box on the second output", poke: "where", since: mark);
        await session.ExpectAsync(line => line == "FOCUS box", "keyboard focus on the box", poke: "where", since: mark);
        await session.SendAsync("move 100 100");
        await session.ExpectAsync("BOX HEADLESS-1");
    }

    [Fact]
    public async Task The_control_socket_is_bound_only_when_asked()
    {
        using var fixture = new Fixture();
        using (var off = fixture.Start())
        {
            await Ready(off);
            Assert.DoesNotContain(off.Lines(), line => line.StartsWith("IPC ", StringComparison.Ordinal));
        }

        var path = Path.Combine(Path.GetTempPath(), $"basin-greeter-ipc-{Environment.ProcessId}.sock");
        using var on = fixture.Start("pixman", "--ipc", path);
        await Ready(on);
        await on.ExpectAsync(line => line == $"IPC {path}", "the IPC line");
        Assert.True(File.Exists(path));
    }
}
