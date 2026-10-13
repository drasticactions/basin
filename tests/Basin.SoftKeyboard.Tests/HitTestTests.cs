using Xunit;

namespace Basin.SoftKeyboard.Tests;

public sealed class HitTestTests
{
    public static TheoryData<float, float, string, string> Touches => new()
    {
        { 141.6f, 127f, "s", "w" },
        { 186.2f, 127f, "s", "s" },
        { 186.2f, 137f, "s", "s" },
        { 186.2f, 10f, "2", "2" },
        { 186.2f, 50f, "2", "2" },
        { 156.2f, 50f, "2", "w" },
        { 605.1f, 268f, " ", " " },
        { 605.1f, 262f, "n", "n" },
        { 977.5f, 269f, "hide", "right shift" },
        { 977.5f, 280f, "hide", "right shift" },
        { 977.5f, 290f, "hide", "hide" },
        { 10f, 20f, "q", "q" },
        { 1020f, 20f, "backspace", "backspace" },
        { 10f, 160f, "a", "a" },
    };

    [Theory]
    [MemberData(nameof(Touches))]
    public void A_touch_lands_on_lunace_s_key_with_and_without_diamonds(float x, float y, string plain, string diamond)
    {
        var engine = Keys.Engine();
        Assert.Equal(Cell(engine, plain), engine.HitTester.Find(x, y, diamonds: false));
        Assert.Equal(Cell(engine, diamond), engine.HitTester.Find(x, y, diamonds: true));
    }

    private static int Cell(SoftKeyboardEngine engine, string name) =>
        name == "right shift" ? engine.Grid.Index(3, 10) : Keys.Find(engine, name);

    [Fact]
    public void Space_wins_its_own_upper_edge_against_a_letter_above()
    {
        var engine = Keys.Engine();
        var n = engine.Rects[Keys.Find(engine, "n")];
        Assert.Equal(Keys.Find(engine, " "), engine.HitTester.Find(n.CenterX, n.Bottom + 3));
        Assert.Equal(Keys.Find(engine, "n"), engine.HitTester.Find(n.CenterX, n.Bottom - 3));
    }

    [Fact]
    public void A_touch_outside_the_keymap_finds_nothing()
    {
        var engine = Keys.Engine();
        Assert.Equal(-1, engine.HitTester.Find(500, 400));
        Assert.Equal(-1, engine.HitTester.Find(1100, 100));
    }

    [Fact]
    public void A_touch_in_the_padding_above_maps_to_the_number_row()
    {
        var engine = Keys.Engine();
        Assert.Equal(Keys.Find(engine, "2"), engine.HitTester.Find(186.2f, -3f));
    }
}
