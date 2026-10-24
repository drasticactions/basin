using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Frames.Metacity;
using Basin.UI.Skia;
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
        palette.SetCustom("accent", MetacityColor.FromHex(0xFF00FF));
        AssertClose(MetacityColor.FromHex(0xFF00FF), Resolve("gtk:custom(accent,#00ff00)", palette));
        AssertClose(MetacityColor.FromHex(0x808080), Resolve("gtk:custom(other,shade/#808080/1.0)", palette));
    }

    [Fact]
    public void A_custom_color_change_repaints()
    {
        var text = MetacityParserTests.Wrap(
            MetacityParserTests.Geometry + "<draw_ops name=\"empty\"/>"
            + "<draw_ops name=\"fill\"><rectangle color=\"gtk:custom(accent,#00ff00)\" x=\"0\" y=\"0\" width=\"width\" height=\"height\" filled=\"true\"/></draw_ops>"
            + MetacityParserTests.MinimalStyle.Replace(
                "<frame_style name=\"s\" geometry=\"g\">",
                "<frame_style name=\"s\" geometry=\"g\"><piece position=\"entire_background\" draw_ops=\"fill\"/>",
                StringComparison.Ordinal));
        var theme = MetacityParserTests.ParseText(text, 1);
        var palette = MetacityPalette.Light;
        using var skia = new TestFrameTheme();
        using var font = new MetacityFont(skia.Typeface, 14);
        using var resources = new MetacityResources();
        var renderer = new MetacityFrameRenderer(theme, palette, MetacityButtonLayout.Parse(string.Empty), font, resources);
        using var host = new SkiaUIHost();
        var state = new FrameState { Title = "t", Active = true };
        var insets = renderer.Measure(state, 1.0);
        var surface = host.CreateSurface(new UISurfaceOptions { Target = UITargetKind.Memory, Width = 100 + insets.Left + insets.Right, Height = 40 + insets.Top + insets.Bottom, Scale = 1.0 });
        Assert.NotNull(surface);

        (byte R, byte G, byte B) Paint()
        {
            renderer.Draw(surface, new Box(insets.Left, insets.Top, 100, 40), state, default);
            Assert.True(surface.TryAcquire(out var frame));
            var buffer = (MemoryBuffer)frame.Buffer!;
            var pixels = BufferCapture.ReadRgba(buffer);
            var at = ((1 * buffer.Width) + (buffer.Width / 2)) * 4;
            frame.Dispose();
            return (pixels[at], pixels[at + 1], pixels[at + 2]);
        }

        Assert.Equal(((byte)0x00, (byte)0xFF, (byte)0x00), Paint());
        palette.SetCustom("accent", MetacityColor.FromHex(0xFF00FF));
        Assert.Equal(((byte)0xFF, (byte)0x00, (byte)0xFF), Paint());
        Assert.True(palette.RemoveCustom("accent"));
        Assert.Equal(((byte)0x00, (byte)0xFF, (byte)0x00), Paint());
        surface.Dispose();
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
