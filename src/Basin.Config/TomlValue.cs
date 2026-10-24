using System.Globalization;
using System.Text;

namespace Basin.Config;

/// <summary>
/// A TOML value literal in its text form, ready to write into a <see cref="TomlDocument"/>. Two values are equal when their text is equal.
/// </summary>
public sealed class TomlValue : IEquatable<TomlValue>
{
    private TomlValue(string text)
    {
        Text = text;
    }

    /// <summary>
    /// Gets the TOML literal text, for example <c>"name"</c>, <c>1.0</c> or <c>[1, 2]</c>.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Makes a boolean literal.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns><c>true</c> or <c>false</c>.</returns>
    public static TomlValue From(bool value) => new(value ? "true" : "false");

    /// <summary>
    /// Makes an integer literal in decimal.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The integer literal.</returns>
    public static TomlValue From(long value) => new(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Makes a float literal that reads back as the same value. A whole number gets <c>.0</c>, so that it stays a float.
    /// </summary>
    /// <param name="value">The value. NaN and infinities give <c>nan</c>, <c>inf</c> and <c>-inf</c>.</param>
    /// <returns>The float literal.</returns>
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

    /// <summary>
    /// Makes a basic string literal with quotes, backslashes and control characters escaped.
    /// </summary>
    /// <param name="value">The string.</param>
    /// <returns>The quoted string literal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static TomlValue From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(Quote(value));
    }

    /// <summary>
    /// Checks that a text is one single-line TOML value and keeps it without the outer white space.
    /// </summary>
    /// <param name="literal">The literal text.</param>
    /// <returns>The value.</returns>
    /// <exception cref="FormatException">The text is empty, has a line break or is not a TOML value.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="literal"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Checks a literal in the same way as <see cref="Parse"/> without an exception.
    /// </summary>
    /// <param name="literal">The literal text.</param>
    /// <param name="value">The value, or <see langword="null"/> on failure.</param>
    /// <returns><see langword="false"/> when the text is not one single-line TOML value.</returns>
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

    /// <summary>
    /// Makes an inline array of the given values.
    /// </summary>
    /// <param name="items">The values in order.</param>
    /// <returns>The array literal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Makes an inline table of the given entries. Keys are quoted when they are not bare keys.
    /// </summary>
    /// <param name="entries">The keys and values in order.</param>
    /// <returns>The inline table literal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Formats a key: bare when it has only ASCII letters, digits, <c>_</c> and <c>-</c>, else quoted.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The key text for a TOML file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
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

    /// <inheritdoc/>
    public bool Equals(TomlValue? other) => other is not null && string.Equals(Text, other.Text, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as TomlValue);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Text);

    /// <summary>
    /// Gives the literal text.
    /// </summary>
    /// <returns>The value of <see cref="Text"/>.</returns>
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
