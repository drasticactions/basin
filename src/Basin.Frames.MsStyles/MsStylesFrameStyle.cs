using Basin.Themes.MsStyles;

namespace Basin.Frames.MsStyles;

/// <summary>
/// The parts of an msstyles frame that every window shares: the theme, the look, the image cache and the metrics read from the theme.
/// </summary>
public sealed class MsStylesFrameStyle
{
    /// <summary>
    /// The default glass colorization as 0xAARRGGBB, a light blue at an alpha of 0x45.
    /// </summary>
    public const uint DefaultColorization = 0x45409efe;

    /// <summary>
    /// The window icon size in logical pixels.
    /// </summary>
    public const int IconSize = 16;

    private const int DefaultCaptionBar = 18;
    private const int DefaultSmallCaptionBar = 15;

    /// <summary>
    /// Initializes a new instance of the <see cref="MsStylesFrameStyle"/> class and reads the frame metrics from the theme.
    /// </summary>
    /// <param name="theme">The loaded theme. The style does not own it.</param>
    /// <param name="look">The look to draw. <see cref="MsStylesLook.Auto"/> picks one from the theme format.</param>
    /// <param name="colorization">The glass colorization as 0xAARRGGBB. Only the glass look uses it.</param>
    /// <exception cref="ArgumentException">The theme has no Window class, or <paramref name="look"/> is glass and the theme is format 3 or has no DWMWindow class.</exception>
    public MsStylesFrameStyle(MsStylesTheme theme, MsStylesLook look = MsStylesLook.Auto, uint colorization = DefaultColorization)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
        Window = theme.OpenClass("Window") ?? throw new ArgumentException("The theme has no Window class, so it cannot draw a frame.", nameof(theme));
        DwmWindow = theme.OpenClass("DWMWindow");
        Look = look switch
        {
            MsStylesLook.Auto => theme.Format == 3 ? MsStylesLook.ClassicXp : DwmWindow is not null ? MsStylesLook.Glass : MsStylesLook.Basic,
            MsStylesLook.Glass when theme.Format == 3 => throw new ArgumentException("look = glass needs a Vista or later theme (PACKTHEM 4); this one is PACKTHEM 3."),
            MsStylesLook.Glass when DwmWindow is null => throw new ArgumentException("look = glass needs the DWMWindow class, and this theme has none."),
            _ => look,
        };
        Colorization = colorization;

        var metrics = theme.SysMetrics;
        var sizing = metrics.TryGetSize(SysSize.SizingBorderWidth, out var sizingBorder) ? sizingBorder : 1;
        var padded = theme.Format >= 4 && metrics.TryGetSize(SysSize.PaddedBorderWidth, out var paddedBorder) ? paddedBorder : 0;
        Border = Math.Max(0, sizing) + Math.Max(0, padded) + 3;
        CaptionBar = metrics.TryGetSize(SysSize.CaptionBarHeight, out var captionBar) && captionBar > 0 ? captionBar : DefaultCaptionBar;
        SmallCaptionBar = metrics.TryGetSize(SysSize.SMCaptionBarHeight, out var smallBar) && smallBar > 0 ? smallBar : DefaultSmallCaptionBar;
        ButtonHeight = Math.Max(1, CaptionBar - 4);
        ButtonWidth = theme.Format >= 4
            ? VistaButtonMetrics.BasicWidth
            : Math.Max(1, (metrics.TryGetSize(SysSize.CaptionBarWidth, out var barWidth) && barWidth > 0 ? barWidth : CaptionBar) - 4);
        SmallButtonHeight = Math.Max(1, SmallCaptionBar - 4);
        SmallButtonWidth = Math.Max(1, (metrics.TryGetSize(SysSize.SMCaptionBarWidth, out var smallWidth) && smallWidth > 0 ? smallWidth : SmallCaptionBar) - 4);

        CaptionFont = metrics.TryGetFont(SysFont.CaptionFont, out var captionFont)
            ? captionFont
            : new MsStylesFont("Tahoma", 8, MsStylesFont.Bold, false, false, false);
        SmallCaptionFont = metrics.TryGetFont(SysFont.SmallCaptionFont, out var smallFont) ? smallFont : CaptionFont;
        CompositedWindow = theme.OpenClass("Window", "CompositedWindow") ?? Window;
        CaptionText = metrics.TryGetColor(SysColor.CaptionText, out var text) ? text : new MsStylesColor(255, 255, 255);
        InactiveCaptionText = metrics.TryGetColor(SysColor.InactiveCaptionText, out var inactive) ? inactive : new MsStylesColor(216, 228, 248);
    }

    /// <summary>
    /// Gets the theme that the style reads.
    /// </summary>
    public MsStylesTheme Theme { get; }

    /// <summary>
    /// Gets the resolved look. It is never <see cref="MsStylesLook.Auto"/>.
    /// </summary>
    public MsStylesLook Look { get; }

    /// <summary>
    /// Gets the glass colorization as 0xAARRGGBB.
    /// </summary>
    public uint Colorization { get; }

    /// <summary>
    /// Gets the theme's Window class, which draws the classic-xp and basic looks.
    /// </summary>
    public MsStylesClass Window { get; }

    /// <summary>
    /// Gets the theme's DWMWindow class, or <see langword="null"/> when the theme has none.
    /// </summary>
    public MsStylesClass? DwmWindow { get; }

    /// <summary>
    /// Gets the CompositedWindow::Window class, which sets the glass title text. It is <see cref="Window"/> when the theme has no such class.
    /// </summary>
    public MsStylesClass CompositedWindow { get; }

    /// <summary>
    /// Gets a value indicating whether the look is <see cref="MsStylesLook.Glass"/>.
    /// </summary>
    public bool IsGlass => Look == MsStylesLook.Glass;

    /// <summary>
    /// Computes the alpha of the colorization fill for a DWMWindow part and state.
    /// </summary>
    /// <param name="part">The DWMWindow part number.</param>
    /// <param name="state">The DWMWindow state number.</param>
    /// <returns>The colorization alpha times the part's <c>COLORIZATIONOPACITY</c> percent, from 0 to 1. A missing value counts as 100 percent.</returns>
    public float ColorizationAlpha(int part, int state)
    {
        var strength = DwmWindow is { } dwm && dwm.TryGetInt(part, state, Basin.Themes.MsStyles.ThemeProperty.ColorizationOpacity, out var opacity) ? opacity : 100;
        return (Colorization >> 24) / 255f * Math.Clamp(strength, 0, 100) / 100f;
    }

    /// <summary>
    /// Reads the opacity at which DWM draws a DWMWindow part in a state.
    /// </summary>
    /// <param name="part">The DWMWindow part number.</param>
    /// <param name="state">The DWMWindow state number.</param>
    /// <returns>The part's <c>OPACITY</c> percent as a value from 0 to 1, or 1 when the theme sets none.</returns>
    public float PartOpacity(int part, int state)
    {
        var opacity = DwmWindow is { } dwm && dwm.TryGetInt(part, state, Basin.Themes.MsStyles.ThemeProperty.Opacity, out var value) ? value : 100;
        return Math.Clamp(opacity, 0, 100) / 100f;
    }

    /// <summary>
    /// Gets the image cache that every window of this style shares.
    /// </summary>
    public MsStylesImageCache Images { get; } = new();

    /// <summary>
    /// Gets the frame border width in logical pixels, the sizing border plus the padded border plus 3.
    /// </summary>
    public int Border { get; }

    /// <summary>
    /// Gets the caption bar height in logical pixels from the theme's system metrics, or 18 when the theme sets none.
    /// </summary>
    public int CaptionBar { get; }

    /// <summary>
    /// Gets the utility caption bar height in logical pixels from the theme's system metrics, or 15 when the theme sets none.
    /// </summary>
    public int SmallCaptionBar { get; }

    /// <summary>
    /// Gets the caption button width in logical pixels. It is 28 for a format 4 theme, and the caption bar width less 4 for format 3.
    /// </summary>
    public int ButtonWidth { get; }

    /// <summary>
    /// Gets the caption button height in logical pixels, the caption bar less 4.
    /// </summary>
    public int ButtonHeight { get; }

    /// <summary>
    /// Gets the utility caption button width in logical pixels, the small caption bar width less 4.
    /// </summary>
    public int SmallButtonWidth { get; }

    /// <summary>
    /// Gets the utility caption button height in logical pixels, the small caption bar less 4.
    /// </summary>
    public int SmallButtonHeight { get; }

    /// <summary>
    /// Gets the title font from the theme's system metrics, or Tahoma 8 bold when the theme sets none.
    /// </summary>
    public MsStylesFont CaptionFont { get; }

    /// <summary>
    /// Gets the utility window title font. It is <see cref="CaptionFont"/> when the theme sets none.
    /// </summary>
    public MsStylesFont SmallCaptionFont { get; }

    /// <summary>
    /// Gets the system title color of an active window, or white when the theme sets none.
    /// </summary>
    public MsStylesColor CaptionText { get; }

    /// <summary>
    /// Gets the system title color of an inactive window, with a light blue default.
    /// </summary>
    public MsStylesColor InactiveCaptionText { get; }

    /// <summary>
    /// Picks the Window part that draws the caption. A part that the theme does not have falls back to the normal caption.
    /// </summary>
    /// <param name="small">Whether the window is a utility window.</param>
    /// <param name="maximized">Whether the window is maximized.</param>
    /// <returns>The part number of the small, maximized or normal caption.</returns>
    public int CaptionPart(bool small, bool maximized)
    {
        if (small && Window.HasPart((int)WindowPart.SmallCaption))
        {
            return (int)WindowPart.SmallCaption;
        }

        if (maximized && Window.HasPart((int)WindowPart.MaxCaption))
        {
            return (int)WindowPart.MaxCaption;
        }

        return (int)WindowPart.Caption;
    }

    /// <summary>
    /// Picks the Window part for a frame edge, the small part when it applies and the theme has it.
    /// </summary>
    /// <param name="normal">The part for a normal window.</param>
    /// <param name="small">The part for a utility window.</param>
    /// <param name="isSmall">Whether the window is a utility window.</param>
    /// <returns>The part number to draw.</returns>
    public int FramePart(WindowPart normal, WindowPart small, bool isSmall) =>
        isSmall && Window.HasPart((int)small) ? (int)small : (int)normal;
}
