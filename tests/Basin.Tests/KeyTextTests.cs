using Basin.Capabilities;
using Xunit;

namespace Basin.Tests;

public sealed class KeyTextTests
{
    private const uint KeyQ = 16;
    private const uint KeyE = 18;
    private const uint KeyA = 30;
    private const uint KeyMinus = 12;
    private const uint KeyLeftBrace = 26;
    private const uint KeyLeftShift = 42;
    private const uint KeyLeftCtrl = 29;
    private const uint KeyRightAlt = 100;
    private const uint KeyEnter = 28;

    [Fact]
    public void A_us_key_gives_its_letter_and_shift_gives_the_capital()
    {
        using var host = new CompositorTestHost();
        var keyboard = host.Seat.Keyboard;
        keyboard.SetKeymap(new KeymapNames(Layout: "us"));

        Assert.Equal("a", Press(keyboard, KeyA));
        Assert.Equal("q", Press(keyboard, KeyQ));

        keyboard.NotifyKeyConsumed(KeyLeftShift, true);
        Assert.Equal("A", Press(keyboard, KeyA));
        keyboard.NotifyKeyConsumed(KeyLeftShift, false);
    }

    [Fact]
    public void Control_chords_and_control_characters_give_no_text()
    {
        using var host = new CompositorTestHost();
        var keyboard = host.Seat.Keyboard;
        keyboard.SetKeymap(new KeymapNames(Layout: "us"));

        Assert.Equal(string.Empty, Press(keyboard, KeyEnter));

        keyboard.NotifyKeyConsumed(KeyLeftCtrl, true);
        Assert.Equal(string.Empty, Press(keyboard, KeyA));
        keyboard.NotifyKeyConsumed(KeyLeftCtrl, false);
    }

    [Fact]
    public void A_de_layout_gives_sharp_s_and_altgr_q_gives_an_at_sign()
    {
        using var host = new CompositorTestHost();
        var keyboard = host.Seat.Keyboard;
        keyboard.SetKeymap(new KeymapNames(Layout: "de"));

        Assert.Equal("ß", Press(keyboard, KeyMinus));

        keyboard.NotifyKeyConsumed(KeyRightAlt, true);
        Assert.Equal("@", Press(keyboard, KeyQ));
        keyboard.NotifyKeyConsumed(KeyRightAlt, false);

        Assert.Equal("q", Press(keyboard, KeyQ));
    }

    [Fact]
    public void A_fr_dead_circumflex_composes_with_the_next_key()
    {
        using var host = new CompositorTestHost();
        var keyboard = host.Seat.Keyboard;
        keyboard.SetKeymap(new KeymapNames(Layout: "fr"));

        Assert.Equal(string.Empty, Press(keyboard, KeyLeftBrace));
        Assert.Equal("ê", Press(keyboard, KeyE));
        Assert.Equal("e", Press(keyboard, KeyE));
    }

    private static string Press(IKeyText text, uint key)
    {
        Span<char> into = stackalloc char[16];
        var length = text.TextFor(key, into);
        if (text is Basin.Seat.SeatKeyboard keyboard)
        {
            keyboard.NotifyKeyConsumed(key, true);
            keyboard.NotifyKeyConsumed(key, false);
        }

        return new string(into[..length]);
    }
}
