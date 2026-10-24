using System.Globalization;

namespace Basin.Config;

/// <summary>
/// Reads colors from TOML values.
/// </summary>
public static class TomlColor
{
    /// <summary>
    /// Reads an RGBA color from an integer or from a <c>#rgb</c>, <c>#rrggbb</c> or <c>#rrggbbaa</c> string. A color without alpha is opaque.
    /// </summary>
    /// <param name="value">The TOML value.</param>
    /// <returns>The color as <c>0xRRGGBBAA</c>, or <see langword="null"/> when the value is not a color.</returns>
    public static uint? Rgba(object? value)
    {
        if (value is long raw)
        {
            return (uint)raw;
        }

        if (value is not string text || !text.StartsWith('#'))
        {
            return null;
        }

        var hex = text[1..];
        try
        {
            return hex.Length switch
            {
                3 => (uint)(
                    (Convert.ToUInt32(hex[..1], 16) * 0x11 << 24)
                    | (Convert.ToUInt32(hex[1..2], 16) * 0x11 << 16)
                    | (Convert.ToUInt32(hex[2..3], 16) * 0x11 << 8)
                    | 0xFF),
                6 => (Convert.ToUInt32(hex, 16) << 8) | 0xFF,
                8 => Convert.ToUInt32(hex, 16),
                _ => null,
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads an ARGB color from hexadecimal digits with an optional <c>#</c>: three digits (<c>rgb</c>) or six (<c>rrggbb</c>) give an opaque color, and eight are <c>aarrggbb</c>.
    /// </summary>
    /// <param name="text">The color text.</param>
    /// <param name="fallback">The color to return when the text is empty, not hexadecimal, or another length.</param>
    /// <returns>The color as <c>0xAARRGGBB</c>.</returns>
    public static uint Argb(string? text, uint fallback)
    {
        if (text is not { Length: > 0 })
        {
            return fallback;
        }

        var digits = text.StartsWith('#') ? text.AsSpan(1) : text.AsSpan();
        if (digits.Length is not (3 or 6 or 8) ||
            !uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            return fallback;
        }

        return digits.Length switch
        {
            3 => 0xff000000u
                | (((value >> 8) & 0xf) * 0x11 << 16)
                | (((value >> 4) & 0xf) * 0x11 << 8)
                | ((value & 0xf) * 0x11),
            6 => 0xff000000u | value,
            _ => value,
        };
    }
}
