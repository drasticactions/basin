using Xunit;

namespace Basin.SoftKeyboard.Tests;

public sealed class TyperTests
{
    public static TheoryData<string> LayoutIds => [.. KeyboardLayouts.Ids];

    [Theory]
    [MemberData(nameof(LayoutIds))]
    public void Letters_capitals_and_symbols_type_through_the_keys(string id)
    {
        var engine = Keys.Engine(id);
        var typed = Keys.Record(engine);
        var typer = new SoftKeyboardTyper(engine);

        Assert.True(typer.Type("Hello World 42@!"));

        Assert.Equal("Hello World 42@!", Printed(typed));
    }

    [Theory]
    [MemberData(nameof(LayoutIds))]
    public void A_long_press_alternate_types_through_the_open_list(string id)
    {
        var engine = Keys.Engine(id);
        var typed = Keys.Record(engine);
        var typer = new SoftKeyboardTyper(engine);
        var alternate = FirstLongPressAlternate(engine);
        Assert.NotNull(alternate);

        Assert.True(typer.Type(alternate));

        Assert.Equal(alternate, Printed(typed));
        Assert.False(engine.IsLongPressOpen);
    }

    [Theory]
    [InlineData("a\U0001F600b")]
    [InlineData("\U0001F198")]
    [InlineData("\U0001F9D1\u200D\U0001F4BB")]
    [InlineData("\u2764\uFE0F")]
    public void An_emoji_types_as_one_key_from_the_emoji_pages(string text)
    {
        var layout = Keys.Layout("win8-standard-en-us");
        var engine = new SoftKeyboardEngine(layout.Grid()) { Pages = layout.PageSet() };
        engine.Layout(1300, 300, 5);
        var typed = Keys.Record(engine);
        var typer = new SoftKeyboardTyper(engine);

        Assert.True(typer.Type(text));

        Assert.Equal(text.Replace("\uFE0F", string.Empty, StringComparison.Ordinal), Printed(typed));
    }

    [Fact]
    public void A_character_no_key_produces_stops_the_typing_there()
    {
        var engine = Keys.Engine();
        var typed = Keys.Record(engine);
        var typer = new SoftKeyboardTyper(engine);

        Assert.False(typer.Type("ab☃cd"));

        Assert.Equal("ab", Printed(typed));
    }

    [Fact]
    public void Every_tap_moves_the_clock_floor_by_the_step()
    {
        var engine = Keys.Engine();
        var typer = new SoftKeyboardTyper(engine) { StepMilliseconds = 10_000 };
        var before = typer.Clock;

        Assert.True(typer.Tap("a"));
        Assert.True(typer.Tap("space"));

        Assert.True(typer.Clock >= before + 20_000);
        Assert.False(typer.Tap("no-such-key"));
        Assert.False(typer.TapCell(-1));
    }

    [Fact]
    public void Two_taps_of_the_same_key_never_read_as_a_double_tap()
    {
        var engine = Keys.Engine();
        var typed = Keys.Record(engine);
        var typer = new SoftKeyboardTyper(engine);

        Assert.True(typer.Tap("shift"));
        Assert.True(typer.Tap("shift"));
        Assert.True(typer.Type("a"));

        Assert.Equal("a", Printed(typed));
    }

    [Fact]
    public void The_name_aliases_find_their_keys()
    {
        var engine = Keys.Engine();
        var typer = new SoftKeyboardTyper(engine);

        Assert.Equal(Keys.Find(engine, " "), typer.Find("space"));
        Assert.Equal(Keys.Find(engine, "symbol"), typer.Find("sym"));
        Assert.Equal(Keys.Find(engine, "return"), typer.Find("enter"));
        Assert.Equal(Keys.Find(engine, "a"), typer.Find("A"));
    }

    [Fact]
    public void Holding_a_key_past_the_long_press_time_opens_its_list()
    {
        var engine = Keys.Engine();
        var pressed = 0;
        var typer = new SoftKeyboardTyper(engine);
        typer.Pressing += () => pressed++;

        Assert.True(typer.HoldCell(typer.Find("e"), 2000));

        Assert.True(engine.IsLongPressOpen);
        Assert.Equal(1, pressed);
    }

    private static string? FirstLongPressAlternate(SoftKeyboardEngine engine)
    {
        for (var cell = 0; cell < KeyGrid.DefaultRows * KeyGrid.DefaultColumns; cell++)
        {
            if (engine.Grid[cell] is not { IsVisible: true, LongPress: { } entries })
            {
                continue;
            }

            for (var index = 1; index < entries.Count; index++)
            {
                var text = entries[index].IsText ? entries[index].Text : null;
                if (text is { Length: 1 } && char.IsLetter(text[0]) && text[0] > 127)
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static string Printed(IEnumerable<TypedKey> typed) =>
        string.Concat(typed.Where(static t => t.Cap.IsText).Select(static t => t.Text));
}
