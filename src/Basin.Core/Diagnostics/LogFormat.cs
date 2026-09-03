using System.Globalization;
using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

internal static class LogFormat
{
    private const int Width = 64;

    public static void Append(ref DefaultInterpolatedStringHandler inner, double value, string? format)
    {
        Span<char> chars = stackalloc char[Width];
        if (value.TryFormat(chars, out var written, format, CultureInfo.InvariantCulture))
        {
            inner.AppendFormatted(chars[..written]);
            return;
        }

        inner.AppendFormatted(value, format);
    }

    public static void Append(ref DefaultInterpolatedStringHandler inner, float value, string? format)
    {
        Span<char> chars = stackalloc char[Width];
        if (value.TryFormat(chars, out var written, format, CultureInfo.InvariantCulture))
        {
            inner.AppendFormatted(chars[..written]);
            return;
        }

        inner.AppendFormatted(value, format);
    }

    public static void Append(ref DefaultInterpolatedStringHandler inner, long value, string? format)
    {
        Span<char> chars = stackalloc char[Width];
        if (value.TryFormat(chars, out var written, format, CultureInfo.InvariantCulture))
        {
            inner.AppendFormatted(chars[..written]);
            return;
        }

        inner.AppendFormatted(value, format);
    }

    public static void Append(ref DefaultInterpolatedStringHandler inner, ulong value, string? format)
    {
        Span<char> chars = stackalloc char[Width];
        if (value.TryFormat(chars, out var written, format, CultureInfo.InvariantCulture))
        {
            inner.AppendFormatted(chars[..written]);
            return;
        }

        inner.AppendFormatted(value, format);
    }

    public static void Append(ref DefaultInterpolatedStringHandler inner, bool value) =>
        inner.AppendLiteral(value ? "True" : "False");
}
