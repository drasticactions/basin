using Xunit;

namespace Basin.SoftKeyboard.Tests;

public sealed class GridTests
{
    public static TheoryData<string> LayoutIds => [.. KeyboardLayouts.Ids];

    [Theory]
    [MemberData(nameof(LayoutIds))]
    public void Every_built_in_layout_loads_with_five_rows_and_a_full_bottom_row(string id)
    {
        var layout = Keys.Layout(id);
        Assert.Equal(id, layout.Id);
        foreach (var bottom in Enum.GetValues<BottomRow>())
        {
            var grid = layout.Grid(bottom);
            Assert.Equal(KeyGrid.DefaultRows * KeyGrid.DefaultColumns, grid.Cells.Count);
            Assert.Equal(layout.WidthUnits, grid.RowWeight(KeyGrid.DefaultRows - 1), 3);
            for (var row = 0; row < KeyGrid.DefaultRows; row++)
            {
                Assert.True(grid.RowWeight(row) > 0, $"{id} row {row}");
            }
        }

        Assert.NotEmpty(layout.DotCom);
        Assert.Equal(".com", layout.DotCom[0].Text);
    }

    [Theory]
    [InlineData("us-qwerty")]
    [InlineData("fr-azerty")]
    [InlineData("de-qwertz")]
    public void A_regular_layout_sums_every_row_to_its_width_units(string id)
    {
        var layout = Keys.Layout(id);
        var grid = layout.Grid();
        for (var row = 0; row < KeyGrid.DefaultRows; row++)
        {
            Assert.Equal(layout.WidthUnits, grid.RowWeight(row), 3);
        }
    }

    [Fact]
    public void The_us_qwerty_geometry_at_1024_matches_lunace()
    {
        var engine = Keys.Engine();
        var one = engine.Rects[Keys.Find(engine, "1")];
        Assert.Equal(46.55f, one.X, 0.1f);
        Assert.Equal(93.1f, one.Width, 0.1f);
        Assert.Equal(55f, engine.Rects[Keys.Find(engine, "1")].Height, 0.01f);
        Assert.Equal(70f, engine.Rects[Keys.Find(engine, "q")].Height, 0.01f);
        Assert.Equal(55f, engine.Rects[Keys.Find(engine, "q")].Y, 0.01f);
        var space = engine.Rects[Keys.Find(engine, " ")];
        Assert.Equal(5 * 93.09f, space.Width, 0.1f);
    }

    [Fact]
    public void At_768_one_unit_is_69_8()
    {
        var engine = Keys.Engine(width: 768);
        Assert.Equal(69.8f, engine.Rects[Keys.Find(engine, "q")].Width, 0.1f);
    }

    [Fact]
    public void The_invisible_keys_hand_off_to_their_visible_owners()
    {
        var engine = Keys.Engine();
        var hit = engine.HitTester;
        Assert.Equal(Keys.Find(engine, "q"), hit.Find(10, 20, diamonds: false));
        Assert.Equal(Keys.Find(engine, "backspace"), hit.Find(1020, 20, diamonds: false));
        Assert.Equal(Keys.Find(engine, "a"), hit.Find(10, 55 + 70 + 35, diamonds: false));
    }

    [Fact]
    public void The_language_key_takes_one_unit_from_symbol()
    {
        var layout = Keys.Layout();
        var languages = new[] { KeyCap.LanguageChoice(0, "En"), KeyCap.LanguageChoice(1, "Fr") };
        var grid = layout.Grid(BottomRow.Default, languages);
        var symbol = grid[KeyGrid.DefaultRows - 1, KeyboardLayout.SymbolColumn];
        var language = grid[KeyGrid.DefaultRows - 1, KeyboardLayout.SymbolColumn + 1];
        Assert.Equal(1, symbol.Weight);
        Assert.Equal(KeyFunction.Language, language.Plain!.Function);
        Assert.Equal("En", language.Plain.Label);
        Assert.Equal(2, language.LongPress!.Count);
        Assert.Equal(layout.WidthUnits, grid.RowWeight(KeyGrid.DefaultRows - 1), 3);
        Assert.Equal(2, layout.Grid(BottomRow.Default, [languages[0]])[KeyGrid.DefaultRows - 1, KeyboardLayout.SymbolColumn].Weight);
    }

    [Fact]
    public void Email_and_url_rows_carry_the_at_and_dotcom_keys()
    {
        var layout = Keys.Layout();
        var email = layout.Grid(BottomRow.Email);
        var url = layout.Grid(BottomRow.Url);
        Assert.Contains(email.Cells, c => c.Plain?.Text == "@");
        Assert.Contains(email.Cells, c => c.Plain?.Text == ".com" && c.LongPress!.Any(e => e.Text == ".org"));
        Assert.Contains(url.Cells, c => c.Plain?.Text == "/" && c.LongPress!.Any(e => e.Text == "http://"));
        Assert.DoesNotContain(layout.Grid().Cells, c => c.Plain?.Text == ".com");
    }

    [Fact]
    public void A_user_file_shadows_a_built_in_layout()
    {
        var dir = Directory.CreateTempSubdirectory("basin-layouts");
        try
        {
            using (var stream = typeof(KeyboardLayouts).Assembly.GetManifestResourceStream("layouts/us-qwerty.toml")!)
            using (var reader = new StreamReader(stream))
            {
                File.WriteAllText(Path.Combine(dir.FullName, "us-qwerty.toml"), reader.ReadToEnd().Replace("name = \"US QWERTY\"", "name = \"Mine\""));
            }

            Assert.Equal("Mine", KeyboardLayouts.Load("us-qwerty", dir.FullName)!.Name);
            Assert.Equal("US QWERTY", KeyboardLayouts.Load("us-qwerty")!.Name);
            Assert.Null(KeyboardLayouts.Load("nope", dir.FullName));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("fr_FR.UTF-8", "fr-azerty")]
    [InlineData("de_DE.UTF-8", "de-qwertz")]
    [InlineData("sv_SE.UTF-8", "se-qwerty")]
    [InlineData("en_GB.UTF-8", "us-qwerty")]
    [InlineData(null, "us-qwerty")]
    public void The_locale_picks_the_full_layout_id(string? locale, string expected) =>
        Assert.Equal(expected, KeyboardLayouts.DefaultFor(locale));

    [Theory]
    [InlineData(KeyboardSize.ExtraSmall, 243, 239, 4)]
    [InlineData(KeyboardSize.Small, 291, 286, 5)]
    [InlineData(KeyboardSize.Medium, 340, 335, 5)]
    [InlineData(KeyboardSize.Large, 393, 387, 6)]
    public void The_size_presets_split_into_keymap_and_padding(KeyboardSize size, int height, int keymap, int padding)
    {
        var metrics = KeyboardMetrics.For(size, 1024, 768);
        Assert.Equal(new KeyboardMetrics(height, keymap, padding), metrics);
    }

    [Fact]
    public void A_portrait_output_derives_its_height_until_a_size_is_chosen()
    {
        Assert.Equal(300, KeyboardMetrics.For(KeyboardSize.Medium, 768, 1024, sizeChosen: false).Height);
        Assert.Equal(250, KeyboardMetrics.For(KeyboardSize.Medium, 400, 900, sizeChosen: false).Height);
        Assert.Equal(340, KeyboardMetrics.For(KeyboardSize.Medium, 768, 1024, sizeChosen: true).Height);
        Assert.Equal(200 - 28, KeyboardMetrics.For(KeyboardSize.Large, 1024, 200).Height);
    }
}
