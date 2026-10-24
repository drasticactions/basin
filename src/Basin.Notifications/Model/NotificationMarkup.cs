using System.Globalization;
using System.Text;

namespace Basin.Notifications;

/// <summary>
/// Reduces the body markup of the specification to plain text and a list of spans.
/// </summary>
public static class NotificationMarkup
{
    /// <summary>
    /// Reduces markup to plain text. <c>b</c>, <c>i</c>, <c>u</c>, <c>a</c> and <c>img</c> become spans, <c>br</c> becomes a line feed, and other tags are removed with their text kept.
    /// Entities are decoded. A less-than sign that does not start a tag stays as text.
    /// </summary>
    /// <param name="markup">The body text with markup.</param>
    /// <param name="spans">The spans in order of start, the longer span first at the same start. It is empty when the text has no markup.</param>
    /// <returns>The plain text. It is <paramref name="markup"/> itself when the text has no tag or entity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="markup"/> is <see langword="null"/>.</exception>
    public static string Reduce(string markup, out NotificationSpan[] spans)
    {
        ArgumentNullException.ThrowIfNull(markup);
        if (markup.AsSpan().IndexOfAny('<', '&') < 0)
        {
            spans = [];
            return markup;
        }

        var text = new StringBuilder(markup.Length);
        var open = new List<(NotificationSpanKind Kind, int Start, string? Target)>();
        var closed = new List<NotificationSpan>();
        var i = 0;
        while (i < markup.Length)
        {
            var c = markup[i];
            if (c == '<' && TryReadTag(markup, i, out var end, out var closing, out var name, out var attributes))
            {
                Apply(text, open, closed, closing, name, attributes);
                i = end;
                continue;
            }

            if (c == '&' && TryReadEntity(markup, i, out var entityEnd, out var decoded))
            {
                text.Append(decoded);
                i = entityEnd;
                continue;
            }

            text.Append(c);
            i++;
        }

        for (var k = open.Count - 1; k >= 0; k--)
        {
            Close(text, closed, open[k]);
        }

        closed.Sort(static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.Length.CompareTo(a.Length));
        spans = [.. closed];
        return text.ToString();
    }

    private static void Apply(
        StringBuilder text,
        List<(NotificationSpanKind Kind, int Start, string? Target)> open,
        List<NotificationSpan> closed,
        bool closing,
        string name,
        string attributes)
    {
        switch (name)
        {
            case "br":
                if (!closing)
                {
                    text.Append('\n');
                }

                return;

            case "img":
                if (!closing)
                {
                    var alt = Attribute(attributes, "alt") ?? "";
                    var start = text.Length;
                    text.Append(alt);
                    closed.Add(new NotificationSpan(start, alt.Length, NotificationSpanKind.Image, Attribute(attributes, "src")));
                }

                return;
        }

        NotificationSpanKind kind;
        switch (name)
        {
            case "b":
                kind = NotificationSpanKind.Bold;
                break;
            case "i":
                kind = NotificationSpanKind.Italic;
                break;
            case "u":
                kind = NotificationSpanKind.Underline;
                break;
            case "a":
                kind = NotificationSpanKind.Link;
                break;
            default:
                return;
        }

        if (!closing)
        {
            open.Add((kind, text.Length, kind == NotificationSpanKind.Link ? Attribute(attributes, "href") : null));
            return;
        }

        for (var k = open.Count - 1; k >= 0; k--)
        {
            if (open[k].Kind == kind)
            {
                Close(text, closed, open[k]);
                open.RemoveAt(k);
                return;
            }
        }
    }

    private static void Close(StringBuilder text, List<NotificationSpan> closed, (NotificationSpanKind Kind, int Start, string? Target) entry)
    {
        var length = text.Length - entry.Start;
        if (length > 0)
        {
            closed.Add(new NotificationSpan(entry.Start, length, entry.Kind, entry.Target));
        }
    }

    private static bool TryReadTag(string markup, int at, out int end, out bool closing, out string name, out string attributes)
    {
        end = 0;
        closing = false;
        name = "";
        attributes = "";
        var i = at + 1;
        if (i < markup.Length && markup[i] == '/')
        {
            closing = true;
            i++;
        }

        var nameStart = i;
        while (i < markup.Length && char.IsAsciiLetterOrDigit(markup[i]))
        {
            i++;
        }

        if (i == nameStart || !char.IsAsciiLetter(markup[nameStart]))
        {
            return false;
        }

        var close = markup.IndexOf('>', i);
        if (close < 0)
        {
            return false;
        }

        if (i < close && !char.IsWhiteSpace(markup[i]) && markup[i] != '/')
        {
            return false;
        }

        name = markup[nameStart..i].ToLowerInvariant();
        attributes = markup[i..close];
        end = close + 1;
        return true;
    }

    private static string? Attribute(string attributes, string name)
    {
        var i = 0;
        while (i < attributes.Length)
        {
            while (i < attributes.Length && (char.IsWhiteSpace(attributes[i]) || attributes[i] == '/'))
            {
                i++;
            }

            var keyStart = i;
            while (i < attributes.Length && attributes[i] != '=' && !char.IsWhiteSpace(attributes[i]) && attributes[i] != '/')
            {
                i++;
            }

            var key = attributes.AsSpan(keyStart, i - keyStart);
            while (i < attributes.Length && char.IsWhiteSpace(attributes[i]))
            {
                i++;
            }

            if (i >= attributes.Length || attributes[i] != '=')
            {
                if (i == keyStart)
                {
                    i++;
                }

                continue;
            }

            i++;
            while (i < attributes.Length && char.IsWhiteSpace(attributes[i]))
            {
                i++;
            }

            string value;
            if (i < attributes.Length && attributes[i] is '"' or '\'')
            {
                var quote = attributes[i];
                var valueEnd = attributes.IndexOf(quote, i + 1);
                if (valueEnd < 0)
                {
                    valueEnd = attributes.Length;
                }

                value = attributes[(i + 1)..valueEnd];
                i = Math.Min(valueEnd + 1, attributes.Length);
            }
            else
            {
                var valueStart = i;
                while (i < attributes.Length && !char.IsWhiteSpace(attributes[i]))
                {
                    i++;
                }

                value = attributes[valueStart..i];
            }

            if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return DecodeEntities(value);
            }
        }

        return null;
    }

    private static string DecodeEntities(string value)
    {
        if (!value.Contains('&', StringComparison.Ordinal))
        {
            return value;
        }

        var text = new StringBuilder(value.Length);
        var i = 0;
        while (i < value.Length)
        {
            if (value[i] == '&' && TryReadEntity(value, i, out var end, out var decoded))
            {
                text.Append(decoded);
                i = end;
                continue;
            }

            text.Append(value[i]);
            i++;
        }

        return text.ToString();
    }

    private static bool TryReadEntity(string markup, int at, out int end, out string decoded)
    {
        end = 0;
        decoded = "";
        var semicolon = markup.IndexOf(';', at + 1, Math.Min(12, markup.Length - at - 1));
        if (semicolon < 0)
        {
            return false;
        }

        var entity = markup.AsSpan(at + 1, semicolon - at - 1);
        switch (entity)
        {
            case "amp":
                decoded = "&";
                break;
            case "lt":
                decoded = "<";
                break;
            case "gt":
                decoded = ">";
                break;
            case "quot":
                decoded = "\"";
                break;
            case "apos":
                decoded = "'";
                break;
            case "nbsp":
                decoded = " ";
                break;
            default:
                if (entity.Length < 2 || entity[0] != '#')
                {
                    return false;
                }

                var hex = entity[1] is 'x' or 'X';
                var digits = hex ? entity[2..] : entity[1..];
                if (!int.TryParse(digits, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out var code)
                    || code <= 0 || code > 0x10FFFF || code is >= 0xD800 and <= 0xDFFF)
                {
                    return false;
                }

                decoded = char.ConvertFromUtf32(code);
                break;
        }

        end = semicolon + 1;
        return true;
    }
}
