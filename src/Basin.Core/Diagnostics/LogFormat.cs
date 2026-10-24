using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

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

    public static string WithException(ReadOnlySpan<char> message, Exception exception, bool stack)
    {
        var builder = new StringBuilder(message.Length + 64);
        builder.Append(message);
        var separator = " (";
        for (var current = exception; current is not null; current = current.InnerException)
        {
            builder.Append(separator).Append(current.GetType().Name).Append(": ");
            AppendOneLine(builder, current.Message);
            builder.Append(')');
            separator = " <- (";
        }

        if (stack)
        {
            for (var current = exception; current is not null; current = current.InnerException)
            {
                if (current.StackTrace is { } trace)
                {
                    builder.Append(Environment.NewLine).Append(current.GetType().Name).Append(':').Append(Environment.NewLine).Append(trace);
                }
            }
        }

        return builder.ToString();
    }

    private static void AppendOneLine(StringBuilder builder, string text)
    {
        var first = true;
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();
            if (trimmed.IsEmpty)
            {
                continue;
            }

            if (!first)
            {
                builder.Append(' ');
            }

            builder.Append(trimmed);
            first = false;
        }
    }
}
