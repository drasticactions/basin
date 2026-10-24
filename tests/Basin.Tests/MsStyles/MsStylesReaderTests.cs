using System.Buffers.Binary;
using System.Text;
using Basin.Themes.MsStyles;
using Xunit;

namespace Basin.Tests;

public sealed class MsStylesReaderTests
{
    private const int Caption = (int)WindowPart.Caption;
    private const int CloseButton = (int)WindowPart.CloseButton;

    [Fact]
    public void Plain_selects_the_first_color_and_size_by_default()
    {
        var theme = MsStylesTheme.Parse(MsStylesWriter.Plain());

        Assert.Equal(3, theme.Format);
        Assert.Equal(["Blue", "Green"], theme.ColorSchemes);
        Assert.Equal(["NormalSize", "LargeFonts"], theme.Sizes);
        Assert.Equal("Blue", theme.Color);
        Assert.Equal("NormalSize", theme.Size);
        Assert.Equal("Large Fonts", theme.DisplayNameOfSize("LargeFonts"));
        Assert.True(theme.SysMetrics.TryGetFont(SysFont.CaptionFont, out var font));
        Assert.Equal(new MsStylesFont("Basin Test Sans", 9, MsStylesFont.Bold, false, false, false), font);
        var window = theme.OpenClass("Window")!;
        Assert.True(window.TryGetImage(Caption, 0, ThemeProperty.ImageFile, out var caption));
        Assert.Equal("BLUE_CAPTION_BMP", caption.Name);
    }

    [Fact]
    public void Plain_selects_a_variant_by_name_in_any_case()
    {
        var theme = MsStylesTheme.Parse(MsStylesWriter.Plain(), color: "green", size: "LARGEFONTS");

        Assert.Equal("Green", theme.Color);
        Assert.Equal("LargeFonts", theme.Size);
        Assert.True(theme.SysMetrics.TryGetFont(SysFont.CaptionFont, out var font));
        Assert.Equal(12, font.Points);
        Assert.True(theme.SysMetrics.TryGetSize(SysSize.CaptionBarHeight, out var height));
        Assert.Equal(25, height);
        Assert.True(theme.OpenClass("Window")!.TryGetImage(Caption, 0, ThemeProperty.ImageFile, out var caption));
        Assert.Equal("GREEN_CAPTION_BMP", caption.Name);
        Assert.Equal(MsStylesImageFormat.Dib, caption.Format);
        Assert.Equal((byte)'B', caption.Bytes.Span[0]);
    }

    [Fact]
    public void An_unknown_variant_is_refused_by_name()
    {
        var error = Assert.Throws<ArgumentException>(() => MsStylesTheme.Parse(MsStylesWriter.Plain(), color: "Purple"));
        Assert.Contains("Purple", error.Message);
        Assert.Contains("Blue, Green", error.Message);
    }

    [Fact]
    public void The_documentation_comes_from_themes_ini()
    {
        var documentation = MsStylesTheme.Parse(MsStylesWriter.Plain()).Documentation;

        Assert.Equal("Plain", documentation.DisplayName);
        Assert.Equal("Plain", documentation.Name);
        Assert.Equal("basin", documentation.Author);
        Assert.Equal("1.0", documentation.Version);
    }

    [Theory]
    [InlineData("Window", "", "Window", null, null)]
    [InlineData("Window(Active)", "", "Window", null, "Active")]
    [InlineData("Window.Caption", "", "Window", "Caption", null)]
    [InlineData(" Window.Caption ( Active ) ", "", "Window", "Caption", "Active")]
    [InlineData("Start::Button.PushButton(Hot)", "Start", "Button", "PushButton", "Hot")]
    [InlineData("TaskBar::Toolbar", "TaskBar", "Toolbar", null, null)]
    public void Section_names_split_into_app_class_part_and_state(string section, string app, string className, string? part, string? state)
    {
        Assert.True(MsStylesIniReader.TryParseSectionName(section, out var parsedApp, out var parsedClass, out var parsedPart, out var parsedState));
        Assert.Equal(app, parsedApp);
        Assert.Equal(className, parsedClass);
        Assert.Equal(part, parsedPart);
        Assert.Equal(state, parsedState);
    }

    [Fact]
    public void The_tokenizer_strips_comments_and_whitespace()
    {
        var ini = MsStylesIni.Parse("﻿; leading comment\r\n  [Window.Caption]  \r\n\tSizingMargins = 1, 2, 3, 4 ; a comment\r\n;Hidden = 1\r\n\r\nImageCount=2\r\n");

        var section = Assert.Single(ini.Sections);
        Assert.Equal("Window.Caption", section.Name);
        Assert.Equal([("SizingMargins", "1, 2, 3, 4"), ("ImageCount", "2")], section.Values);
    }

    [Fact]
    public void Every_xp_primitive_parses()
    {
        var window = MsStylesTheme.Parse(MsStylesWriter.Plain()).OpenClass("Window")!;

        Assert.True(window.TryGetPosition(CloseButton, 0, ThemeProperty.Offset, out var offset));
        Assert.Equal(new MsStylesPosition(-20, 4), offset);
        Assert.True(window.TryGetEnum<OffsetType>(CloseButton, 0, ThemeProperty.OffsetType, out var offsetType));
        Assert.Equal(OffsetType.TopRight, offsetType);
        Assert.True(window.TryGetMargins(Caption, 0, ThemeProperty.SizingMargins, out var margins));
        Assert.Equal(new MsStylesMargins(8, 8, 6, 4), margins);
        Assert.True(window.TryGetEnum<SizingType>(Caption, 0, ThemeProperty.SizingType, out var sizing));
        Assert.Equal(SizingType.Stretch, sizing);
        Assert.True(window.TryGetBool(Caption, 0, ThemeProperty.Transparent, out var transparent));
        Assert.True(transparent);
        Assert.True(window.TryGetInt(CloseButton, 0, ThemeProperty.ImageCount, out var count));
        Assert.Equal(8, count);
        Assert.True(window.TryGetColor(0, 0, ThemeProperty.TextShadowColor, out var shadow));
        Assert.Equal(new MsStylesColor(0, 0, 64), shadow);
        Assert.True(window.TryGetPosition(0, 0, ThemeProperty.TextShadowOffset, out var shadowOffset));
        Assert.Equal(new MsStylesPosition(1, 1), shadowOffset);
        Assert.True(window.TryGetString(CloseButton, 0, ThemeProperty.ImageFile1, out var path));
        Assert.Equal(@"Blue\CloseGlyph.bmp", path);
        Assert.True(window.TryGetImage(CloseButton, 0, ThemeProperty.ImageFile2, out var large));
        Assert.Equal("BLUE_CLOSEGLYPH16_BMP", large.Name);
        Assert.False(window.TryGetInt(CloseButton, 0, ThemeProperty.MinDpi3, out _));
    }

    [Fact]
    public void Integers_parse_as_wine_does_except_for_the_sign()
    {
        Assert.Equal([-25, 5], MsStylesValueParser.Integers("-25, 5", 4));
        Assert.Equal([1, 2, 3], MsStylesValueParser.Integers("1 2,3", 4));
        Assert.Equal([7], MsStylesValueParser.Integers("x7-", 4));
        Assert.Equal([-3], MsStylesValueParser.Integers("- -3", 4));
        Assert.True(MsStylesValueParser.ParseBool("True"));
        Assert.True(MsStylesValueParser.ParseBool("t"));
        Assert.False(MsStylesValueParser.ParseBool("1"));
        Assert.True(MsStylesValueParser.TryParseFont("Segoe UI, 9, Underline, Quality:ClearType", out var font));
        Assert.Equal(new MsStylesFont("Segoe UI", 9, MsStylesFont.Regular, false, true, false), font);
        Assert.Equal("BLUE_FRAME_LEFT_BMP", MsStylesValueParser.ResourceNameOf(@"Blue\frame.left.bmp"));
    }

    [Fact]
    public void The_lookup_chain_runs_app_class_then_class_then_globals()
    {
        var theme = MsStylesTheme.Parse(MsStylesWriter.Plain());
        var app = theme.OpenClass("Window", app: "Test")!;
        Assert.Equal("Test::Window", app.FullName);

        Assert.True(app.TryGetMargins(Caption, 1, ThemeProperty.ContentMargins, out var own));
        Assert.Equal(new MsStylesMargins(7, 7, 7, 7), own);
        Assert.Equal(PropertyOrigin.Part, app.OriginOf(Caption, 1, ThemeProperty.ContentMargins));

        Assert.True(app.TryGetMargins(Caption, 1, ThemeProperty.SizingMargins, out var inherited));
        Assert.Equal(new MsStylesMargins(8, 8, 6, 4), inherited);
        Assert.True(app.TryGetEnum<TextShadowType>(Caption, 1, ThemeProperty.TextShadowType, out var shadowType));
        Assert.Equal(TextShadowType.Single, shadowType);
        Assert.Equal(PropertyOrigin.State, app.OriginOf(Caption, 1, ThemeProperty.TextShadowType));

        Assert.True(app.TryGetColor(Caption, 1, ThemeProperty.TextColor, out var text));
        Assert.Equal(new MsStylesColor(1, 2, 3), text);
        Assert.Equal(PropertyOrigin.Global, app.OriginOf(Caption, 1, ThemeProperty.TextColor));
        Assert.Equal(PropertyOrigin.Class, app.OriginOf(Caption, 1, ThemeProperty.BgType));
        Assert.Equal(PropertyOrigin.NotFound, app.OriginOf(Caption, 1, ThemeProperty.AtlasRect));
    }

    [Fact]
    public void TryGetInt_refuses_a_color()
    {
        var window = MsStylesTheme.Parse(MsStylesWriter.Plain()).OpenClass("Window")!;
        Assert.False(window.TryGetInt(Caption, 1, ThemeProperty.TextColor, out var value));
        Assert.Equal(0, value);
        Assert.True(window.TryGetColor(Caption, 1, ThemeProperty.TextColor, out _));
    }

    [Fact]
    public void The_style_map_ignores_an_app_prefix()
    {
        Assert.True(MsStylesStyleMap.HasClass("Explorer::Window"));
        Assert.True(MsStylesStyleMap.TryLookupPartState("Window", "Caption", "Active", out var part, out var state));
        Assert.True(MsStylesStyleMap.TryLookupPartState("Explorer::Window", "Caption", "Active", out var prefixedPart, out var prefixedState));
        Assert.Equal((part, state), (prefixedPart, prefixedState));
        Assert.NotEqual(0, part);
        Assert.Equal(MsStylesStyleMap.PartName("Window", part), MsStylesStyleMap.PartName("Explorer::Window", part));
    }

    [Fact]
    public void OpenClass_follows_uxtheme_list_rules()
    {
        var theme = MsStylesTheme.Parse(MsStylesWriter.Plain());

        Assert.Equal("Window", theme.OpenClass("NoSuchClass; window")!.FullName);
        Assert.Equal("Test::Window", theme.OpenClass("Test::Window")!.FullName);
        Assert.Equal("Window", theme.OpenClass("Other::Window")!.FullName);
        Assert.Null(theme.OpenClass("Test::Window", app: "Explorer"));
        Assert.Null(theme.OpenClass("NoSuchClass"));
        Assert.Equal("Window", theme.OpenClass("Window".AsSpan())!.FullName);
    }

    [Fact]
    public void Unknown_names_are_counted_not_thrown()
    {
        var ini = MsStylesWriter.PlainIni("NormalSize", "Blue") + """

            [Window.NoSuchPart]
            ImageCount = 2

            [NoSuchClass.Part]
            ImageCount = 2

            [Window.Caption(NoSuchState)]
            ImageCount = 2

            [Window.Caption]
            NoSuchProperty = 1
            BgType = NoSuchEnum
            ImageFile7 = Blue\Missing.bmp
            this line has no equals sign
            """;
        var theme = MsStylesTheme.Parse(MsStylesWriter.V3(ini));

        Assert.Equal(1, theme.Diagnostics.Counts["part:Window.NoSuchPart"]);
        Assert.Equal(1, theme.Diagnostics.Counts["class:NoSuchClass.Part"]);
        Assert.Equal(1, theme.Diagnostics.Counts["state:Window.Caption(NoSuchState)"]);
        Assert.Equal(1, theme.Diagnostics.Counts["property:NoSuchProperty"]);
        Assert.Equal(1, theme.Diagnostics.Counts["enum:BGTYPE=NoSuchEnum"]);
        Assert.Equal(1, theme.Diagnostics.Counts["image:BLUE_MISSING_BMP"]);
        Assert.Equal(1, theme.Diagnostics.CountOf("ini-line"));
        Assert.Equal(0, theme.Diagnostics.CountOf("resource-type"));
        Assert.NotNull(theme.OpenClass("Window"));
    }

    [Fact]
    public void Glassy_walks_every_record_to_the_end_of_the_blob()
    {
        var theme = MsStylesTheme.Parse(MsStylesWriter.Glassy());

        Assert.Equal(4, theme.Format);
        Assert.Equal(0, theme.Diagnostics.Total);
        Assert.Equal(["NormalColor"], theme.ColorSchemes);
        Assert.Equal(["NormalSize"], theme.Sizes);
        Assert.Equal("Glassy", theme.Documentation.Name);
        Assert.Equal("Glassy", theme.Documentation.DisplayName);
        Assert.Equal("basin", theme.Documentation.Author);

        Assert.True(theme.SysMetrics.TryGetFont(SysFont.CaptionFont, out var stringFont));
        Assert.Equal(new MsStylesFont("Basin Test Sans", 9, MsStylesFont.Regular, false, false, false), stringFont);
        Assert.True(theme.SysMetrics.TryGetFont(SysFont.SmallCaptionFont, out var logFont));
        Assert.Equal(stringFont, logFont);
        Assert.True(theme.SysMetrics.TryGetColor(SysColor.ActiveCaption, out var active));
        Assert.Equal(new MsStylesColor(153, 180, 209), active);
        Assert.True(theme.SysMetrics.TryGetSize(SysSize.PaddedBorderWidth, out var padded));
        Assert.Equal(4, padded);

        var window = theme.OpenClass("Window")!;
        Assert.True(window.TryGetBool((int)WindowPart.HelpButton, 0, ThemeProperty.LocalizedMirrorImage, out var mirror));
        Assert.True(mirror);
        Assert.True(window.TryGetRect(0, 0, ThemeProperty.DefaultPaneSize, out var pane));
        Assert.Equal(new MsStylesRect(0, 0, 12, 34), pane);
        Assert.Equal(PropertyOrigin.Global, window.OriginOf(0, 0, ThemeProperty.DefaultPaneSize));
        Assert.True(window.TryGetImage(CloseButton, 0, ThemeProperty.ImageFile3, out var glyph));
        Assert.Equal(MsStylesImageFormat.Png, glyph.Format);

        var dwm = theme.OpenClass("DWMWindow")!;
        Assert.True(dwm.TryGetImage(40, 1, ThemeProperty.AtlasImage, out var atlas));
        Assert.Equal(MsStylesImageFormat.Png, atlas.Format);
        Assert.True(dwm.TryGetRect(40, 1, ThemeProperty.AtlasRect, out var rect));
        Assert.Equal(new MsStylesRect(76, 160, 196, 250), rect);
        Assert.True(dwm.TryGetInt(40, 2, ThemeProperty.Opacity, out var opacity));
        Assert.Equal(40, opacity);
        Assert.Equal("CompositedWindow::Window", theme.OpenClass("Window", "CompositedWindow")!.FullName);
    }

    [Fact]
    public void A_record_that_runs_past_the_blob_is_refused_with_its_offset()
    {
        var theme = MsStylesTheme.Parse(MsStylesWriter.Glassy());
        using var blob = new MemoryStream();
        MsStylesWriter.Record(blob, (int)ThemeProperty.ImageCount, ThemePrimitive.Int, 0, 1, 0, 0, [2, 0, 0, 0]);
        MsStylesWriter.Record(blob, (int)ThemeProperty.ImageCount, ThemePrimitive.Int, 0, 2, 0, 0, [2, 0, 0, 0]);
        var bytes = blob.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40 + 28), 64);

        var error = Assert.Throws<MsStylesFormatException>(() => MsStylesVariantReader.ReadRecords(theme, ["Window"], bytes, "VARIANT"));
        Assert.Equal(40, error.Offset);
    }

    [Fact]
    public void Plain_reads_the_same_from_both_formats()
    {
        var v3 = MsStylesTheme.Parse(MsStylesWriter.Plain());
        var v4 = MsStylesTheme.Parse(MsStylesWriter.PlainAsV4());
        var compared = 0;
        foreach (var cls in v3.Classes.Where(c => c.Name != "documentation"))
        {
            var other = v4.OpenClass(cls.FullName);
            Assert.NotNull(other);
            foreach (var (part, state) in cls.PartStates)
            {
                foreach (var id in cls.PropertiesOf(part, state))
                {
                    compared++;
                    var primitive = cls.PrimitiveOf(part, state, id);
                    Assert.Equal(primitive, other!.PrimitiveOf(part, state, id));
                    Assert.Equal(cls.OriginOf(part, state, id), other.OriginOf(part, state, id));
                    switch (primitive)
                    {
                        case ThemePrimitive.Filename:
                            Assert.Equal(cls.TryGetImage(part, state, id, out _), other.TryGetImage(part, state, id, out _));
                            break;
                        case ThemePrimitive.Font:
                            cls.TryGetFont(part, state, id, out var a);
                            other.TryGetFont(part, state, id, out var b);
                            Assert.Equal(a, b);
                            break;
                        case ThemePrimitive.String:
                            cls.TryGetString(part, state, id, out var s3);
                            other.TryGetString(part, state, id, out var s4);
                            Assert.Equal(s3, s4);
                            break;
                        case ThemePrimitive.Color:
                            cls.TryGetColor(part, state, id, out var c3);
                            other.TryGetColor(part, state, id, out var c4);
                            Assert.Equal(c3, c4);
                            break;
                        default:
                            Assert.True(cls.TryGetIntList(part, state, id, out _) || cls.TryGetInt(part, state, id, out _) || cls.TryGetMargins(part, state, id, out _) || cls.TryGetPosition(part, state, id, out _) || cls.TryGetRect(part, state, id, out _));
                            Assert.Equal(Values(cls, part, state, id), Values(other, part, state, id));
                            break;
                    }
                }
            }
        }

        Assert.True(compared > 100);
    }

    private static string Values(MsStylesClass cls, int part, int state, ThemeProperty id)
    {
        var text = new StringBuilder();
        if (cls.TryGetInt(part, state, id, out var value))
        {
            text.Append(value);
        }

        if (cls.TryGetMargins(part, state, id, out var margins))
        {
            text.Append(margins);
        }

        if (cls.TryGetRect(part, state, id, out var rect))
        {
            text.Append(rect);
        }

        if (cls.TryGetPosition(part, state, id, out var position))
        {
            text.Append(position);
        }

        return text.ToString();
    }
}
