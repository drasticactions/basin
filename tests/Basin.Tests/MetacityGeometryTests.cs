using Basin.Capabilities;
using Basin.Frames.Metacity;
using Xunit;

namespace Basin.Tests;

public sealed class MetacityGeometryTests
{
    private const FrameCapabilities AllThree = FrameCapabilities.WindowMenu | FrameCapabilities.Minimize | FrameCapabilities.Maximize;

    private sealed class Rig : IDisposable
    {
        public Rig(string theme = "Atlanta", int major = 1, string layout = "menu:minimize,maximize,close")
        {
            Skia = new TestFrameTheme();
            Font = new MetacityFont(Skia.Typeface, 14);
            Resources = new MetacityResources();
            Theme = MetacityTheme.ParseFile(MetacityParserTests.Fixture(theme, major));
            Painter = new MetacityPainter(Theme, MetacityPalette.Light, MetacityButtonLayout.Parse(layout), Font, Resources);
        }

        public TestFrameTheme Skia { get; }

        public MetacityFont Font { get; }

        public MetacityResources Resources { get; }

        public MetacityTheme Theme { get; }

        public MetacityPainter Painter { get; }

        public int T => Font.TextHeight(1.0);

        public MetacityFrameGeometry Layout(FrameState state, int width, int height)
        {
            var geometry = new MetacityFrameGeometry();
            Assert.True(Painter.Layout(new MetacityFrameInput(state, default), width, height, geometry));
            return geometry;
        }

        public void Dispose()
        {
            Resources.Dispose();
            Font.Dispose();
            Skia.Dispose();
        }
    }

    [Fact]
    public void Atlanta_borders_follow_get_borders()
    {
        using var rig = new Rig();
        var t = rig.T;
        var insets = rig.Painter.Measure(new MetacityFrameInput(new FrameState { Active = true, Capabilities = AllThree }, default));
        Assert.Equal(new FrameInsets(t + 10, 6, 7, 6), insets);

        var shaded = rig.Painter.Measure(new MetacityFrameInput(new FrameState { Shaded = true, Capabilities = AllThree }, default));
        Assert.Equal(new FrameInsets(t + 10, 6, 0, 6), shaded);

        var fullscreen = rig.Painter.Measure(new MetacityFrameInput(new FrameState { Fullscreen = true }, default));
        Assert.Equal(default, fullscreen);

        var utility = rig.Painter.Measure(new MetacityFrameInput(new FrameState { Kind = FrameKind.Utility }, default));
        var tiny = rig.Font.TextHeight(1.0 / (1.2 * 1.2 * 1.2));
        Assert.Equal(new FrameInsets(Math.Max(11 + 2, tiny + 1 + 3 + 3), 3, 4, 3), utility);

        var border = rig.Painter.Measure(new MetacityFrameInput(new FrameState { Kind = FrameKind.Border }, default));
        Assert.Equal(new FrameInsets(4, 4, 4, 4), border);
    }

    [Fact]
    public void Atlanta_button_rects_follow_calc_geometry()
    {
        using var rig = new Rig();
        var t = rig.T;
        var bw = t + 8;
        var geometry = rig.Layout(new FrameState { Active = true, Capabilities = AllThree }, 300, 200);

        Assert.Equal(312, geometry.Width);
        Assert.Equal(200 + t + 10 + 7, geometry.Height);
        Assert.Equal(new Box(306 - bw, 1, bw, bw), geometry.VisibleRect(FramePart.Close));
        Assert.Equal(new Box(306 - 2 * bw, 1, bw, bw), geometry.VisibleRect(FramePart.Maximize));
        Assert.Equal(new Box(306 - 3 * bw, 1, bw, bw), geometry.VisibleRect(FramePart.Minimize));
        Assert.Equal(new Box(6, 1, bw, bw), geometry.VisibleRect(FramePart.Menu));
        Assert.Equal(geometry.VisibleRect(FramePart.Close), geometry.ClickableRect(FramePart.Close));
        Assert.Equal(new Box(9 + bw, 4, 293 - 4 * bw, t + 3), geometry.TitleRect);

        Assert.Equal(geometry.VisibleRect(FramePart.Menu), geometry.LeftSingleBackground);
        Assert.Equal(geometry.VisibleRect(FramePart.Minimize), geometry.RightLeftBackground);
        Assert.Equal(geometry.VisibleRect(FramePart.Maximize), geometry.RightMiddleBackgrounds[0]);
        Assert.Equal(geometry.VisibleRect(FramePart.Close), geometry.RightRightBackground);
        Assert.Equal(default, geometry.LeftLeftBackground);

        Assert.Equal(FramePart.Close, geometry.ButtonAt(306 - bw + 1, 2));
        Assert.Equal(FramePart.Menu, geometry.ButtonAt(7, 2));
        Assert.Equal(FramePart.None, geometry.ButtonAt(150, 2));
    }

    [Fact]
    public void Missing_capabilities_drop_their_buttons()
    {
        using var rig = new Rig();
        var t = rig.T;
        var bw = t + 8;
        var geometry = rig.Layout(new FrameState { Capabilities = FrameCapabilities.Maximize }, 300, 200);
        Assert.Equal(new Box(306 - bw, 1, bw, bw), geometry.VisibleRect(FramePart.Close));
        Assert.Equal(new Box(306 - 2 * bw, 1, bw, bw), geometry.VisibleRect(FramePart.Maximize));
        Assert.Equal(default, geometry.VisibleRect(FramePart.Minimize));
        Assert.Equal(default, geometry.VisibleRect(FramePart.Menu));
        Assert.Equal(new Box(9, 4, 302 - 2 * bw - 9, t + 3), geometry.TitleRect);
    }

    [Fact]
    public void The_fitting_loop_drops_spacers_then_buttons_in_marcos_order()
    {
        using var rig = new Rig(layout: "menu,spacer:minimize,maximize,spacer,close");
        var t = rig.T;
        var bw = t + 8;

        var spacer = (int)(0.75 * bw);
        var roomy = rig.Layout(new FrameState { Capabilities = AllThree }, 4 * bw + 2 * spacer + 40, 100);
        Assert.Equal(new Box(6 + bw + spacer + 3, 4, 40 - 7, t + 3), roomy.TitleRect);
        Assert.Equal(new Box(roomy.Width - 6 - bw, 1, bw, bw), roomy.VisibleRect(FramePart.Close));
        Assert.Equal(new Box(roomy.Width - 6 - 2 * bw - spacer, 1, bw, bw), roomy.VisibleRect(FramePart.Maximize));

        var oneSpacerShort = rig.Layout(new FrameState { Capabilities = AllThree }, 4 * bw + spacer, 100);
        Assert.Equal(new Box(6 + bw + 3, 4, 0, 0), oneSpacerShort.TitleRect);
        Assert.Equal(new Box(oneSpacerShort.Width - 6 - 2 * bw - spacer, 1, bw, bw), oneSpacerShort.VisibleRect(FramePart.Maximize));

        var noSpacers = rig.Layout(new FrameState { Capabilities = AllThree }, 4 * bw, 100);
        Assert.Equal(new Box(noSpacers.Width - 6 - 2 * bw - spacer, 1, bw, bw), noSpacers.VisibleRect(FramePart.Maximize));
        Assert.Equal(new Box(6, 1, bw, bw), noSpacers.VisibleRect(FramePart.Menu));
        Assert.Equal(new Box(6 + bw + 3, 4, 0, 0), noSpacers.TitleRect);

        using var mirrored = new Rig(layout: "menu:minimize,spacer,maximize,close");
        var mirroredNoSpacers = mirrored.Layout(new FrameState { Capabilities = AllThree }, 4 * bw, 100);
        Assert.Equal(new Box(mirroredNoSpacers.Width - 6 - 2 * bw, 1, bw, bw), mirroredNoSpacers.VisibleRect(FramePart.Maximize));
        Assert.Equal(new Box(mirroredNoSpacers.Width - 6 - 3 * bw, 1, bw, bw), mirroredNoSpacers.VisibleRect(FramePart.Minimize));

        var threeFit = rig.Layout(new FrameState { Capabilities = AllThree }, 3 * bw, 100);
        Assert.Equal(default, threeFit.VisibleRect(FramePart.Minimize));
        Assert.NotEqual(default, threeFit.VisibleRect(FramePart.Maximize));
        Assert.NotEqual(default, threeFit.VisibleRect(FramePart.Close));
        Assert.NotEqual(default, threeFit.VisibleRect(FramePart.Menu));

        var twoFit = rig.Layout(new FrameState { Capabilities = AllThree }, 2 * bw, 100);
        Assert.Equal(default, twoFit.VisibleRect(FramePart.Maximize));
        Assert.NotEqual(default, twoFit.VisibleRect(FramePart.Close));
        Assert.NotEqual(default, twoFit.VisibleRect(FramePart.Menu));

        var oneFits = rig.Layout(new FrameState { Capabilities = AllThree }, bw, 100);
        Assert.Equal(default, oneFits.VisibleRect(FramePart.Close));
        Assert.NotEqual(default, oneFits.VisibleRect(FramePart.Menu));

        var none = rig.Layout(new FrameState { Capabilities = AllThree }, bw - 1, 100);
        Assert.Equal(default, none.VisibleRect(FramePart.Menu));
        Assert.Equal(0, none.LeftCount + none.RightCount);
    }

    [Fact]
    public void Fitts_law_extends_the_outer_buttons_when_maximized()
    {
        using var rig = new Rig();
        var t = rig.T;
        var bw = t + 8;
        var geometry = rig.Layout(new FrameState { Maximized = true, Capabilities = AllThree }, 300, 200);
        Assert.Equal(300, geometry.Width);
        Assert.Equal(new Box(300 - bw, 1, bw, bw), geometry.VisibleRect(FramePart.Close));
        Assert.Equal(new Box(300 - bw, 0, bw, 1 + bw), geometry.ClickableRect(FramePart.Close));
        Assert.Equal(new Box(0, 0, bw, 1 + bw), geometry.ClickableRect(FramePart.Menu));
        Assert.Equal(geometry.VisibleRect(FramePart.Maximize), geometry.ClickableRect(FramePart.Maximize));
        Assert.Equal(FramePart.Close, geometry.ButtonAt(299, 0));
        Assert.Equal(FramePart.Menu, geometry.ButtonAt(0, 0));

        var tiledLeft = rig.Layout(new FrameState { Tiled = FrameTiling.Left, Capabilities = AllThree }, 300, 200);
        Assert.Equal(new Box(0, 0, bw + 12, 1 + bw), tiledLeft.ClickableRect(FramePart.Menu));
        Assert.Equal(tiledLeft.VisibleRect(FramePart.Close), tiledLeft.ClickableRect(FramePart.Close));
    }

    [Fact]
    public void A_title_that_does_not_fit_is_zeroed()
    {
        using var rig = new Rig();
        var t = rig.T;
        var bw = t + 8;
        var geometry = rig.Layout(new FrameState { Capabilities = AllThree }, 4 * bw + 1, 100);
        Assert.Equal(0, geometry.TitleRect.Width);
        Assert.Equal(0, geometry.TitleRect.Height);
    }

    [Fact]
    public void Corner_radii_need_five_pixels_of_border_unless_shaded()
    {
        var theme = MetacityParserTests.ParseText(
            MetacityParserTests.Wrap(
                "<frame_geometry name=\"g\" rounded_top_left=\"true\" rounded_bottom_left=\"7\"><distance name=\"left_width\" value=\"1\"/><distance name=\"right_width\" value=\"1\"/><distance name=\"bottom_height\" value=\"1\"/><distance name=\"left_titlebar_edge\" value=\"1\"/><distance name=\"right_titlebar_edge\" value=\"1\"/><distance name=\"button_width\" value=\"8\"/><distance name=\"button_height\" value=\"8\"/><distance name=\"title_vertical_pad\" value=\"1\"/><border name=\"title_border\" left=\"1\" right=\"1\" top=\"1\" bottom=\"1\"/><border name=\"button_border\" left=\"0\" right=\"0\" top=\"0\" bottom=\"0\"/></frame_geometry>"
                + "<draw_ops name=\"empty\"/>" + MetacityParserTests.MinimalStyle),
            1);
        using var skia = new TestFrameTheme();
        using var font = new MetacityFont(skia.Typeface, 14);
        using var resources = new MetacityResources();
        var painter = new MetacityPainter(theme, MetacityPalette.Light, MetacityButtonLayout.Default, font, resources);
        var geometry = new MetacityFrameGeometry();

        Assert.True(painter.Layout(new MetacityFrameInput(default, default), 100, 100, geometry));
        Assert.Equal(5, geometry.TopLeftRadius);
        Assert.Equal(0, geometry.BottomLeftRadius);
        Assert.True(geometry.HasRoundedCorner);

        Assert.True(painter.Layout(new MetacityFrameInput(new FrameState { Shaded = true }, default), 100, 100, geometry));
        Assert.Equal(5, geometry.TopLeftRadius);
        Assert.Equal(7, geometry.BottomLeftRadius);
        Assert.Equal(0, geometry.Borders.Bottom);
        Assert.Equal(geometry.Borders.Top, geometry.Height);
    }

    [Fact]
    public void Shade_above_and_sticky_buttons_split_on_state_and_need_their_capability()
    {
        using var rig = new Rig(theme: "eOS", major: 3, layout: "shade,above,stick:close");
        var without = rig.Layout(new FrameState { Capabilities = FrameCapabilities.Maximize }, 400, 100);
        Assert.Equal(default, without.VisibleRect(FramePart.Shade));
        Assert.Equal(default, without.VisibleRect(FramePart.Above));
        Assert.Equal(default, without.VisibleRect(FramePart.Stick));

        var with = rig.Layout(new FrameState { Capabilities = FrameCapabilities.Shade | FrameCapabilities.Above | FrameCapabilities.Stick }, 400, 100);
        Assert.NotEqual(default, with.VisibleRect(FramePart.Shade));
        Assert.NotEqual(default, with.VisibleRect(FramePart.Above));
        Assert.NotEqual(default, with.VisibleRect(FramePart.Stick));
        Assert.NotEqual(default, with.Buttons[(int)MetacityButtonType.Shade].Visible);
        Assert.Equal(default, with.Buttons[(int)MetacityButtonType.Unshade].Visible);

        var toggled = rig.Layout(new FrameState { Shaded = true, Above = true, Sticky = true, Capabilities = FrameCapabilities.Shade | FrameCapabilities.Above | FrameCapabilities.Stick }, 400, 100);
        Assert.Equal(default, toggled.Buttons[(int)MetacityButtonType.Shade].Visible);
        Assert.NotEqual(default, toggled.Buttons[(int)MetacityButtonType.Unshade].Visible);
        Assert.NotEqual(default, toggled.Buttons[(int)MetacityButtonType.Unabove].Visible);
        Assert.NotEqual(default, toggled.Buttons[(int)MetacityButtonType.Unstick].Visible);
        Assert.Equal(toggled.Buttons[(int)MetacityButtonType.Unshade].Visible, toggled.VisibleRect(FramePart.Shade));
    }
}
