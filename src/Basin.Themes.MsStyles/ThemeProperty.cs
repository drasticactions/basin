namespace Basin.Themes.MsStyles;

/// <summary>
/// Identifies a visual-style property by its uxtheme <c>TMT_*</c> id, as stored in an msstyles theme. <see cref="MsStylesClass"/> looks properties up by these values.
/// </summary>
public enum ThemeProperty
{
    /// <summary>
    /// The id of raw image bitmap data. No reader or renderer in this library uses it.
    /// </summary>
    DibData = 2,
    /// <summary>
    /// The id of raw glyph bitmap data. No reader or renderer in this library uses it.
    /// </summary>
    GlyphDibData = 8,
    /// <summary>
    /// The generic property of the <see cref="ThemePrimitive.Enum"/> type. The name table does not map it.
    /// </summary>
    Enum = 200,
    /// <summary>
    /// The generic property of the <see cref="ThemePrimitive.String"/> type, read as a string.
    /// </summary>
    String = 201,
    /// <summary>
    /// The generic property of the <see cref="ThemePrimitive.Int"/> type, read as an integer.
    /// </summary>
    Int = 202,
    /// <summary>
    /// The generic property of the <see cref="ThemePrimitive.Bool"/> type, read as a boolean.
    /// </summary>
    Bool = 203,
    /// <summary>
    /// The generic property of the <see cref="ThemePrimitive.Color"/> type, read as an RGB color.
    /// </summary>
    Color = 204,
    /// <summary>
    /// The generic property of the <see cref="ThemePrimitive.Margins"/> type, read as four integers.
    /// </summary>
    Margins = 205,
    /// <summary>
    /// The generic property of the <see cref="ThemePrimitive.Filename"/> type, with the same spelling, read as an image resource.
    /// </summary>
    Filename = 206,
    /// <summary>
    /// The generic property of the <see cref="ThemePrimitive.Size"/> type, read as one integer.
    /// </summary>
    Size = 207,
    /// <summary>
    /// The generic property of the <see cref="ThemePrimitive.Position"/> type, read as an x and y pair.
    /// </summary>
    Position = 208,
    /// <summary>
    /// The generic property of the <see cref="ThemePrimitive.Rect"/> type, read as four integers.
    /// </summary>
    Rect = 209,
    /// <summary>
    /// The generic property of the <see cref="ThemePrimitive.Font"/> type, read as a font.
    /// </summary>
    Font = 210,
    /// <summary>
    /// The generic property of the <see cref="ThemePrimitive.IntList"/> type, read as an integer list.
    /// </summary>
    IntList = 211,
    /// <summary>
    /// The id that matches <see cref="ThemePrimitive.HBitmap"/>, with the same spelling. No reader or renderer in this library uses it.
    /// </summary>
    HBitmap = 212,
    /// <summary>
    /// The id that matches <see cref="ThemePrimitive.DiskStream"/>. <see cref="MsStylesClass"/> returns its image when a caller asks for <see cref="AtlasImage"/> and the class has none.
    /// </summary>
    DiskStream = 213,
    /// <summary>
    /// The id that matches <see cref="ThemePrimitive.Stream"/>. No reader or renderer in this library uses it.
    /// </summary>
    Stream = 214,
    /// <summary>
    /// The id that matches <see cref="ThemePrimitive.BitmapRef"/>. No reader or renderer in this library uses it.
    /// </summary>
    BitmapRef = 215,
    /// <summary>
    /// The list of color scheme names that the theme declares, read as a string.
    /// </summary>
    ColorSchemes = 401,
    /// <summary>
    /// The list of size names that the theme declares, read as a string.
    /// </summary>
    Sizes = 402,
    /// <summary>
    /// The character set of the theme text, read as an integer.
    /// </summary>
    CharSet = 403,
    /// <summary>
    /// The theme name in the documentation class, read as a string. It fills <see cref="MsStylesDocumentation.Name"/>.
    /// </summary>
    Name = 600,
    /// <summary>
    /// The theme display name, read as a string. It fills <see cref="MsStylesDocumentation.DisplayName"/>.
    /// </summary>
    DisplayName = 601,
    /// <summary>
    /// The theme tooltip text, read as a string. It fills <see cref="MsStylesDocumentation.Tooltip"/>.
    /// </summary>
    Tooltip = 602,
    /// <summary>
    /// The company that made the theme, read as a string. It fills <see cref="MsStylesDocumentation.Company"/>.
    /// </summary>
    Company = 603,
    /// <summary>
    /// The theme author, read as a string. It fills <see cref="MsStylesDocumentation.Author"/>.
    /// </summary>
    Author = 604,
    /// <summary>
    /// The theme copyright text, read as a string. It fills <see cref="MsStylesDocumentation.Copyright"/>.
    /// </summary>
    Copyright = 605,
    /// <summary>
    /// The theme web address, read as a string. It fills <see cref="MsStylesDocumentation.Url"/>.
    /// </summary>
    Url = 606,
    /// <summary>
    /// The theme version text, read as a string. It fills <see cref="MsStylesDocumentation.Version"/>.
    /// </summary>
    Version = 607,
    /// <summary>
    /// The theme description, read as a string. It fills <see cref="MsStylesDocumentation.Description"/>.
    /// </summary>
    Description = 608,
    /// <summary>
    /// The system font for window captions, read as a font. The frame style uses it for the title text.
    /// </summary>
    CaptionFont = 801,
    /// <summary>
    /// The system font for small (tool window) captions, read as a font. The frame style falls back to <see cref="CaptionFont"/>.
    /// </summary>
    SmallCaptionFont = 802,
    /// <summary>
    /// The system font for menus, read as a font.
    /// </summary>
    MenuFont = 803,
    /// <summary>
    /// The system font for status bars, read as a font.
    /// </summary>
    StatusFont = 804,
    /// <summary>
    /// The system font for message boxes, read as a font.
    /// </summary>
    MsgBoxFont = 805,
    /// <summary>
    /// The system font for icon labels, read as a font.
    /// </summary>
    IconTitleFont = 806,
    /// <summary>
    /// The font for first-level headings, read as a font.
    /// </summary>
    Heading1Font = 807,
    /// <summary>
    /// The font for second-level headings, read as a font.
    /// </summary>
    Heading2Font = 808,
    /// <summary>
    /// The font for body text, read as a font.
    /// </summary>
    BodyFont = 809,
    /// <summary>
    /// A system metric that turns on flat menus, read as a boolean. <see cref="MsStylesSysMetrics.FlatMenus"/> exposes it.
    /// </summary>
    FlatMenus = 1001,
    /// <summary>
    /// The system width of the window sizing border, read as an integer size. The frame style uses 1 when it is absent.
    /// </summary>
    SizingBorderWidth = 1201,
    /// <summary>
    /// The system width of a vertical scroll bar, read as an integer size.
    /// </summary>
    ScrollbarWidth = 1202,
    /// <summary>
    /// The system height of a horizontal scroll bar, read as an integer size.
    /// </summary>
    ScrollbarHeight = 1203,
    /// <summary>
    /// The system width of a caption button, read as an integer size. The frame style uses it for the button width.
    /// </summary>
    CaptionBarWidth = 1204,
    /// <summary>
    /// The system height of the caption bar, read as an integer size. The frame style uses it for the title bar height.
    /// </summary>
    CaptionBarHeight = 1205,
    /// <summary>
    /// The system width of a small caption button, read as an integer size.
    /// </summary>
    SMCaptionBarWidth = 1206,
    /// <summary>
    /// The system height of a small caption bar, read as an integer size.
    /// </summary>
    SMCaptionBarHeight = 1207,
    /// <summary>
    /// The system width of a menu bar item, read as an integer size.
    /// </summary>
    MenuBarWidth = 1208,
    /// <summary>
    /// The system height of the menu bar, read as an integer size.
    /// </summary>
    MenuBarHeight = 1209,
    /// <summary>
    /// The system padding added to the sizing border. The frame style reads it as a size only for format 4 and later themes.
    /// </summary>
    PaddedBorderWidth = 1210,
    /// <summary>
    /// The minimum display color depth that the theme supports, read as an integer.
    /// </summary>
    MinColorDepth = 1301,
    /// <summary>
    /// The CSS name of the theme, read as a string.
    /// </summary>
    CssName = 1401,
    /// <summary>
    /// The XML name of the theme, read as a string.
    /// </summary>
    XmlName = 1402,
    /// <summary>
    /// The time the theme was last updated. No reader or renderer in this library uses it.
    /// </summary>
    LastUpdated = 1403,
    /// <summary>
    /// An alias name for the theme. No reader or renderer in this library uses it.
    /// </summary>
    Alias = 1404,
    /// <summary>
    /// The system color for the scroll bar track, read as an RGB color.
    /// </summary>
    Scrollbar = 1601,
    /// <summary>
    /// The system color for the desktop background, read as an RGB color.
    /// </summary>
    Background = 1602,
    /// <summary>
    /// The system color for the active window caption, read as an RGB color.
    /// </summary>
    ActiveCaption = 1603,
    /// <summary>
    /// The system color for the inactive window caption, read as an RGB color.
    /// </summary>
    InactiveCaption = 1604,
    /// <summary>
    /// The system color for the menu background, read as an RGB color.
    /// </summary>
    Menu = 1605,
    /// <summary>
    /// The system color for the window background, read as an RGB color.
    /// </summary>
    Window = 1606,
    /// <summary>
    /// The system color for the window frame line, read as an RGB color.
    /// </summary>
    WindowFrame = 1607,
    /// <summary>
    /// The system color for menu text, read as an RGB color.
    /// </summary>
    MenuText = 1608,
    /// <summary>
    /// The system color for window text, read as an RGB color.
    /// </summary>
    WindowText = 1609,
    /// <summary>
    /// The system color for active caption text, read as an RGB color. The frame painter uses it when the caption part has no own text color.
    /// </summary>
    CaptionText = 1610,
    /// <summary>
    /// The system color for the active window border, read as an RGB color.
    /// </summary>
    ActiveBorder = 1611,
    /// <summary>
    /// The system color for the inactive window border, read as an RGB color.
    /// </summary>
    InactiveBorder = 1612,
    /// <summary>
    /// The system color for the multiple-document workspace, read as an RGB color.
    /// </summary>
    AppWorkspace = 1613,
    /// <summary>
    /// The system color for selected items, read as an RGB color.
    /// </summary>
    Highlight = 1614,
    /// <summary>
    /// The system color for selected text, read as an RGB color.
    /// </summary>
    HighlightText = 1615,
    /// <summary>
    /// The system color for 3D element faces, read as an RGB color.
    /// </summary>
    BtnFace = 1616,
    /// <summary>
    /// The system color for 3D element shadows, read as an RGB color.
    /// </summary>
    BtnShadow = 1617,
    /// <summary>
    /// The system color for disabled text, read as an RGB color.
    /// </summary>
    GrayText = 1618,
    /// <summary>
    /// The system color for push button text, read as an RGB color.
    /// </summary>
    BtnText = 1619,
    /// <summary>
    /// The system color for inactive caption text, read as an RGB color. The frame painter uses it when the caption part has no own text color.
    /// </summary>
    InactiveCaptionText = 1620,
    /// <summary>
    /// The system color for 3D element highlights, read as an RGB color.
    /// </summary>
    BtnHighlight = 1621,
    /// <summary>
    /// The system color for the dark shadow of 3D elements, read as an RGB color.
    /// </summary>
    DkShadow3D = 1622,
    /// <summary>
    /// The system color for the light edge of 3D elements, read as an RGB color.
    /// </summary>
    Light3D = 1623,
    /// <summary>
    /// The system color for tooltip text, read as an RGB color.
    /// </summary>
    InfoText = 1624,
    /// <summary>
    /// The system color for the tooltip background, read as an RGB color.
    /// </summary>
    InfoBk = 1625,
    /// <summary>
    /// The system color for the alternate button face, read as an RGB color.
    /// </summary>
    ButtonAlternateFace = 1626,
    /// <summary>
    /// The system color for hot-tracked items, read as an RGB color.
    /// </summary>
    HotTracking = 1627,
    /// <summary>
    /// The system color for the second gradient stop of the active caption, read as an RGB color.
    /// </summary>
    GradientActiveCaption = 1628,
    /// <summary>
    /// The system color for the second gradient stop of the inactive caption, read as an RGB color.
    /// </summary>
    GradientInactiveCaption = 1629,
    /// <summary>
    /// The system color for highlighted menu items when menus are flat, read as an RGB color.
    /// </summary>
    MenuHilight = 1630,
    /// <summary>
    /// The system color for the menu bar background when menus are flat, read as an RGB color.
    /// </summary>
    MenuBar = 1631,
    /// <summary>
    /// Hue 1 that a color scheme replaces, read as an integer.
    /// </summary>
    FromHue1 = 1801,
    /// <summary>
    /// Hue 2 that a color scheme replaces, read as an integer.
    /// </summary>
    FromHue2 = 1802,
    /// <summary>
    /// Hue 3 that a color scheme replaces, read as an integer.
    /// </summary>
    FromHue3 = 1803,
    /// <summary>
    /// Hue 4 that a color scheme replaces, read as an integer.
    /// </summary>
    FromHue4 = 1804,
    /// <summary>
    /// Hue 5 that a color scheme replaces, read as an integer.
    /// </summary>
    FromHue5 = 1805,
    /// <summary>
    /// The hue that replaces <see cref="FromHue1"/>, read as an integer.
    /// </summary>
    ToHue1 = 1806,
    /// <summary>
    /// The hue that replaces <see cref="FromHue2"/>, read as an integer.
    /// </summary>
    ToHue2 = 1807,
    /// <summary>
    /// The hue that replaces <see cref="FromHue3"/>, read as an integer.
    /// </summary>
    ToHue3 = 1808,
    /// <summary>
    /// The hue that replaces <see cref="FromHue4"/>, read as an integer.
    /// </summary>
    ToHue4 = 1809,
    /// <summary>
    /// The hue that replaces <see cref="FromHue5"/>, read as an integer.
    /// </summary>
    ToHue5 = 1810,
    /// <summary>
    /// Color 1 that a color scheme replaces, read as an RGB color.
    /// </summary>
    FromColor1 = 2001,
    /// <summary>
    /// Color 2 that a color scheme replaces, read as an RGB color.
    /// </summary>
    FromColor2 = 2002,
    /// <summary>
    /// Color 3 that a color scheme replaces, read as an RGB color.
    /// </summary>
    FromColor3 = 2003,
    /// <summary>
    /// Color 4 that a color scheme replaces, read as an RGB color.
    /// </summary>
    FromColor4 = 2004,
    /// <summary>
    /// Color 5 that a color scheme replaces, read as an RGB color.
    /// </summary>
    FromColor5 = 2005,
    /// <summary>
    /// The color that replaces <see cref="FromColor1"/>, read as an RGB color.
    /// </summary>
    ToColor1 = 2006,
    /// <summary>
    /// The color that replaces <see cref="FromColor2"/>, read as an RGB color.
    /// </summary>
    ToColor2 = 2007,
    /// <summary>
    /// The color that replaces <see cref="FromColor3"/>, read as an RGB color.
    /// </summary>
    ToColor3 = 2008,
    /// <summary>
    /// The color that replaces <see cref="FromColor4"/>, read as an RGB color.
    /// </summary>
    ToColor4 = 2009,
    /// <summary>
    /// The color that replaces <see cref="FromColor5"/>, read as an RGB color.
    /// </summary>
    ToColor5 = 2010,
    /// <summary>
    /// Turns on the color key for an image without alpha, read as a boolean. <see cref="TransparentColor"/> gives the key.
    /// </summary>
    Transparent = 2201,
    /// <summary>
    /// Sizes the part to its content, read as a boolean.
    /// </summary>
    AutoSize = 2202,
    /// <summary>
    /// Skips the center cell of a nine-grid image background, read as a boolean.
    /// </summary>
    BorderOnly = 2203,
    /// <summary>
    /// Marks the part as drawn by the compositor, read as a boolean.
    /// </summary>
    Composited = 2204,
    /// <summary>
    /// Fills the background behind the image, read as a boolean.
    /// </summary>
    BgFill = 2205,
    /// <summary>
    /// Turns on the color key for a glyph image without alpha, read as a boolean. <see cref="GlyphTransparentColor"/> gives the key.
    /// </summary>
    GlyphTransparent = 2206,
    /// <summary>
    /// Draws only the glyph and no background, read as a boolean.
    /// </summary>
    GlyphOnly = 2207,
    /// <summary>
    /// Always shows the sizing bar, read as a boolean.
    /// </summary>
    AlwaysShowSizingBar = 2208,
    /// <summary>
    /// Lets the image mirror in right-to-left layouts, read as a boolean.
    /// </summary>
    MirrorImage = 2209,
    /// <summary>
    /// Keeps the aspect ratio when a true-size image scales, read as a boolean.
    /// </summary>
    UniformSizing = 2210,
    /// <summary>
    /// Scales the image only by whole multiples, read as a boolean.
    /// </summary>
    IntegralSizing = 2211,
    /// <summary>
    /// Lets the source image grow to fit, read as a boolean.
    /// </summary>
    SourceGrow = 2212,
    /// <summary>
    /// Lets the source image shrink to fit, read as a boolean.
    /// </summary>
    SourceShrink = 2213,
    /// <summary>
    /// Draws the part borders. No reader or renderer in this library uses it.
    /// </summary>
    DrawBorders = 2214,
    /// <summary>
    /// Turns off the etched effect on disabled text. No reader or renderer in this library uses it.
    /// </summary>
    NoEtchedEffect = 2215,
    /// <summary>
    /// Applies an overlay to the text. No reader or renderer in this library uses it.
    /// </summary>
    TextApplyOverlay = 2216,
    /// <summary>
    /// Turns on a glow behind the text. No reader or renderer in this library uses it.
    /// </summary>
    TextGlow = 2217,
    /// <summary>
    /// Draws the text in italics. No reader or renderer in this library uses it.
    /// </summary>
    TextItalic = 2218,
    /// <summary>
    /// Marks a composited part as opaque. No reader or renderer in this library uses it.
    /// </summary>
    CompositedOpaque = 2219,
    /// <summary>
    /// Mirrors the image only in localized right-to-left layouts. No reader or renderer in this library uses it.
    /// </summary>
    LocalizedMirrorImage = 2220,
    /// <summary>
    /// The number of frames in a part image strip, read as an integer. The renderer picks the frame by state or animation frame.
    /// </summary>
    ImageCount = 2401,
    /// <summary>
    /// The alpha level of the part, read as an integer.
    /// </summary>
    AlphaLevel = 2402,
    /// <summary>
    /// The border line width of a border-fill background, read as an integer. The renderer uses 1 when it is absent.
    /// </summary>
    BorderSize = 2403,
    /// <summary>
    /// The corner width of a <see cref="BorderType.RoundRect"/> border, read as an integer. The renderer uses half of it as the radius.
    /// </summary>
    RoundCornerWidth = 2404,
    /// <summary>
    /// The corner height of a round rectangle border, read as an integer.
    /// </summary>
    RoundCornerHeight = 2405,
    /// <summary>
    /// The position of <see cref="GradientColor1"/> along a gradient fill, read as an integer.
    /// </summary>
    GradientRatio1 = 2406,
    /// <summary>
    /// The position of <see cref="GradientColor2"/> along a gradient fill, read as an integer.
    /// </summary>
    GradientRatio2 = 2407,
    /// <summary>
    /// The position of <see cref="GradientColor3"/> along a gradient fill, read as an integer.
    /// </summary>
    GradientRatio3 = 2408,
    /// <summary>
    /// The position of <see cref="GradientColor4"/> along a gradient fill, read as an integer.
    /// </summary>
    GradientRatio4 = 2409,
    /// <summary>
    /// The position of <see cref="GradientColor5"/> along a gradient fill, read as an integer.
    /// </summary>
    GradientRatio5 = 2410,
    /// <summary>
    /// The size of one progress bar chunk, read as an integer.
    /// </summary>
    ProgressChunkSize = 2411,
    /// <summary>
    /// The space between progress bar chunks, read as an integer.
    /// </summary>
    ProgressSpaceSize = 2412,
    /// <summary>
    /// The saturation adjustment of the part, read as an integer.
    /// </summary>
    Saturation = 2413,
    /// <summary>
    /// The width of the border around text, read as an integer.
    /// </summary>
    TextBorderSize = 2414,
    /// <summary>
    /// The alpha value below which a pixel counts as transparent, read as an integer.
    /// </summary>
    AlphaThreshold = 2415,
    /// <summary>
    /// The fixed width of the part, read as an integer size.
    /// </summary>
    Width = 2416,
    /// <summary>
    /// The fixed height of the part, read as an integer size.
    /// </summary>
    Height = 2417,
    /// <summary>
    /// The character code of a font glyph, read as an integer. The renderer draws it in <see cref="GlyphFont"/>.
    /// </summary>
    GlyphIndex = 2418,
    /// <summary>
    /// The percent by which the destination must exceed a size-scaled true-size image before it stretches, read as an integer.
    /// </summary>
    TrueSizeStretchMark = 2419,
    /// <summary>
    /// The lowest DPI at which <see cref="ImageFile1"/> applies, read as an integer.
    /// </summary>
    MinDpi1 = 2420,
    /// <summary>
    /// The lowest DPI at which <see cref="ImageFile2"/> applies, read as an integer.
    /// </summary>
    MinDpi2 = 2421,
    /// <summary>
    /// The lowest DPI at which <see cref="ImageFile3"/> applies, read as an integer.
    /// </summary>
    MinDpi3 = 2422,
    /// <summary>
    /// The lowest DPI at which <see cref="ImageFile4"/> applies, read as an integer.
    /// </summary>
    MinDpi4 = 2423,
    /// <summary>
    /// The lowest DPI at which <see cref="ImageFile5"/> applies, read as an integer.
    /// </summary>
    MinDpi5 = 2424,
    /// <summary>
    /// The radius of the glow behind glass caption text, read as an integer. The frame painter draws no glow when it is zero.
    /// </summary>
    TextGlowSize = 2425,
    /// <summary>
    /// The frame rate of the part animation. No reader or renderer in this library uses it.
    /// </summary>
    FramesPerSecond = 2426,
    /// <summary>
    /// The distance the part animation moves per frame. No reader or renderer in this library uses it.
    /// </summary>
    PixelsPerFrame = 2427,
    /// <summary>
    /// The delay before the part animation starts. No reader or renderer in this library uses it.
    /// </summary>
    AnimationDelay = 2428,
    /// <summary>
    /// The strength of the caption text glow as a percent, read as an integer. The frame painter uses 100 when it is absent.
    /// </summary>
    GlowIntensity = 2429,
    /// <summary>
    /// The opacity of a DWM window part as a percent, read as an integer. The frame style uses 100 when it is absent.
    /// </summary>
    Opacity = 2430,
    /// <summary>
    /// The DWM colorization color. No reader or renderer in this library uses it.
    /// </summary>
    ColorizationColor = 2431,
    /// <summary>
    /// The strength of the DWM colorization as a percent, read as an integer. The frame style uses 100 when it is absent.
    /// </summary>
    ColorizationOpacity = 2432,
    /// <summary>
    /// The lowest DPI at which <see cref="ImageFile6"/> applies, read as an integer.
    /// </summary>
    MinDpi6 = 2433,
    /// <summary>
    /// The lowest DPI at which <see cref="ImageFile7"/> applies, read as an integer.
    /// </summary>
    MinDpi7 = 2434,
    /// <summary>
    /// The font of a font glyph, read as a font. <see cref="GlyphIndex"/> gives the character.
    /// </summary>
    GlyphFont = 2601,
    /// <summary>
    /// The main part image, read as an image resource. The renderer uses it when no image selection applies.
    /// </summary>
    ImageFile = 3001,
    /// <summary>
    /// Image 1 of the DPI or size choices that <see cref="ImageSelectType"/> selects among, read as an image resource.
    /// </summary>
    ImageFile1 = 3002,
    /// <summary>
    /// Image 2 of the DPI or size choices that <see cref="ImageSelectType"/> selects among, read as an image resource.
    /// </summary>
    ImageFile2 = 3003,
    /// <summary>
    /// Image 3 of the DPI or size choices that <see cref="ImageSelectType"/> selects among, read as an image resource.
    /// </summary>
    ImageFile3 = 3004,
    /// <summary>
    /// Image 4 of the DPI or size choices that <see cref="ImageSelectType"/> selects among, read as an image resource.
    /// </summary>
    ImageFile4 = 3005,
    /// <summary>
    /// Image 5 of the DPI or size choices that <see cref="ImageSelectType"/> selects among, read as an image resource.
    /// </summary>
    ImageFile5 = 3006,
    /// <summary>
    /// A stock image of the part, read as an image resource.
    /// </summary>
    StockImageFile = 3007,
    /// <summary>
    /// The image of an image glyph, read as an image resource.
    /// </summary>
    GlyphImageFile = 3008,
    /// <summary>
    /// Image 6 of the DPI or size choices that <see cref="ImageSelectType"/> selects among, read as an image resource.
    /// </summary>
    ImageFile6 = 3009,
    /// <summary>
    /// Image 7 of the DPI or size choices that <see cref="ImageSelectType"/> selects among, read as an image resource.
    /// </summary>
    ImageFile7 = 3010,
    /// <summary>
    /// The text of the part, read as a string.
    /// </summary>
    Text = 3201,
    /// <summary>
    /// The value for the classic, unthemed look. No reader or renderer in this library uses it.
    /// </summary>
    ClassicValue = 3202,
    /// <summary>
    /// The offset of the part from the point that <see cref="OffsetType"/> names, read as an x and y pair.
    /// </summary>
    Offset = 3401,
    /// <summary>
    /// The offset of the caption text shadow, read as an x and y pair. The frame painter uses (1, 1) when it is absent.
    /// </summary>
    TextShadowOffset = 3402,
    /// <summary>
    /// The minimum size of the part, read as an x and y pair.
    /// </summary>
    MinSize = 3403,
    /// <summary>
    /// The smallest destination size at which <see cref="ImageFile1"/> applies, read as an x and y pair.
    /// </summary>
    MinSize1 = 3404,
    /// <summary>
    /// The smallest destination size at which <see cref="ImageFile2"/> applies, read as an x and y pair.
    /// </summary>
    MinSize2 = 3405,
    /// <summary>
    /// The smallest destination size at which <see cref="ImageFile3"/> applies, read as an x and y pair.
    /// </summary>
    MinSize3 = 3406,
    /// <summary>
    /// The smallest destination size at which <see cref="ImageFile4"/> applies, read as an x and y pair.
    /// </summary>
    MinSize4 = 3407,
    /// <summary>
    /// The smallest destination size at which <see cref="ImageFile5"/> applies, read as an x and y pair.
    /// </summary>
    MinSize5 = 3408,
    /// <summary>
    /// The normal size of the part, read as an x and y pair.
    /// </summary>
    NormalSize = 3409,
    /// <summary>
    /// The smallest destination size at which <see cref="ImageFile6"/> applies, read as an x and y pair.
    /// </summary>
    MinSize6 = 3410,
    /// <summary>
    /// The smallest destination size at which <see cref="ImageFile7"/> applies, read as an x and y pair.
    /// </summary>
    MinSize7 = 3411,
    /// <summary>
    /// The nine-grid margins that keep the image edges unscaled, read as four integers in 96-DPI pixels.
    /// </summary>
    SizingMargins = 3601,
    /// <summary>
    /// The margins between the part edge and its content, read as four integers in 96-DPI pixels.
    /// </summary>
    ContentMargins = 3602,
    /// <summary>
    /// The margins around the caption text, read as four integers.
    /// </summary>
    CaptionMargins = 3603,
    /// <summary>
    /// The border line color of a border-fill background, read as an RGB color. The renderer uses black when it is absent.
    /// </summary>
    BorderColor = 3801,
    /// <summary>
    /// The solid fill color of a border-fill background, read as an RGB color. The renderer uses white when it is absent.
    /// </summary>
    FillColor = 3802,
    /// <summary>
    /// The text color of the part, read as an RGB color. The frame painter ignores a value that only the globals class sets.
    /// </summary>
    TextColor = 3803,
    /// <summary>
    /// The light edge color of a 3D edge, read as an RGB color.
    /// </summary>
    EdgeLightColor = 3804,
    /// <summary>
    /// The highlight color of a 3D edge, read as an RGB color.
    /// </summary>
    EdgeHighlightColor = 3805,
    /// <summary>
    /// The shadow color of a 3D edge, read as an RGB color.
    /// </summary>
    EdgeShadowColor = 3806,
    /// <summary>
    /// The dark shadow color of a 3D edge, read as an RGB color.
    /// </summary>
    EdgeDkShadowColor = 3807,
    /// <summary>
    /// The fill color inside a 3D edge, read as an RGB color.
    /// </summary>
    EdgeFillColor = 3808,
    /// <summary>
    /// The color key for <see cref="Transparent"/>, read as an RGB color. The image cache has a default key when it is absent.
    /// </summary>
    TransparentColor = 3809,
    /// <summary>
    /// Stop 1 of a gradient fill, read as an RGB color.
    /// </summary>
    GradientColor1 = 3810,
    /// <summary>
    /// Stop 2 of a gradient fill, read as an RGB color.
    /// </summary>
    GradientColor2 = 3811,
    /// <summary>
    /// Stop 3 of a gradient fill, read as an RGB color.
    /// </summary>
    GradientColor3 = 3812,
    /// <summary>
    /// Stop 4 of a gradient fill, read as an RGB color.
    /// </summary>
    GradientColor4 = 3813,
    /// <summary>
    /// Stop 5 of a gradient fill, read as an RGB color.
    /// </summary>
    GradientColor5 = 3814,
    /// <summary>
    /// The shadow color of the part, read as an RGB color.
    /// </summary>
    ShadowColor = 3815,
    /// <summary>
    /// The color of the caption text glow, read as an RGB color. The frame painter uses white when it is absent.
    /// </summary>
    GlowColor = 3816,
    /// <summary>
    /// The color of the border around text, read as an RGB color.
    /// </summary>
    TextBorderColor = 3817,
    /// <summary>
    /// The color of the caption text shadow, read as an RGB color.
    /// </summary>
    TextShadowColor = 3818,
    /// <summary>
    /// The color of a font glyph, read as an RGB color. The renderer uses black when it is absent.
    /// </summary>
    GlyphTextColor = 3819,
    /// <summary>
    /// The color key for <see cref="GlyphTransparent"/>, read as an RGB color.
    /// </summary>
    GlyphTransparentColor = 3820,
    /// <summary>
    /// A hint of the main fill color for other drawing, read as an RGB color.
    /// </summary>
    FillColorHint = 3821,
    /// <summary>
    /// A hint of the main border color for other drawing, read as an RGB color.
    /// </summary>
    BorderColorHint = 3822,
    /// <summary>
    /// A hint of the accent color for other drawing, read as an RGB color.
    /// </summary>
    AccentColorHint = 3823,
    /// <summary>
    /// A hint of the text color for other drawing. The name table does not map it.
    /// </summary>
    TextColorHint = 3824,
    /// <summary>
    /// The text color of first-level headings. The name table does not map it.
    /// </summary>
    Heading1TextColor = 3825,
    /// <summary>
    /// The text color of second-level headings. The name table does not map it.
    /// </summary>
    Heading2TextColor = 3826,
    /// <summary>
    /// The text color of body text. The name table does not map it.
    /// </summary>
    BodyTextColor = 3827,
    /// <summary>
    /// Selects an image, border-fill or no background, read as a <see cref="BackgroundType"/>. The renderer uses border-fill when it is absent.
    /// </summary>
    BgType = 4001,
    /// <summary>
    /// Selects the shape of a border-fill background, read as a <see cref="Basin.Themes.MsStyles.BorderType"/>.
    /// </summary>
    BorderType = 4002,
    /// <summary>
    /// Selects a solid or gradient fill, read as a <see cref="Basin.Themes.MsStyles.FillType"/>. The renderer reports the tile-image fill as not implemented.
    /// </summary>
    FillType = 4003,
    /// <summary>
    /// Selects how an image background fills the part, read as a <see cref="Basin.Themes.MsStyles.SizingType"/>. The renderer stretches when it is absent.
    /// </summary>
    SizingType = 4004,
    /// <summary>
    /// The horizontal alignment of a true-size image or a glyph, read as a <see cref="HorizontalAlign"/>.
    /// </summary>
    HAlign = 4005,
    /// <summary>
    /// The horizontal alignment of the caption text, read as a <see cref="Basin.Themes.MsStyles.ContentAlignment"/>.
    /// </summary>
    ContentAlignment = 4006,
    /// <summary>
    /// The vertical alignment of a true-size image or a glyph, read as a <see cref="VerticalAlign"/>.
    /// </summary>
    VAlign = 4007,
    /// <summary>
    /// The anchor point that <see cref="Offset"/> is relative to, read as a <see cref="Basin.Themes.MsStyles.OffsetType"/>.
    /// </summary>
    OffsetType = 4008,
    /// <summary>
    /// The effect applied to an icon, read as a <see cref="Basin.Themes.MsStyles.IconEffect"/>.
    /// </summary>
    IconEffect = 4009,
    /// <summary>
    /// Selects the caption text shadow, read as a <see cref="Basin.Themes.MsStyles.TextShadowType"/>. The frame painter draws only the single shadow.
    /// </summary>
    TextShadowType = 4010,
    /// <summary>
    /// Whether the frames of an image strip run across or down, read as a <see cref="Basin.Themes.MsStyles.ImageLayout"/>.
    /// </summary>
    ImageLayout = 4011,
    /// <summary>
    /// Selects no glyph, an image glyph or a font glyph, read as a <see cref="Basin.Themes.MsStyles.GlyphType"/>.
    /// </summary>
    GlyphType = 4012,
    /// <summary>
    /// Selects the image choice by DPI or by size, read as a <see cref="Basin.Themes.MsStyles.ImageSelectType"/>.
    /// </summary>
    ImageSelectType = 4013,
    /// <summary>
    /// Selects how a font glyph scales, read as a <see cref="Basin.Themes.MsStyles.GlyphFontSizingType"/>.
    /// </summary>
    GlyphFontSizingType = 4014,
    /// <summary>
    /// Selects when a true-size image can scale up, read as a <see cref="Basin.Themes.MsStyles.TrueSizeScalingType"/>.
    /// </summary>
    TrueSizeScalingType = 4015,
    /// <summary>
    /// Marks the part as a user picture, read as a boolean.
    /// </summary>
    UserPicture = 5001,
    /// <summary>
    /// The default size of a pane, read as four integers.
    /// </summary>
    DefaultPaneSize = 5002,
    /// <summary>
    /// The color blended over the part, read as an RGB color.
    /// </summary>
    BlendColor = 5003,
    /// <summary>
    /// A custom split rectangle. No reader or renderer in this library uses it.
    /// </summary>
    CustomSplitRect = 5004,
    /// <summary>
    /// The rectangle of an animated button. No reader or renderer in this library uses it.
    /// </summary>
    AnimationButtonRect = 5005,
    /// <summary>
    /// The length of the part animation. No reader or renderer in this library uses it.
    /// </summary>
    AnimationDuration = 5006,
    /// <summary>
    /// The state-to-state transition times, read as an integer list.
    /// </summary>
    TransitionDurations = 6000,
    /// <summary>
    /// A background that scales with the part. No reader or renderer in this library uses it.
    /// </summary>
    ScaledBackground = 7001,
    /// <summary>
    /// The atlas image that holds many parts, read as an image. <see cref="AtlasRect"/> gives the part region.
    /// </summary>
    AtlasImage = 8000,
    /// <summary>
    /// The source image that built the atlas. No reader or renderer in this library uses it.
    /// </summary>
    AtlasInputImage = 8001,
    /// <summary>
    /// The region of the part inside <see cref="AtlasImage"/>, read as a rectangle.
    /// </summary>
    AtlasRect = 8002,
}
