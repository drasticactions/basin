using System.Globalization;

namespace Basin.Config;

public static class TomlColor
{
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

    public static uint Argb(string? text, uint fallback)
    {
        if (text is not { Length: > 0 })
        {
            return fallback;
        }

        var digits = text.TrimStart('#');
        if (!uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return fallback;
        }

        return digits.Length <= 6 ? 0xff000000u | value : value;
    }
}
