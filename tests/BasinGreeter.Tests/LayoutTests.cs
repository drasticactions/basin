using Basin.Greetd;
using Xunit;

namespace BasinGreeter.Tests;

public sealed class LayoutTests
{
    private static GreetdAuthMessage Secret => new(GreetdAuthMessageType.Secret, "Password: ");

    private const string Layouts = "[keyboard]\nlayouts = [\"us\", \"de(neo)\", \"de\"]\n";

    [Fact]
    public async Task The_picked_layout_types_the_password_and_reaches_the_session_environment()
    {
        using var fixture = new Fixture(Layouts);
        fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
        using var greeter = fixture.Start();
        await greeter.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");
        await greeter.SendAsync("layout de");
        await greeter.ExpectAsync("LAYOUT de");
        await greeter.SendAsync("select ada");
        await greeter.ExpectAsync("AUTH secret Password: ");
        await greeter.SettleAsync();
        await greeter.SendAsync("key 44 1");
        await greeter.SendAsync("key 44 0");
        await greeter.SettleAsync();
        await greeter.SendAsync("submit");
        await greeter.ExpectAsync("START session=sway");
        var requests = fixture.Greetd.Requests();
        Assert.Equal(new GreetdPostAuthMessageResponse("y"), requests[1]);
        var start = Assert.IsType<GreetdStartSession>(requests[2]);
        Assert.Contains("XKB_DEFAULT_LAYOUT=de", start.Env);
        Assert.DoesNotContain(start.Env, pair => pair.StartsWith("XKB_DEFAULT_VARIANT=", StringComparison.Ordinal));
        Assert.Contains("XKB_DEFAULT_LAYOUT=de", start.Cmd);
        Assert.Contains("layout = \"de\"", File.ReadAllText(fixture.StatePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_variant_reaches_the_environment_and_the_users_layout_comes_back()
    {
        using var fixture = new Fixture(Layouts);
        fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
        using (var greeter = fixture.Start())
        {
            await greeter.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");
            await greeter.SendAsync("layout de(neo)");
            await greeter.ExpectAsync("LAYOUT de(neo)");
            await greeter.SendAsync("select charles");
            await greeter.ExpectAsync("AUTH secret Password: ");
            await greeter.SendAsync("type pw");
            await greeter.SendAsync("submit");
            await greeter.ExpectAsync("START session=sway");
            var start = Assert.IsType<GreetdStartSession>(fixture.Greetd.Requests()[2]);
            Assert.Contains("XKB_DEFAULT_LAYOUT=de", start.Env);
            Assert.Contains("XKB_DEFAULT_VARIANT=neo", start.Env);
            Assert.True(greeter.WaitForExit(5000));
        }

        using var again = fixture.Start();
        await again.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");
        var mark = again.Mark();
        await again.ExpectAsync(line => line == "KEYMAP layout=de(neo)", "charles's layout preselected", poke: "where", since: mark);
    }

    [Fact]
    public async Task Without_layouts_there_is_no_picker_and_no_xkb_environment_and_a_bad_name_is_refused()
    {
        using (var fixture = new Fixture())
        {
            fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
            using var greeter = fixture.Start();
            await greeter.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");
            await greeter.SendAsync("select ada");
            await greeter.ExpectAsync("AUTH secret Password: ");
            await greeter.SendAsync("type pw");
            await greeter.SendAsync("submit");
            await greeter.ExpectAsync("START session=sway");
            var start = Assert.IsType<GreetdStartSession>(fixture.Greetd.Requests()[2]);
            Assert.DoesNotContain(start.Env, pair => pair.StartsWith("XKB_", StringComparison.Ordinal));
            Assert.DoesNotContain(greeter.Lines(), line => line.StartsWith("LAYOUT ", StringComparison.Ordinal));
        }

        using var bad = new Fixture("[keyboard]\nlayouts = [\"no-such-layout\"]\n");
        using var refused = bad.Start();
        await refused.ExpectAsync("LAYOUT no-such-layout failed");
    }
}
