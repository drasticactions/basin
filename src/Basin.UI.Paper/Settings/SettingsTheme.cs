using Prowl.Scribe;
using Prowl.Vector;

namespace Basin.UI.Paper;

public sealed record SettingsTheme
{
    public required FontFile Font { get; init; }

    public float FontSize { get; init; } = 14f;

    public Color32 Background { get; init; }

    public Color32 Surface { get; init; }

    public Color32 Raised { get; init; }

    public Color32 Border { get; init; }

    public Color32 Text { get; init; }

    public Color32 Dim { get; init; }

    public Color32 Accent { get; init; }

    public Color32 AccentText { get; init; }

    public Color32 Warning { get; init; }

    public Color32 Error { get; init; }

    public float RowHeight => MathF.Round(FontSize * 2.3f);

    public float ControlWidth => MathF.Round(FontSize * 18f);

    public float Small => MathF.Round(FontSize * 0.85f);

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
