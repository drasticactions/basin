using Basin.Config;
using Xunit;

namespace Basin.Tests;

public sealed class TriggerSyntaxTests
{
    [Theory]
    [InlineData("CTRL+SHIFT+r", Modifiers.Ctrl | Modifiers.Shift, "r")]
    [InlineData("LOGO+t", Modifiers.Super, "t")]
    [InlineData("alt+F4", Modifiers.Alt, "F4")]
    [InlineData("Print", Modifiers.None, "Print")]
    public void The_freedesktop_grammar_parses_to_a_chord(string trigger, Modifiers expectedModifiers, string keyName)
    {
        Assert.True(TriggerSyntax.TryParse(trigger, out var keysym, out var modifiers));
        Assert.Equal(expectedModifiers, modifiers);
        Assert.Equal(Keysym.FromName(keyName), keysym);
    }

    [Theory]
    [InlineData("")]
    [InlineData("HYPER+t")]
    [InlineData("CTRL+NotAKey")]
    [InlineData("CTRL+")]
    public void A_bad_trigger_parses_to_nothing(string trigger)
    {
        Assert.False(TriggerSyntax.TryParse(trigger, out var keysym, out var modifiers));
        Assert.Equal(Keysym.NoSymbol, keysym);
        Assert.Equal(Modifiers.None, modifiers);
    }

    [Fact]
    public void Format_round_trips_and_describe_is_the_readable_form()
    {
        Assert.True(TriggerSyntax.TryParse("CTRL+SHIFT+LOGO+r", out var keysym, out var modifiers));
        Assert.Equal("CTRL+SHIFT+LOGO+r", TriggerSyntax.Format(keysym, modifiers));
        Assert.Equal("Ctrl+Shift+Super+r", TriggerSyntax.Describe(keysym, modifiers));
        Assert.True(TriggerSyntax.TryParse(TriggerSyntax.Format(keysym, modifiers), out var again, out var againModifiers));
        Assert.Equal(keysym, again);
        Assert.Equal(modifiers, againModifiers);
    }
}
