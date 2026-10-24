using Prowl.Scribe;
using Prowl.Vector;

namespace Basin.Frames.Quill;

/// <summary>
/// The sizes, colors and modes of a Quill frame look. The default instance is the dark opaque look. Sizes are logical pixels.
/// </summary>
public sealed class QuillFrameTheme
{
    /// <summary>
    /// Gets or sets the font for the title and the menu. The renderer reads it on each draw. When it is <see langword="null"/>, the renderer uses <see cref="QuillFrameFonts.Bundled"/>.
    /// </summary>
    public FontFile? Face { get; set; }

    /// <summary>
    /// Gets or sets the font size of the title and the menu items. The default is 13.
    /// </summary>
    public float FontSize { get; set; } = 13f;

    /// <summary>
    /// Gets or sets the height of the title bar below the top border. The default is 30.
    /// </summary>
    public int TitleHeight { get; set; } = 30;

    /// <summary>
    /// Gets or sets the thickness of each border. The default is 4.
    /// </summary>
    public int Border { get; set; } = 4;

    /// <summary>
    /// Gets or sets the diameter of each title-bar button. The default is 18.
    /// </summary>
    public int ButtonSize { get; set; } = 18;

    /// <summary>
    /// Gets or sets the space between the title-bar buttons, and around the icon and the title. The default is 8.
    /// </summary>
    public int ButtonGap { get; set; } = 8;

    /// <summary>
    /// Gets or sets a value indicating whether the title bar shows the shade, always-on-top and all-workspaces toggles that the window allows. The default is <see langword="false"/>.
    /// </summary>
    public bool Toggles { get; set; }

    /// <summary>
    /// Gets or sets the side of the window icon or menu button. The default is 18.
    /// </summary>
    public int IconSize { get; set; } = 18;

    /// <summary>
    /// Gets or sets the length along each edge, from a corner, where a border press resizes from the corner. The default is 16.
    /// </summary>
    public int CornerZone { get; set; } = 16;

    /// <summary>
    /// Gets or sets the radius of the outer frame corners. A value of 0 gives square corners. The default is 8.
    /// </summary>
    public float CornerRadius { get; set; } = 8f;

    /// <summary>
    /// Gets or sets a value indicating whether the frame fills are translucent with <see cref="FrostAlpha"/>, and ask for a backdrop effect under the frame. <see cref="Glass"/> takes precedence.
    /// </summary>
    public bool Frosted { get; set; }

    /// <summary>
    /// Gets or sets the alpha of the frame fills when <see cref="Frosted"/> is set. The default is 0x70.
    /// </summary>
    public byte FrostAlpha { get; set; } = 0x70;

    /// <summary>
    /// Gets or sets a value indicating whether the frame draws only the outline, buttons and title, and publishes a glass backdrop shape. Glass buttons and focus changes fade.
    /// </summary>
    public bool Glass { get; set; }

    /// <summary>
    /// Gets or sets the width of the glass edge band in the backdrop shape. The default is 10.
    /// </summary>
    public float Bevel { get; set; } = 10f;

    /// <summary>
    /// Gets or sets the length of the glass fades and of the highlight follow, in milliseconds. A value under 1 counts as 1. The default is 150.
    /// </summary>
    public int AnimationMs { get; set; } = 150;

    /// <summary>
    /// Gets or sets the corner radius of the client hole in the glass shape. A negative value, the default, derives it from the corner radius and the border.
    /// </summary>
    public float ContentRadius { get; set; } = -1f;

    /// <summary>
    /// Gets the client hole radius in use: <see cref="ContentRadius"/> when it is 0 or more, otherwise <see cref="CornerRadius"/> less <see cref="Border"/>, and never under 0.
    /// </summary>
    public float ResolvedContentRadius => ContentRadius >= 0f ? ContentRadius : Math.Max(0f, CornerRadius - Border);

    /// <summary>
    /// Gets or sets the top color of the title-bar gradient of an active window.
    /// </summary>
    public Color32 TitleTop { get; set; } = new(0x3B, 0x41, 0x4D, 0xFF);

    /// <summary>
    /// Gets or sets the bottom color of the title-bar gradient of an active window.
    /// </summary>
    public Color32 TitleBottom { get; set; } = new(0x24, 0x28, 0x31, 0xFF);

    /// <summary>
    /// Gets or sets the top color of the title-bar gradient of an inactive window.
    /// </summary>
    public Color32 TitleTopInactive { get; set; } = new(0x24, 0x27, 0x2D, 0xFF);

    /// <summary>
    /// Gets or sets the bottom color of the title-bar gradient of an inactive window.
    /// </summary>
    public Color32 TitleBottomInactive { get; set; } = new(0x1A, 0x1C, 0x21, 0xFF);

    /// <summary>
    /// Gets or sets the border fill of an active window.
    /// </summary>
    public Color32 Body { get; set; } = new(0x24, 0x28, 0x31, 0xFF);

    /// <summary>
    /// Gets or sets the border fill of an inactive window.
    /// </summary>
    public Color32 BodyInactive { get; set; } = new(0x1A, 0x1C, 0x21, 0xFF);

    /// <summary>
    /// Gets or sets the one-pixel outline of an active window. The menu outline also uses it.
    /// </summary>
    public Color32 Outline { get; set; } = new(0x70, 0x78, 0x86, 0xFF);

    /// <summary>
    /// Gets or sets the one-pixel outline of an inactive window.
    /// </summary>
    public Color32 OutlineInactive { get; set; } = new(0x3A, 0x3E, 0x47, 0xFF);

    /// <summary>
    /// Gets or sets the title, icon and menu text color of an active window.
    /// </summary>
    public Color32 Text { get; set; } = new(0xF2, 0xF4, 0xF7, 0xFF);

    /// <summary>
    /// Gets or sets the title and icon color of an inactive window.
    /// </summary>
    public Color32 TextInactive { get; set; } = new(0x88, 0x8E, 0x99, 0xFF);

    /// <summary>
    /// Gets or sets the lit color of the close button.
    /// </summary>
    public Color32 Close { get; set; } = new(0xFF, 0x5F, 0x57, 0xFF);

    /// <summary>
    /// Gets or sets the lit color of the maximize button.
    /// </summary>
    public Color32 Maximize { get; set; } = new(0x28, 0xC8, 0x40, 0xFF);

    /// <summary>
    /// Gets or sets the lit color of the minimize button.
    /// </summary>
    public Color32 Minimize { get; set; } = new(0xFE, 0xBC, 0x2E, 0xFF);

    /// <summary>
    /// Gets or sets the lit color of the shade, always-on-top and all-workspaces toggles.
    /// </summary>
    public Color32 Toggle { get; set; } = new(0x7C, 0x8B, 0xA8, 0xFF);

    /// <summary>
    /// Gets or sets the color of a button that is not hovered, pressed or on.
    /// </summary>
    public Color32 Dormant { get; set; } = new(0x4A, 0x4F, 0x59, 0xFF);

    /// <summary>
    /// Gets or sets the color of the mark on a hovered or pressed button.
    /// </summary>
    public Color32 Glyph { get; set; } = new(0x1B, 0x1D, 0x23, 0xFF);

    /// <summary>
    /// Gets or sets the background of the window menu.
    /// </summary>
    public Color32 MenuFill { get; set; } = new(0x1F, 0x22, 0x29, 0xF7);

    /// <summary>
    /// Gets or sets the background of the menu item under the pointer.
    /// </summary>
    public Color32 MenuHot { get; set; } = new(0x3A, 0x41, 0x4E, 0xFF);

    /// <summary>
    /// Gets or sets the width of the window menu. The default is 190.
    /// </summary>
    public int MenuWidth { get; set; } = 190;

    /// <summary>
    /// Gets or sets the height of each window menu item. The default is 28.
    /// </summary>
    public int MenuItemHeight { get; set; } = 28;

    /// <summary>
    /// Gets or sets the space between the menu edge and its items. The default is 6.
    /// </summary>
    public int MenuPadding { get; set; } = 6;

    /// <summary>
    /// Makes a new glass theme with light text and marks, for a dark color scheme.
    /// </summary>
    /// <returns>A new theme that the caller can change.</returns>
    public static QuillFrameTheme DarkGlass() => new()
    {
        Glass = true,
        Border = 6,
        CornerRadius = 12f,
        Outline = new Color32(0xFF, 0xFF, 0xFF, 0x70),
        OutlineInactive = new Color32(0xFF, 0xFF, 0xFF, 0x30),
        Text = new Color32(0xF7, 0xF8, 0xFA, 0xFF),
        TextInactive = new Color32(0xC8, 0xCC, 0xD4, 0xC0),
        Dormant = new Color32(0xFF, 0xFF, 0xFF, 0x48),
        Glyph = new Color32(0x1B, 0x1D, 0x23, 0xFF),
    };

    /// <summary>
    /// Makes a new glass theme with dark text and light marks, for a light color scheme.
    /// </summary>
    /// <returns>A new theme that the caller can change.</returns>
    public static QuillFrameTheme LightGlass() => new()
    {
        Glass = true,
        Border = 6,
        CornerRadius = 12f,
        Outline = new Color32(0xFF, 0xFF, 0xFF, 0xB0),
        OutlineInactive = new Color32(0xFF, 0xFF, 0xFF, 0x60),
        Text = new Color32(0x1B, 0x1D, 0x23, 0xFF),
        TextInactive = new Color32(0x4A, 0x50, 0x5A, 0xC0),
        Toggle = new Color32(0x5B, 0x6B, 0x86, 0xFF),
        Dormant = new Color32(0x1B, 0x1D, 0x23, 0x40),
        Glyph = new Color32(0xF7, 0xF8, 0xFA, 0xFF),
        MenuFill = new Color32(0xF7, 0xF8, 0xFA, 0xF7),
        MenuHot = new Color32(0xD3, 0xDA, 0xE5, 0xFF),
    };

    /// <summary>
    /// Makes a new opaque theme with light fills and dark text.
    /// </summary>
    /// <returns>A new theme that the caller can change.</returns>
    public static QuillFrameTheme Light() => new()
    {
        TitleTop = new Color32(0xFA, 0xFB, 0xFC, 0xFF),
        TitleBottom = new Color32(0xDF, 0xE3, 0xE9, 0xFF),
        TitleTopInactive = new Color32(0xEF, 0xF1, 0xF4, 0xFF),
        TitleBottomInactive = new Color32(0xE3, 0xE6, 0xEA, 0xFF),
        Body = new Color32(0xDF, 0xE3, 0xE9, 0xFF),
        BodyInactive = new Color32(0xE3, 0xE6, 0xEA, 0xFF),
        Outline = new Color32(0x9A, 0xA2, 0xAE, 0xFF),
        OutlineInactive = new Color32(0xC2, 0xC8, 0xD0, 0xFF),
        Text = new Color32(0x1B, 0x1D, 0x23, 0xFF),
        TextInactive = new Color32(0x7A, 0x80, 0x8A, 0xFF),
        Toggle = new Color32(0x5B, 0x6B, 0x86, 0xFF),
        Dormant = new Color32(0xC2, 0xC8, 0xD0, 0xFF),
        Glyph = new Color32(0xF7, 0xF8, 0xFA, 0xFF),
        MenuFill = new Color32(0xF7, 0xF8, 0xFA, 0xF7),
        MenuHot = new Color32(0xD3, 0xDA, 0xE5, 0xFF),
    };
}
