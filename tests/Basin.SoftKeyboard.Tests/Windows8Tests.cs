using Xkb;
using Xunit;

namespace Basin.SoftKeyboard.Tests;

public sealed class Windows8Tests
{
    public static TheoryData<string> Windows8Ids => [.. KeyboardLayouts.Windows8Ids];

    private static string Row(KeyGrid grid, int row) =>
        string.Concat(Enumerable.Range(0, grid.Columns)
            .Select(c => grid[row, c])
            .Where(static c => c.IsVisible)
            .Select(static c => c.Plain!.IsText ? c.Plain.Text : "<" + c.Plain.Name + ">"));

    private static float AbsoluteWeight(KeyGrid grid, int row) => grid.RowWeight(row);

    private static SoftKeyboardEngine Engine(KeyboardLayout layout)
    {
        var engine = new SoftKeyboardEngine(layout.Grid());
        engine.Pages = layout.PageSet();
        engine.Layout(1300, 300, 5);
        return engine;
    }

    [Theory]
    [InlineData("win8-standard-en-us", 4, 13, 13f)]
    [InlineData("win8-thumb-en-us", 4, 17, 18f)]
    [InlineData("win8-full-en-us", 6, 16, 15f)]
    public void Each_row_of_each_page_has_the_hand_counted_width(string id, int rows, int columns, float units)
    {
        var layout = Keys.Layout(id);
        Assert.True(KeyboardLayouts.IsBuiltIn(id));
        Assert.Equal(units, layout.WidthUnits);
        var set = layout.PageSet();
        foreach (var name in set.Names.Where(static n => !n.StartsWith("emoji", StringComparison.Ordinal)))
        {
            Assert.True(set.TryGet(name, out var grid));
            Assert.Equal(rows, grid.Rows);
            Assert.Equal(columns, grid.Columns);
            for (var row = 0; row < rows; row++)
            {
                Assert.True(Math.Abs(units - AbsoluteWeight(grid, row)) < 0.001f, $"{id} {name} row {row} sums to {AbsoluteWeight(grid, row)}");
            }
        }
    }

    [Fact]
    public void The_standard_layout_is_qwerty_without_a_number_row()
    {
        var grid = Keys.Layout("win8-standard-en-us").Grid();
        Assert.Equal("<tab>qwertyuiop<backspace>", Row(grid, 0));
        Assert.Equal("asdfghjkl'<return>", Row(grid, 1));
        Assert.Equal("<shift>zxcvbnm,.?<shift>", Row(grid, 2));
        Assert.Equal("<page:symbols><control><emoji> <left><right><hide>", Row(grid, 3));
        Assert.Equal("&123", grid[3, 0].Plain!.Label);
        Assert.Equal(["{hide}", "shape:standard", "shape:thumb", "shape:full"],
            grid[3, 6].LongPress!.Select(static c => c.Function == KeyFunction.Hide ? "{hide}" : c.Name));
    }

    [Fact]
    public void The_thumb_layout_splits_the_letters_around_a_spacer_and_its_symbol_page_has_a_number_pad()
    {
        var layout = Keys.Layout("win8-thumb-en-us");
        var grid = layout.Grid();
        Assert.Equal("<tab>qwertyuiop<backspace>", Row(grid, 0));
        Assert.True(grid[0, 6].IsSpacer);
        var symbols = layout.PageGrid("symbols")!;
        Assert.Contains("123", Row(symbols, 0), StringComparison.Ordinal);
        Assert.Contains("456", Row(symbols, 1), StringComparison.Ordinal);
        Assert.Contains("789", Row(symbols, 2), StringComparison.Ordinal);
    }

    [Fact]
    public void The_full_layout_has_the_modifier_row_the_arrows_and_the_f_keys_on_fn()
    {
        var layout = Keys.Layout("win8-full-en-us");
        var grid = layout.Grid();
        Assert.Equal("<escape><home><end><page-up><page-down><delete>", Row(grid, 0));
        Assert.Equal("`1234567890-=<backspace>", Row(grid, 1));
        Assert.Equal("<caps>asdfghjkl;'<return>", Row(grid, 3));
        Assert.Equal("<shift>zxcvbnm,./<up><shift>", Row(grid, 4));
        Assert.Equal("<page:fn><control><super><alt> <emoji><left><down><right><hide>", Row(grid, 5));
        var fn = layout.PageGrid("fn")!;
        Assert.Equal("<escape><f1><f2><f3><f4><f5><f6><f7><f8><f9><f10><f11><f12><delete>", Row(fn, 0));
        Assert.Equal(Row(grid, 1), Row(fn, 1));
    }

    [Fact]
    public void Caps_locks_shift_and_a_second_tap_releases_it()
    {
        var engine = Engine(Keys.Layout("win8-full-en-us"));
        var typed = Keys.Record(engine);
        Keys.Tap(engine, "caps", 0);
        Keys.Tap(engine, "a", 100);
        Keys.Tap(engine, "b", 200);
        Keys.Tap(engine, "caps", 300);
        Keys.Tap(engine, "c", 400);
        Assert.Equal("ABc", Keys.Text(typed));
    }

    [Theory]
    [MemberData(nameof(Windows8Ids))]
    public void The_emoji_key_shows_a_page_of_emoji_and_abc_comes_back(string id)
    {
        var layout = Keys.Layout(id);
        var engine = Engine(layout);
        Assert.Contains(SoftKeyboardEngine.EmojiPage, engine.Pages!.Names);
        Keys.Tap(engine, "emoji", 0);
        Assert.Equal(SoftKeyboardEngine.EmojiPage, engine.Page);
        var typed = Keys.Record(engine);
        Keys.Tap(engine, "😀", 100);
        Assert.Equal("😀", Assert.Single(typed).Text);
        Assert.Equal(SoftKeyboardEngine.EmojiPage, engine.Page);
        var people = Enumerable.Range(0, engine.Grid.CellCount).First(i => engine.Grid[i].Plain?.Label == "👍");
        engine.TapCell(1, people, 200);
        Assert.Equal(EmojiPages.PageName(1, 0), engine.Page);
        var next = Enumerable.Range(0, engine.Grid.CellCount).First(i => engine.Grid[i].Plain?.Label == "→");
        engine.TapCell(1, next, 300);
        Assert.Equal(EmojiPages.PageName(1, 1), engine.Page);
        var abc = Enumerable.Range(0, engine.Grid.CellCount).First(i => engine.Grid[i].Plain?.Label == "abc");
        engine.TapCell(1, abc, 400);
        Assert.Equal(KeyPageSet.MainPage, engine.Page);
    }

    [Fact]
    public void The_emoji_file_has_eight_categories_and_every_page_fits_the_grid()
    {
        Assert.Equal(8, EmojiPages.BuiltIn.Count);
        Assert.All(EmojiPages.BuiltIn, static c => Assert.True(c.Emoji.Count >= 40, c.Id));
        Assert.Empty(Keys.Layout().PageNames);
    }

    [Theory]
    [MemberData(nameof(Windows8Ids))]
    public void The_typer_reaches_letters_capitals_digits_and_symbols_through_the_pages(string id)
    {
        var engine = Engine(Keys.Layout(id));
        var typed = Keys.Record(engine);
        var typer = new SoftKeyboardTyper(engine) { StepMilliseconds = 1000 };
        foreach (var c in "Hi, 42 $x!")
        {
            Assert.True(typer.TypeCharacter(c), $"{c} on page {engine.Page}");
        }

        Assert.Equal("Hi, 42 $x!", Keys.Text(typed));
    }

    private static XkbKeymap Keymap(XkbContext context, string layout) =>
        context.CreateKeymap(new XkbRuleNames { Layout = layout, Model = "pc105" })
            ?? throw new InvalidOperationException($"xkb cannot compile {layout}");

    [Fact]
    public void A_us_keymap_in_the_windows_8_shapes_matches_the_transcribed_files()
    {
        using var context = XkbContext.Create();
        using var keymap = Keymap(context, "us");
        var standard = XkbGrid.Standard(keymap, 0);
        var template = Keys.Layout("win8-standard-en-us").Grid();
        var grid = standard.Grid();
        Assert.Equal("Xkb", standard.LanguageLabel);
        for (var row = 0; row < grid.Rows; row++)
        {
            Assert.Equal(Row(template, row), Row(grid, row));
        }

        var full = XkbGrid.Full(keymap, 0).Grid();
        var fullTemplate = Keys.Layout("win8-full-en-us").Grid();
        for (var row = 0; row < full.Rows; row++)
        {
            Assert.Equal(Row(fullTemplate, row), Row(full, row));
        }
    }

    [Fact]
    public void A_german_keymap_gets_its_umlauts_and_qwertz_in_both_shapes()
    {
        using var context = XkbContext.Create();
        using var keymap = Keymap(context, "de");
        var standard = XkbGrid.Standard(keymap, 0);
        var grid = standard.Grid();
        Assert.Equal("de", standard.Language);
        Assert.Equal("<tab>qwertzuiopü<backspace>", Row(grid, 0));
        Assert.Equal("asdfghjklöä'<return>", Row(grid, 1));
        Assert.Equal("<shift>yxcvbnm,.?<shift>", Row(grid, 2));
        Assert.True(standard.PageSet().TryGet("symbols", out _));
        var full = XkbGrid.Full(keymap, 0);
        Assert.Equal("1234567890ß<backspace>", Row(full.Grid(), 1));
        Assert.StartsWith("<caps>asdfghjklöä", Row(full.Grid(), 3), StringComparison.Ordinal);
        Assert.Equal(Row(full.Grid(), 1), Row(full.PageGrid("fn")!, 1));
    }

    [Fact]
    public void A_french_keymap_gets_azerty_in_both_shapes()
    {
        using var context = XkbContext.Create();
        using var keymap = Keymap(context, "fr");
        var grid = XkbGrid.Standard(keymap, 0).Grid();
        Assert.Equal("<tab>azertyuiop<backspace>", Row(grid, 0));
        Assert.Equal("qsdfghjklmù'<return>", Row(grid, 1));
        Assert.Equal("<shift>wxcvbn,.?<shift>", Row(grid, 2));
        var full = XkbGrid.Full(keymap, 0).Grid();
        Assert.StartsWith("²&é\"'(-è_çà)=", Row(full, 1), StringComparison.Ordinal);
    }
}
