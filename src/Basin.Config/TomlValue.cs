using System.Globalization;
using System.Text;

namespace Basin.Config;

public sealed class TomlValue : IEquatable<TomlValue>
{
    private TomlValue(string text)
    {
        Text = text;
    }

    public string Text { get; }

    public static TomlValue From(bool value) => new(value ? "true" : "false");

    public static TomlValue From(long value) => new(value.ToString(CultureInfo.InvariantCulture));

    public static TomlValue From(double value)
    {
        if (double.IsNaN(value))
        {
            return new("nan");
        }

        if (double.IsInfinity(value))
        {
            return new(value > 0 ? "inf" : "-inf");
        }

        var text = value.ToString("R", CultureInfo.InvariantCulture);
        if (text.IndexOfAny(['.', 'E', 'e']) < 0)
        {
            text += ".0";
        }

        return new(text);
    }

    public static TomlValue From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(Quote(value));
    }

    public static TomlValue Parse(string literal)
    {
        ArgumentNullException.ThrowIfNull(literal);
        var text = literal.Trim();
        if (text.Length == 0 || text.Contains('\n', StringComparison.Ordinal) || Tomlyn.Toml.Parse("v = " + text).HasErrors)
        {
            throw new FormatException($"'{literal}' is not a TOML value");
        }

        return new(text);
    }

    public static bool TryParse(string literal, out TomlValue? value)
    {
        try
        {
            value = Parse(literal);
            return true;
        }
        catch (FormatException)
        {
            value = null;
            return false;
        }
    }

    public static TomlValue Array(IEnumerable<TomlValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var builder = new StringBuilder("[");
        var first = true;
        foreach (var item in items)
        {
            builder.Append(first ? string.Empty : ", ").Append(item.Text);
            first = false;
        }

        return new(builder.Append(']').ToString());
    }

    public static TomlValue Inline(IEnumerable<KeyValuePair<string, TomlValue>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var builder = new StringBuilder("{");
        var first = true;
        foreach (var (key, value) in entries)
        {
            builder.Append(first ? " " : ", ").Append(Key(key)).Append(" = ").Append(value.Text);
            first = false;
        }

        return new(builder.Append(first ? "}" : " }").ToString());
    }

    public static string Key(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length == 0)
        {
            return "\"\"";
        }

        foreach (var c in key)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-')
            {
                return Quote(key);
            }
        }

        return key;
    }

    public bool Equals(TomlValue? other) => other is not null && string.Equals(Text, other.Text, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as TomlValue);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Text);

    public override string ToString() => Text;

    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                default:
                    if (char.IsControl(c))
                    {
                        builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.Append('"').ToString();
    }
}
