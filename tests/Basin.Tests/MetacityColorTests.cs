using Basin.Frames.Metacity;
using Xunit;

namespace Basin.Tests;

public sealed class MetacityColorTests
{
    private static MetacityColor Resolve(string spec, MetacityPalette? palette = null)
    {
        var parsed = MetacityColorSpec.Parse(spec, out var error);
        Assert.Null(error);
        return parsed.Resolve(palette ?? MetacityPalette.Light);
    }

    private static void AssertClose(MetacityColor expected, MetacityColor actual)
    {
        Assert.Equal(expected.R, actual.R, 3);
        Assert.Equal(expected.G, actual.G, 3);
        Assert.Equal(expected.B, actual.B, 3);
        Assert.Equal(expected.A, actual.A, 3);
    }

    [Theory]
    [InlineData("#fff", 1.0, 1.0, 1.0)]
    [InlineData("#123456", 0x12 / 255.0, 0x34 / 255.0, 0x56 / 255.0)]
    [InlineData("#123456789", 0x123 / 4095.0, 0x456 / 4095.0, 0x789 / 4095.0)]
    [InlineData("#1234567890ab", 0x1234 / 65535.0, 0x5678 / 65535.0, 0x90ab / 65535.0)]
    [InlineData("rgb(255,0,128)", 1.0, 0.0, 128 / 255.0)]
    [InlineData("rgb(10%,20%,30%)", 0.1, 0.2, 0.3)]
    [InlineData("white", 1.0, 1.0, 1.0)]
    [InlineData("black", 0.0, 0.0, 0.0)]
    [InlineData("dark gray", 169 / 255.0, 169 / 255.0, 169 / 255.0)]
    [InlineData("DarkGray", 169 / 255.0, 169 / 255.0, 169 / 255.0)]
    public void Basic_colors_parse_as_gdk_does(string spec, double r, double g, double b) =>
        AssertClose(new MetacityColor(r, g, b, 1.0), Resolve(spec));

    [Fact]
    public void Rgba_keeps_its_alpha()
    {
        AssertClose(new MetacityColor(1 / 255.0, 2 / 255.0, 3 / 255.0, 0.5), Resolve("rgba(1,2,3,0.5)"));
    }

    [Theory]
    [InlineData("#12345")]
    [InlineData("nosuchcolour")]
    [InlineData("gtk:fg")]
    [InlineData("gtk:fg[NOPE]")]
    [InlineData("gtk:nope[NORMAL]")]
    [InlineData("blend/#fff/#000")]
    [InlineData("shade/#fff")]
    [InlineData("gtk:custom(bad name,#fff)")]
    [InlineData("gtk:custom#fff")]
    public void Malformed_specs_report_an_error(string spec)
    {
        MetacityColorSpec.Parse(spec, out var error);
        Assert.NotNull(error);
    }

    [Fact]
    public void Gtk_components_read_the_palette()
    {
        var palette = new MetacityPalette(
            MetacityColor.FromHex(0x808080),
            MetacityColor.FromHex(0x101010),
            MetacityColor.FromHex(0xFFFFFF),
            MetacityColor.FromHex(0x000000));
        palette.Set(MetacityStateFlag.Selected, bg: MetacityColor.FromHex(0x0000FF));
        AssertClose(MetacityColor.FromHex(0x808080), Resolve("gtk:bg[NORMAL]", palette));
        AssertClose(MetacityColor.FromHex(0x0000FF), Resolve("gtk:bg[selected]", palette));
        AssertClose(MetacityColor.FromHex(0x101010), Resolve("gtk:fg[NORMAL]", palette));
        AssertClose(MetacityColor.FromHex(0x000000), Resolve("gtk:text[NORMAL]", palette));
        AssertClose(MetacityColor.FromHex(0xFFFFFF), Resolve("gtk:base[NORMAL]", palette));
        AssertClose(MetacityColor.FromHex(0x808080).Shade(1.3), Resolve("gtk:light[NORMAL]", palette));
        AssertClose(MetacityColor.FromHex(0x808080).Shade(0.7), Resolve("gtk:dark[NORMAL]", palette));
        AssertClose(MetacityColor.FromHex(0x808080).Shade(1.3).Mean(MetacityColor.FromHex(0x808080).Shade(0.7)), Resolve("gtk:mid[NORMAL]", palette));
        AssertClose(MetacityColor.FromHex(0x101010).Mean(MetacityColor.FromHex(0xFFFFFF)), Resolve("gtk:text_aa[NORMAL]", palette));
    }

    [Fact]
    public void Shade_follows_gtk_style_shade()
    {
        var gray = 0x80 / 255.0;
        AssertClose(new MetacityColor(gray * 1.3, gray * 1.3, gray * 1.3, 1.0), Resolve("shade/#808080/1.3"));
        AssertClose(new MetacityColor(0.375, 0.125, 0.125, 1.0), Resolve("shade/#ff0000/0.5"));
        AssertClose(new MetacityColor(1.0, 1.0, 1.0, 1.0), Resolve("shade/#ffffff/2.0"));
    }

    [Fact]
    public void Blend_interpolates_toward_the_foreground_and_keeps_the_background_alpha()
    {
        AssertClose(new MetacityColor(0.25, 0.25, 0.25, 1.0), Resolve("blend/#000000/#ffffff/0.25"));
        AssertClose(new MetacityColor(0.75, 0.0, 0.0, 0.5), Resolve("blend/rgba(255,0,0,0.5)/rgba(0,0,0,1)/0.25"));
    }

    [Fact]
    public void Gtk_custom_takes_the_palette_entry_or_the_fallback()
    {
        var palette = MetacityPalette.Light;
        AssertClose(MetacityColor.FromHex(0x00FF00), Resolve("gtk:custom(accent,#00ff00)", palette));
        palette.Custom["accent"] = MetacityColor.FromHex(0xFF00FF);
        AssertClose(MetacityColor.FromHex(0xFF00FF), Resolve("gtk:custom(accent,#00ff00)", palette));
        AssertClose(MetacityColor.FromHex(0x808080), Resolve("gtk:custom(other,shade/#808080/1.0)", palette));
    }

    [Theory]
    [InlineData("menu:minimize,maximize,close", new[] { "Menu" }, new[] { "Minimize", "Maximize", "Close" })]
    [InlineData("close,minimize,maximize:", new[] { "Close", "Minimize", "Maximize" }, new string[0])]
    [InlineData("shade,stick:menu", new[] { "Shade", "Unshade", "Stick", "Unstick" }, new[] { "Menu" })]
    [InlineData("menu,bogus,menu:close", new[] { "Menu" }, new[] { "Close" })]
    [InlineData("", new string[0], new string[0])]
    public void Button_layouts_parse_marcos_grammar(string text, string[] left, string[] right)
    {
        var layout = MetacityButtonLayout.Parse(text);
        Assert.Equal(left, layout.Left.Take(layout.LeftCount).Select(f => f.ToString()));
        Assert.Equal(right, layout.Right.Take(layout.RightCount).Select(f => f.ToString()));
    }

    [Fact]
    public void Spacers_mark_the_button_before_them()
    {
        var layout = MetacityButtonLayout.Parse("menu,spacer,minimize:maximize,spacer,close");
        Assert.Equal(2, layout.LeftCount);
        Assert.True(layout.LeftSpacer[0]);
        Assert.False(layout.LeftSpacer[1]);
        Assert.Equal(2, layout.RightCount);
        Assert.True(layout.RightSpacer[0]);
        Assert.False(layout.RightSpacer[1]);
        Assert.Equal("menu:minimize,maximize,close", MetacityButtonLayout.Default.ToString());
    }
}
