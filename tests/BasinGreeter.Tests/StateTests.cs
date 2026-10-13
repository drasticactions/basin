using Basin.Greetd;
using Xunit;

namespace BasinGreeter.Tests;

public sealed class StateTests
{
    private static GreetdAuthMessage Secret => new(GreetdAuthMessageType.Secret, "Password: ");

    private static async Task LogIn(Fixture fixture, string user, string? session)
    {
        fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
        using var greeter = fixture.Start();
        await greeter.ExpectAsync("GREETD connected");
        await greeter.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");
        if (session is not null)
        {
            await greeter.SendAsync($"session {session}");
        }

        await greeter.SendAsync($"select {user}");
        await greeter.ExpectAsync("AUTH secret Password: ");
        await greeter.SendAsync("type pw");
        await greeter.SendAsync("submit");
        await greeter.ExpectAsync(line => line.StartsWith("START ", StringComparison.Ordinal), "START");
        Assert.True(greeter.WaitForExit(5000));
    }

    [Fact]
    public async Task The_last_user_and_each_users_session_are_remembered_across_starts()
    {
        using var fixture = new Fixture();
        await LogIn(fixture, "charles", "i3");
        var text = File.ReadAllText(fixture.StatePath);
        Assert.Contains("last_user = \"charles\"", text, StringComparison.Ordinal);
        Assert.Contains("[users.charles]", text, StringComparison.Ordinal);
        Assert.Contains("session = \"i3\"", text, StringComparison.Ordinal);

        await LogIn(fixture, "ada", "tinycomp");
        text = File.ReadAllText(fixture.StatePath);
        Assert.Contains("last_user = \"ada\"", text, StringComparison.Ordinal);
        Assert.Contains("session = \"i3\"", text, StringComparison.Ordinal);
        Assert.Contains("session = \"tinycomp\"", text, StringComparison.Ordinal);

        using var greeter = fixture.Start();
        await greeter.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");
        var mark = greeter.Mark();
        await greeter.ExpectAsync(line => line == "SELECTED session=tinycomp user=ada", "ada and her session preselected", poke: "where", since: mark);
        fixture.Greetd.Respond(Secret);
        await greeter.SendAsync("select charles");
        await greeter.ExpectAsync("AUTH secret Password: ");
        mark = greeter.Mark();
        await greeter.ExpectAsync(line => line.StartsWith("SELECTED session=i3 ", StringComparison.Ordinal), "charles's own session", poke: "where", since: mark);
    }

    [Fact]
    public async Task A_session_picked_before_the_user_is_kept_over_the_remembered_one()
    {
        using var fixture = new Fixture();
        await LogIn(fixture, "charles", "i3");
        fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
        using var greeter = fixture.Start();
        await greeter.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");
        await greeter.SendAsync("session tinycomp");
        await greeter.SendAsync("select charles");
        await greeter.ExpectAsync("AUTH secret Password: ");
        await greeter.SendAsync("type pw");
        await greeter.SendAsync("submit");
        await greeter.ExpectAsync("START session=tinycomp");
    }

    [Fact]
    public async Task An_unwritable_state_directory_logs_once_and_the_login_still_works()
    {
        using var fixture = new Fixture();
        var state = Path.GetDirectoryName(fixture.StatePath)!;
        Directory.CreateDirectory(state);
        File.SetUnixFileMode(state, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
            using var greeter = fixture.Start();
            await greeter.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");
            await greeter.SendAsync("select ada");
            await greeter.ExpectAsync("AUTH secret Password: ");
            await greeter.SendAsync("type pw");
            await greeter.SendAsync("submit");
            await greeter.ExpectAsync("START session=sway");
            Assert.True(greeter.WaitForExit(5000));
            Assert.Equal(0, greeter.Process.ExitCode);
            Assert.Single(greeter.Errors, line => line.Contains("runs without memory", StringComparison.Ordinal));
        }
        finally
        {
            File.SetUnixFileMode(state, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task Sighup_rereads_the_config_and_names_keys_that_need_a_restart()
    {
        using var fixture = new Fixture();
        using var greeter = fixture.Start();
        await greeter.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");
        await greeter.ExpectAsync("USERS 2");

        fixture.Rewrite("[users]\nhide = [\"charles\"]\n", "allow_command = true");
        var mark = greeter.Mark();
        Signal(greeter);
        await greeter.ExpectAsync(line => line == "RELOAD ok", "RELOAD ok", since: mark);
        await greeter.ExpectAsync(line => line == "USERS 1", "the hidden user gone", since: mark);

        fixture.Rewrite("[render]\nrenderer = \"gl\"\n");
        mark = greeter.Mark();
        await greeter.SendAsync("reload");
        await greeter.ExpectAsync(line => line == "RELOAD restart-needed render.renderer", "the restart key", since: mark);

        File.WriteAllText(fixture.ConfigPath, "[look\n");
        mark = greeter.Mark();
        Signal(greeter);
        await greeter.ExpectAsync(line => line == "RELOAD failed", "RELOAD failed", since: mark);
    }

    private static void Signal(GreeterSession greeter)
    {
        using var kill = System.Diagnostics.Process.Start("kill", ["-HUP", greeter.Process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        kill.WaitForExit();
    }
}
