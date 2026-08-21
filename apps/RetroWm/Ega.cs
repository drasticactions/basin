using SkiaSharp;
using Tomlyn.Model;

namespace RetroWm;

internal static class Ega
{
    public const uint Black = 0x000000FF;
    public const uint Blue = 0x0000AAFF;
    public const uint Green = 0x00AA00FF;
    public const uint Cyan = 0x00AAAAFF;
    public const uint Red = 0xAA0000FF;
    public const uint Magenta = 0xAA00AAFF;
    public const uint Brown = 0xAA5500FF;
    public const uint LightGray = 0xAAAAAAFF;
    public const uint DarkGray = 0x555555FF;
    public const uint BrightBlue = 0x5555FFFF;
    public const uint BrightGreen = 0x55FF55FF;
    public const uint BrightCyan = 0x55FFFFFF;
    public const uint BrightRed = 0xFF5555FF;
    public const uint BrightMagenta = 0xFF55FFFF;
    public const uint Yellow = 0xFFFF55FF;
    public const uint White = 0xFFFFFFFF;

    public static ReadOnlySpan<uint> Palette =>
    [
        Black, Blue, Green, Cyan, Red, Magenta, Brown, LightGray,
        DarkGray, BrightBlue, BrightGreen, BrightCyan, BrightRed, BrightMagenta, Yellow, White,
    ];
}
