namespace Basin.Freedesktop;

/// <summary>
/// A POSIX locale in the <c>lang_COUNTRY@MODIFIER</c> form, which ranks the localized keys of a desktop entry. The encoding is not kept.
/// </summary>
/// <param name="Lang">The language part. It is empty for <see cref="None"/>.</param>
/// <param name="Country">The country part, or <see langword="null"/>.</param>
/// <param name="Modifier">The modifier part, or <see langword="null"/>.</param>
public readonly record struct DesktopLocale(string Lang, string? Country, string? Modifier)
{
    /// <summary>
    /// Gets the locale that matches no localized key, so the reader uses the bare key.
    /// </summary>
    public static DesktopLocale None => default;

    /// <summary>
    /// Gets a value indicating whether the locale has no language and so matches no suffix.
    /// </summary>
    public bool IsNone => string.IsNullOrEmpty(Lang);

    /// <summary>
    /// Reads the message locale from <c>LC_ALL</c>, then <c>LC_MESSAGES</c>, then <c>LANG</c>. The first non-empty variable wins.
    /// </summary>
    /// <returns>The parsed locale, or <see cref="None"/> when no variable is set.</returns>
    public static DesktopLocale FromEnvironment()
    {
        foreach (var variable in (string[])["LC_ALL", "LC_MESSAGES", "LANG"])
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value)
            {
                return Parse(value);
            }
        }

        return None;
    }

    /// <summary>
    /// Parses a locale string such as <c>de_DE.UTF-8@euro</c>. The encoding after the dot is dropped.
    /// </summary>
    /// <param name="value">The locale string. Surrounding white space is ignored.</param>
    /// <returns>The locale, or <see cref="None"/> for an empty language, <c>C</c> or <c>POSIX</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static DesktopLocale Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var span = value.AsSpan().Trim();
        string? modifier = null;
        var at = span.IndexOf('@');
        if (at >= 0)
        {
            modifier = span[(at + 1)..].ToString();
            span = span[..at];
        }

        var dot = span.IndexOf('.');
        if (dot >= 0)
        {
            span = span[..dot];
        }

        string? country = null;
        var underscore = span.IndexOf('_');
        if (underscore >= 0)
        {
            country = span[(underscore + 1)..].ToString();
            span = span[..underscore];
        }

        var lang = span.ToString();
        if (lang.Length == 0 || lang == "C" || lang == "POSIX")
        {
            return None;
        }

        return new DesktopLocale(lang, string.IsNullOrEmpty(country) ? null : country, string.IsNullOrEmpty(modifier) ? null : modifier);
    }

    internal int Rank(string suffix)
    {
        if (IsNone)
        {
            return 0;
        }

        var candidate = Parse(suffix);
        if (candidate.IsNone || !string.Equals(candidate.Lang, Lang, StringComparison.Ordinal))
        {
            return 0;
        }

        var rank = 1;
        if (candidate.Country is not null)
        {
            if (!string.Equals(candidate.Country, Country, StringComparison.Ordinal))
            {
                return 0;
            }

            rank += 2;
        }

        if (candidate.Modifier is not null)
        {
            if (!string.Equals(candidate.Modifier, Modifier, StringComparison.Ordinal))
            {
                return 0;
            }

            rank += 1;
        }

        return rank;
    }
}
