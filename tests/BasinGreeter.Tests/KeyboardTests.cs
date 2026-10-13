using Basin.Greetd;
using Xunit;

namespace BasinGreeter.Tests;

public sealed class KeyboardTests
{
    private static GreetdAuthMessage Secret => new(GreetdAuthMessageType.Secret, "Password: ");

    private static Task Ready(GreeterSession greeter) =>
        greeter.ExpectAsync(line => line.StartsWith("SOCKET ", StringComparison.Ordinal), "SOCKET");

    [Fact]
    public async Task The_on_screen_keyboard_types_a_password_with_capitals_digits_and_symbols()
    {
        using var fixture = new Fixture();
        fixture.Greetd.Respond(Secret, new GreetdSuccess(), new GreetdSuccess());
        using var greeter = fixture.Start();
        await Ready(greeter);
        await greeter.SendAsync("keyboard show");
        await greeter.ExpectAsync("KEYBOARD on");
        await greeter.SendAsync("select ada");
        await greeter.ExpectAsync("AUTH secret Password: ");
        await greeter.SettleAsync();
        await greeter.SendAsync("keyboard-type Hunter 2!");
        await greeter.ExpectAsync("KBTYPE ok");
        await greeter.SendAsync("keyboard-tap enter");
        await greeter.ExpectAsync("START session=sway");
        Assert.Equal(new GreetdPostAuthMessageResponse("Hunter 2!"), fixture.Greetd.Requests()[1]);
    }

    [Fact]
    public async Task A_touch_raises_the_keyboard_for_the_next_field_and_the_hide_key_lowers_it()
    {
        using var fixture = new Fixture();
        fixture.Greetd.Respond(Secret);
        using var greeter = fixture.Start();
        await Ready(greeter);
        await greeter.SendAsync("ipc input/touch {\"kind\":\"down\",\"id\":1,\"x\":20,\"y\":20}");
        await greeter.SendAsync("ipc input/touch {\"kind\":\"up\",\"id\":1}");
        await greeter.SendAsync("ipc input/touch {\"kind\":\"frame\"}");
        await greeter.SettleAsync();
        Assert.DoesNotContain("KEYBOARD on", greeter.Lines());
        await greeter.SendAsync("select ada");
        await greeter.ExpectAsync("AUTH secret Password: ");
        await greeter.ExpectAsync("KEYBOARD on");
        await greeter.SettleAsync();
        await greeter.SendAsync("keyboard-tap hide");
        await greeter.ExpectAsync("KEYBOARD off");
    }

    [Fact]
    public async Task Never_keeps_it_away_and_always_shows_it_from_the_start()
    {
        using (var never = new Fixture("[keyboard]\non_screen = \"never\"\n"))
        using (var greeter = never.Start())
        {
            await Ready(greeter);
            await greeter.SendAsync("keyboard show");
            await greeter.ExpectAsync("KEYBOARD off");
        }

        using var always = new Fixture("[keyboard]\non_screen = \"always\"\non_screen_size = \"s\"\n");
        using var shown = always.Start();
        await shown.ExpectAsync("KEYBOARD on");
        await Ready(shown);
        await shown.SendAsync("keyboard hide");
        await shown.ExpectAsync("KEYBOARD on");
    }
}
