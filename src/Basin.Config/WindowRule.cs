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

    public uint[]? SteamGames { get; init; }

    public string[]? Processes { get; init; }

    public string[]? X11Classes { get; init; }

    public string[]? X11Instances { get; init; }

    public string[]? WindowTypes { get; init; }

    public WindowQuirks Quirks { get; init; } = WindowQuirks.None;

    public bool HasAppCriteria => AppIds is not null || AppIdRegex is not null || AppIdPrefixes is not null;

    public bool HasTitleCriteria => Titles is not null || TitleRegex is not null;

    public int FactCriteriaCount =>
        (SteamGames is not null ? 1 : 0) + (Processes is not null ? 1 : 0) + (X11Classes is not null ? 1 : 0)
        + (X11Instances is not null ? 1 : 0) + (WindowTypes is not null ? 1 : 0);

    public int Specificity => (HasAppCriteria ? 2 : 0) + (HasTitleCriteria ? 1 : 0) + (4 * FactCriteriaCount);

    public bool MatchesText(string? appId, string? title) => Matches(new WindowFacts { AppId = appId, Title = title });

    public bool Matches(in WindowFacts facts)
    {
        if (FactCriteriaCount > 0)
        {
            if (!MatchesFacts(facts))
            {
                return false;
            }

            if (!HasAppCriteria && !HasTitleCriteria)
            {
                return true;
            }
        }

        return MatchesAppAndTitle(facts.AppId, facts.Title);
    }

    private bool MatchesFacts(in WindowFacts facts)
    {
        if (SteamGames is not null && (facts.SteamGame is not { } game || Array.IndexOf(SteamGames, game) < 0))
        {
            return false;
        }

        if (Processes is not null && !AnyEqual(Processes, facts.ProcessName) && !AnyEqual(Processes, facts.Executable))
        {
            return false;
        }

        if (X11Classes is not null && !AnyEqual(X11Classes, facts.X11Class))
        {
            return false;
        }

        if (X11Instances is not null && !AnyEqual(X11Instances, facts.X11Instance))
        {
            return false;
        }

        return WindowTypes is null || AnyEqual(WindowTypes, facts.WindowType);

        static bool AnyEqual(string[] candidates, string? value) =>
            !string.IsNullOrEmpty(value) && Array.IndexOf(candidates, value) >= 0;
    }

    private bool MatchesAppAndTitle(string? appId, string? title)
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

    public static WindowQuirks QuirksFor<TRule>(IReadOnlyList<TRule> mostSpecificFirst, in WindowFacts facts)
        where TRule : WindowRule
    {
        ArgumentNullException.ThrowIfNull(mostSpecificFirst);
        var merged = WindowQuirks.None;
        foreach (var rule in mostSpecificFirst)
        {
            if (!rule.Quirks.IsEmpty && rule.Matches(facts))
            {
                merged = merged.Merge(rule.Quirks);
            }
        }

        return merged;
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

    public static uint[]? SteamGameIds(TomlTable table, string key, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.TryGetValue(key, out var value) ? SteamGameIds(value, key, log) : null;
    }

    public static uint[]? SteamGameIds(object? value, string where, BasinLogger log)
    {
        if (value is null)
        {
            return null;
        }

        var parsed = value switch
        {
            long number => [number],
            TomlArray array => array.OfType<long>().ToArray(),
            _ => null,
        };
        if (parsed is null || parsed.Length == 0 || Array.Exists(parsed, static id => id <= 0 || id > uint.MaxValue))
        {
            log.Warn($"{where} wants a positive integer or a list of them");
            return null;
        }

        return [.. parsed.Select(static id => (uint)id)];
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
