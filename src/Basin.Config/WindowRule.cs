using System.Text.RegularExpressions;
using Basin.Diagnostics;
using Tomlyn.Model;

namespace Basin.Config;

public class WindowRule
{
    public string[]? AppIds { get; init; }

    public string[]? AppIdPrefixes { get; init; }

    public string[]? Titles { get; init; }

    public Regex? AppIdRegex { get; init; }

    public Regex? TitleRegex { get; init; }

    public bool HasAppCriteria => AppIds is not null || AppIdRegex is not null || AppIdPrefixes is not null;

    public bool HasTitleCriteria => Titles is not null || TitleRegex is not null;

    public int Specificity => (HasAppCriteria ? 2 : 0) + (HasTitleCriteria ? 1 : 0);

    public bool MatchesText(string? appId, string? title)
    {
        if (!HasAppCriteria && !HasTitleCriteria)
        {
            return string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(title);
        }

        if (HasAppCriteria)
        {
            if (appId is null)
            {
                return false;
            }

            var matched = (AppIds is not null && Array.IndexOf(AppIds, appId) >= 0)
                || AppIdRegex?.IsMatch(appId) == true
                || (AppIdPrefixes is not null
                    && AppIdPrefixes.Any(prefix => appId.StartsWith(prefix, StringComparison.Ordinal)));
            if (!matched)
            {
                return false;
            }
        }

        if (Titles is not null && title is not null)
        {
            return Array.IndexOf(Titles, title) >= 0;
        }

        if (TitleRegex is not null)
        {
            return title is not null && TitleRegex.IsMatch(title);
        }

        return !HasTitleCriteria;
    }

    public static IReadOnlyList<TRule> MostSpecificFirst<TRule>(IEnumerable<TRule> rules)
        where TRule : WindowRule
    {
        ArgumentNullException.ThrowIfNull(rules);
        return [.. rules.OrderByDescending(static rule => rule.Specificity)];
    }

    public static string[]? Strings(TomlTable table, params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var key in keys)
        {
            if (table.TryGetValue(key, out var value))
            {
                var parsed = value switch
                {
                    string text => (string[])[text],
                    TomlArray array => [.. array.OfType<string>()],
                    _ => null,
                };
                if (parsed is not null)
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    public static Regex? Pattern(TomlTable table, string key, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.TryGetValue(key, out var value) && value is string { Length: > 0 } pattern)
        {
            try
            {
                return new Regex(pattern);
            }
            catch (ArgumentException error)
            {
                log.Warn($"rule pattern '{pattern}' is invalid: {error.Message}");
            }
        }

        return null;
    }
}
