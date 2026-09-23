using Prowl.Scribe;
using Prowl.Vector;

namespace Basin.Frames.Quill;

public sealed class QuillFrameTheme
{
    public FontFile? Face { get; set; }

    public float FontSize { get; set; } = 13f;

    public int TitleHeight { get; set; } = 30;

    public int Border { get; set; } = 4;

    public int ButtonSize { get; set; } = 18;

    public int ButtonGap { get; set; } = 8;

    public bool Toggles { get; set; }

    public int IconSize { get; set; } = 18;

    public int CornerZone { get; set; } = 16;

    public float CornerRadius { get; set; } = 8f;

    public bool Frosted { get; set; }

    public byte FrostAlpha { get; set; } = 0x70;

    public Color32 TitleTop { get; set; } = new(0x3B, 0x41, 0x4D, 0xFF);

    public Color32 TitleBottom { get; set; } = new(0x24, 0x28, 0x31, 0xFF);

    public Color32 TitleTopInactive { get; set; } = new(0x24, 0x27, 0x2D, 0xFF);

    public Color32 TitleBottomInactive { get; set; } = new(0x1A, 0x1C, 0x21, 0xFF);

    public Color32 Body { get; set; } = new(0x24, 0x28, 0x31, 0xFF);

    public Color32 BodyInactive { get; set; } = new(0x1A, 0x1C, 0x21, 0xFF);

    public Color32 Outline { get; set; } = new(0x70, 0x78, 0x86, 0xFF);

    public Color32 OutlineInactive { get; set; } = new(0x3A, 0x3E, 0x47, 0xFF);

    public Color32 Text { get; set; } = new(0xF2, 0xF4, 0xF7, 0xFF);

    public Color32 TextInactive { get; set; } = new(0x88, 0x8E, 0x99, 0xFF);

    public Color32 Close { get; set; } = new(0xFF, 0x5F, 0x57, 0xFF);

    public Color32 Maximize { get; set; } = new(0x28, 0xC8, 0x40, 0xFF);

    public Color32 Minimize { get; set; } = new(0xFE, 0xBC, 0x2E, 0xFF);

    public Color32 Toggle { get; set; } = new(0x7C, 0x8B, 0xA8, 0xFF);

    public Color32 Dormant { get; set; } = new(0x4A, 0x4F, 0x59, 0xFF);

    public Color32 Glyph { get; set; } = new(0x1B, 0x1D, 0x23, 0xFF);

    public Color32 MenuFill { get; set; } = new(0x1F, 0x22, 0x29, 0xF7);

    public Color32 MenuHot { get; set; } = new(0x3A, 0x41, 0x4E, 0xFF);

    public int MenuWidth { get; set; } = 190;

    public int MenuItemHeight { get; set; } = 28;

    public int MenuPadding { get; set; } = 6;

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
