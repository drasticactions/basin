using Xkb;
using Xunit;

namespace Basin.SoftKeyboard.Tests;

public sealed class XkbGridTests
{
    private static KeyboardLayout Build(string layout, string model = "pc105")
    {
        using var context = XkbContext.Create();
        using var keymap = context.CreateKeymap(new XkbRuleNames { Layout = layout, Model = model })
            ?? throw new InvalidOperationException($"xkb cannot compile {layout}");
        return XkbGrid.Build(keymap, 0, Keys.Layout());
    }

    private static string Row(KeyGrid grid, int row) =>
        string.Concat(Enumerable.Range(0, KeyGrid.DefaultColumns)
            .Select(c => grid[row, c])
            .Where(static c => c.IsVisible)
            .Select(static c => c.Plain!.IsText ? c.Plain.Text : "<" + c.Plain.Name + ">"));

    [Fact]
    public void A_us_keymap_gives_the_us_letters_with_the_us_symbol_page()
    {
        var layout = Build("us");
        var grid = layout.Grid();
        Assert.Equal(KeyboardLayouts.Xkb, layout.Id);
        Assert.Equal("Xkb", layout.LanguageLabel);
        Assert.Equal("en", layout.Language);
        Assert.Equal("1234567890-=", Row(grid, 0));
        Assert.Equal("qwertyuiop[]<backspace>", Row(grid, 1));
        Assert.Equal("asdfghjkl;'\\<return>", Row(grid, 2));
        Assert.Equal("<shift>zxcvbnm,./<shift>", Row(grid, 3));
        var q = grid[1, 0];
        Assert.True(q.Plain!.IsLetter);
        Assert.Equal("`", q.Alternate!.Text);
        Assert.Equal(":-)", grid[3, 1].Alternate!.Text);
        Assert.Equal("!", grid[0, 0].Alternate!.Text);
        Assert.Equal(KeyFunction.Symbol, grid[4, 1].Plain!.Function);
    }

    [Fact]
    public void A_french_keymap_puts_azerty_in_place()
    {
        var grid = Build("fr").Grid();
        Assert.StartsWith("azertyuiop", Row(grid, 1));
        Assert.StartsWith("qsdfghjklm", Row(grid, 2));
        Assert.Equal("&", grid[0, 0].Plain!.Text);
        Assert.Equal("1", grid[0, 0].Alternate!.Text);
        Assert.Equal("fr", Build("fr").Language);
    }

    [Fact]
    public void A_jp106_keymap_keeps_its_extra_keys_and_restretches_the_rows()
    {
        var grid = Build("jp", "jp106").Grid();
        var numbers = Row(grid, 0);
        Assert.Equal("1234567890-^\\", numbers);
        Assert.Equal("qwertyuiop@[<backspace>", Row(grid, 1));
        Assert.Equal("asdfghjkl;:]<return>", Row(grid, 2));
        Assert.Equal("<shift>zxcvbnm,./\\<shift>", Row(grid, 3));
        Assert.Equal(13, grid.RowWeight(0));

        var engine = new SoftKeyboardEngine(grid);
        engine.Layout(1024, 335, 5);
        var last = engine.Rects[engine.Grid.Index(0, 12)];
        Assert.Equal(1024f, last.Right, 0.01f);
        Assert.Equal(1024f / 13, last.Width, 0.01f);
    }

    [Fact]
    public void Long_press_offers_levels_three_and_four()
    {
        var grid = Build("de").Grid();
        var q = Enumerable.Range(0, KeyGrid.DefaultColumns).Select(c => grid[1, c]).First(static c => c.Plain?.Text == "q");
        Assert.NotNull(q.LongPress);
        Assert.Equal("q", q.LongPress![0].Text);
        Assert.Contains(q.LongPress, static c => c.Text == "@");
    }
}
