using Basin.Capabilities;
using Basin.Config;
using Basin.Diagnostics;
using Basin.Seat.Backends;
using Xunit;

namespace Basin.Tests;

public sealed class ChordMatcherTests
{
    private const uint KeyF12 = 88;
    private const uint KeyLeftShift = 42;
    private const uint KeyLeftMeta = 125;
    private const uint KeyCapsLock = 58;
    private const uint KeyT = 20;

    [Fact]
    public void A_chord_matches_only_its_own_modifiers_and_ignores_locks()
    {
        using var host = new CompositorTestHost();
        var keyboard = host.Seat.Keyboard;
        keyboard.SetKeymap(new KeymapNames(Layout: "us"));
        var chord = new ChordMatcher(keyboard);
        Assert.True(chord.TrySet("Super+F12", BasinLog.For("test")));

        Assert.False(chord.Matches(KeyF12, true));
        keyboard.NotifyKey(1, KeyLeftMeta, true);
        Assert.True(chord.Matches(KeyF12, true));
        Assert.True(chord.Matches(KeyF12, false));
        Assert.False(chord.Matches(KeyF12, false));

        keyboard.NotifyKey(2, KeyLeftShift, true);
        Assert.False(chord.Matches(KeyF12, true));
        keyboard.NotifyKey(3, KeyLeftShift, false);

        keyboard.NotifyKey(4, KeyCapsLock, true);
        keyboard.NotifyKey(5, KeyCapsLock, false);
        Assert.True(chord.Matches(KeyF12, true));
    }

    [Fact]
    public void A_letter_chord_matches_with_shift_held_and_an_empty_chord_clears()
    {
        using var host = new CompositorTestHost();
        var keyboard = host.Seat.Keyboard;
        keyboard.SetKeymap(new KeymapNames(Layout: "us"));
        var chord = new ChordMatcher(keyboard);
        Assert.True(chord.TrySet("Super+Shift+t", BasinLog.For("test")));
        keyboard.NotifyKey(1, KeyLeftMeta, true);
        keyboard.NotifyKey(2, KeyLeftShift, true);
        Assert.True(chord.Matches(KeyT, true));
        Assert.True(chord.MatchesKeysym(keyboard.KeysymFor(KeyT)));

        Assert.True(chord.TrySet("", BasinLog.For("test")));
        Assert.False(chord.IsSet);
        Assert.False(chord.Matches(KeyT, true));
    }
}
