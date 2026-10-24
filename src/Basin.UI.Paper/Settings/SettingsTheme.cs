using Prowl.Scribe;
using Prowl.Vector;

namespace Basin.UI.Paper;

/// <summary>
/// The font, colors and sizes that the settings widgets draw with.
/// </summary>
public sealed record SettingsTheme
{
    /// <summary>
    /// Gets the font of all text.
    /// </summary>
    public required FontFile Font { get; init; }

    /// <summary>
    /// Gets the body font size in logical pixels. The default is 14. The other sizes scale from it.
    /// </summary>
    public float FontSize { get; init; } = 14f;

    /// <summary>
    /// Gets the color behind the whole settings page.
    /// </summary>
    public Color32 Background { get; init; }

    /// <summary>
    /// Gets the fill of text fields, list items and the section list.
    /// </summary>
    public Color32 Surface { get; init; }

    /// <summary>
    /// Gets the fill of buttons, closed choices and switches that are off.
    /// </summary>
    public Color32 Raised { get; init; }

    /// <summary>
    /// Gets the border color of controls.
    /// </summary>
    public Color32 Border { get; init; }

    /// <summary>
    /// Gets the color of ordinary text.
    /// </summary>
    public Color32 Text { get; init; }

    /// <summary>
    /// Gets the color of notes, placeholders, disabled text and the scroll bar.
    /// </summary>
    public Color32 Dim { get; init; }

    /// <summary>
    /// Gets the color of selected items, focus borders, switches that are on and the dirty dot.
    /// </summary>
    public Color32 Accent { get; init; }

    /// <summary>
    /// Gets the color of text on an <see cref="Accent"/> fill.
    /// </summary>
    public Color32 AccentText { get; init; }

    /// <summary>
    /// Gets the color of badges and hints.
    /// </summary>
    public Color32 Warning { get; init; }

    /// <summary>
    /// Gets the color of error messages.
    /// </summary>
    public Color32 Error { get; init; }

    /// <summary>
    /// Gets the height of one settings row in logical pixels, 2.3 times <see cref="FontSize"/>, rounded.
    /// </summary>
    public float RowHeight => MathF.Round(FontSize * 2.3f);

    /// <summary>
    /// Gets the width of the control at the right of a row in logical pixels, 18 times <see cref="FontSize"/>, rounded.
    /// </summary>
    public float ControlWidth => MathF.Round(FontSize * 18f);

    /// <summary>
    /// Gets the small font size in logical pixels, 0.85 times <see cref="FontSize"/>, rounded.
    /// </summary>
    public float Small => MathF.Round(FontSize * 0.85f);

    /// <summary>
    /// Makes the dark theme.
    /// </summary>
    /// <param name="font">The font of all text.</param>
    /// <param name="fontSize">The body font size in logical pixels.</param>
    /// <returns>A new theme.</returns>
    public static SettingsTheme Dark(FontFile font, float fontSize = 14f) => new()
    {
        Font = font,
        FontSize = fontSize,
        Background = new Color32(0x1b, 0x1d, 0x23, 0xff),
        Surface = new Color32(0x23, 0x26, 0x2e, 0xff),
        Raised = new Color32(0x2e, 0x32, 0x3c, 0xff),
        Border = new Color32(0x3d, 0x42, 0x4e, 0xff),
        Text = new Color32(0xe6, 0xe9, 0xef, 0xff),
        Dim = new Color32(0x9a, 0xa1, 0xae, 0xff),
        Accent = new Color32(0x4c, 0x8d, 0xf6, 0xff),
        AccentText = new Color32(0xff, 0xff, 0xff, 0xff),
        Warning = new Color32(0xe0, 0xa3, 0x3a, 0xff),
        Error = new Color32(0xe5, 0x5b, 0x5b, 0xff),
    };

    /// <summary>
    /// Makes the light theme.
    /// </summary>
    /// <param name="font">The font of all text.</param>
    /// <param name="fontSize">The body font size in logical pixels.</param>
    /// <returns>A new theme.</returns>
    public static SettingsTheme Light(FontFile font, float fontSize = 14f) => new()
    {
        Font = font,
        FontSize = fontSize,
        Background = new Color32(0xf4, 0xf5, 0xf7, 0xff),
        Surface = new Color32(0xff, 0xff, 0xff, 0xff),
        Raised = new Color32(0xe8, 0xea, 0xee, 0xff),
        Border = new Color32(0xc9, 0xcd, 0xd5, 0xff),
        Text = new Color32(0x1d, 0x20, 0x26, 0xff),
        Dim = new Color32(0x5e, 0x65, 0x72, 0xff),
        Accent = new Color32(0x25, 0x63, 0xd9, 0xff),
        AccentText = new Color32(0xff, 0xff, 0xff, 0xff),
        Warning = new Color32(0xa8, 0x6b, 0x00, 0xff),
        Error = new Color32(0xc0, 0x2f, 0x2f, 0xff),
    };
}
