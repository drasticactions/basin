namespace Basin.Freedesktop;

public readonly record struct DesktopLocale(string Lang, string? Country, string? Modifier)
{
    public static DesktopLocale None => default;

    public bool IsNone => string.IsNullOrEmpty(Lang);

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
