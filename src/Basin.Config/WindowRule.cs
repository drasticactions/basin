using System.Text.RegularExpressions;
using Basin.Diagnostics;
using Tomlyn.Model;

namespace Basin.Config;

/// <summary>
/// Matches windows by their <see cref="WindowFacts"/>. A consumer subclasses it to add the fields that its rules set.
/// Every criterion that a rule sets must match, and a list matches when one of its members matches.
/// </summary>
public class WindowRule
{
    /// <summary>
    /// Gets the app IDs to match exactly, or <see langword="null"/>.
    /// </summary>
    public string[]? AppIds { get; init; }

    /// <summary>
    /// Gets the app ID prefixes to match, or <see langword="null"/>.
    /// </summary>
    public string[]? AppIdPrefixes { get; init; }

    /// <summary>
    /// Gets the titles to match exactly, or <see langword="null"/>.
    /// </summary>
    public string[]? Titles { get; init; }

    /// <summary>
    /// Gets the pattern that the app ID must match, or <see langword="null"/>.
    /// </summary>
    public Regex? AppIdRegex { get; init; }

    /// <summary>
    /// Gets the pattern that the title must match, or <see langword="null"/>.
    /// </summary>
    public Regex? TitleRegex { get; init; }

    /// <summary>
    /// Gets the Steam game IDs to match, or <see langword="null"/>.
    /// </summary>
    public uint[]? SteamGames { get; init; }

    /// <summary>
    /// Gets the process names or executable base names to match exactly, or <see langword="null"/>.
    /// </summary>
    public string[]? Processes { get; init; }

    /// <summary>
    /// Gets the <c>WM_CLASS</c> classes to match, or <see langword="null"/>.
    /// </summary>
    public string[]? X11Classes { get; init; }

    /// <summary>
    /// Gets the <c>WM_CLASS</c> instances to match, or <see langword="null"/>.
    /// </summary>
    public string[]? X11Instances { get; init; }

    /// <summary>
    /// Gets the window types to match, or <see langword="null"/>.
    /// </summary>
    public string[]? WindowTypes { get; init; }

    /// <summary>
    /// Gets the <see cref="WindowFacts.WantsFocus"/> value to match, or <see langword="null"/>. An unknown fact never matches.
    /// </summary>
    public bool? WantsFocus { get; init; }

    /// <summary>
    /// Gets the <see cref="WindowFacts.SkipTaskbar"/> value to match, or <see langword="null"/>. An unknown fact never matches.
    /// </summary>
    public bool? SkipTaskbar { get; init; }

    /// <summary>
    /// Gets the quirks that the rule asks for. The default is <see cref="WindowQuirks.None"/>.
    /// </summary>
    public WindowQuirks Quirks { get; init; } = WindowQuirks.None;

    /// <summary>
    /// Gets a value indicating whether the rule sets an app ID list, prefix or pattern.
    /// </summary>
    public bool HasAppCriteria => AppIds is not null || AppIdRegex is not null || AppIdPrefixes is not null;

    /// <summary>
    /// Gets a value indicating whether the rule sets a title list or pattern.
    /// </summary>
    public bool HasTitleCriteria => Titles is not null || TitleRegex is not null;

    /// <summary>
    /// Gets the number of fact criteria that the rule sets: Steam game, process, X11 class and instance, window type, focus and taskbar.
    /// </summary>
    public int FactCriteriaCount =>
        (SteamGames is not null ? 1 : 0) + (Processes is not null ? 1 : 0) + (X11Classes is not null ? 1 : 0)
        + (X11Instances is not null ? 1 : 0) + (WindowTypes is not null ? 1 : 0)
        + (WantsFocus is not null ? 1 : 0) + (SkipTaskbar is not null ? 1 : 0);

    /// <summary>
    /// Gets the rank of the rule: 2 for app criteria, 1 for title criteria and 4 for each fact criterion. A higher rank wins.
    /// </summary>
    public int Specificity => (HasAppCriteria ? 2 : 0) + (HasTitleCriteria ? 1 : 0) + (4 * FactCriteriaCount);

    /// <summary>
    /// Matches a window that is known only by its app ID and title. A rule with a fact criterion never matches, and a rule with no criteria matches every window.
    /// </summary>
    /// <param name="appId">The app ID, or <see langword="null"/>.</param>
    /// <param name="title">The title, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the rule matches.</returns>
    public bool MatchesText(string? appId, string? title) => Matches(new WindowFacts { AppId = appId, Title = title });

    /// <summary>
    /// Tells whether the rule matches a window. A rule with no criteria matches every window.
    /// </summary>
    /// <param name="facts">The facts of the window.</param>
    /// <returns><see langword="true"/> when every criterion of the rule matches.</returns>
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

        if (WantsFocus is { } wantsFocus && facts.WantsFocus != wantsFocus)
        {
            return false;
        }

        if (SkipTaskbar is { } skipTaskbar && facts.SkipTaskbar != skipTaskbar)
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
            return true;
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

    /// <summary>
    /// Merges the quirks of every matching rule. For each quirk, the first rule with an opinion wins.
    /// </summary>
    /// <typeparam name="TRule">The rule type of the consumer.</typeparam>
    /// <param name="mostSpecificFirst">The rules in the order of <see cref="MostSpecificFirst{TRule}"/>.</param>
    /// <param name="facts">The facts of the window.</param>
    /// <returns>The merged quirks, or <see cref="WindowQuirks.None"/> when no rule with quirks matches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mostSpecificFirst"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Sorts rules by <see cref="Specificity"/>, highest first. Rules of equal rank keep their order.
    /// </summary>
    /// <typeparam name="TRule">The rule type of the consumer.</typeparam>
    /// <param name="rules">The rules in file order.</param>
    /// <returns>A new sorted list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<TRule> MostSpecificFirst<TRule>(IEnumerable<TRule> rules)
        where TRule : WindowRule
    {
        ArgumentNullException.ThrowIfNull(rules);
        return [.. rules.OrderByDescending(static rule => rule.Specificity)];
    }

    /// <summary>
    /// Reads the first of the keys that holds a string or an array. An array keeps only its string members.
    /// </summary>
    /// <param name="table">The rule table.</param>
    /// <param name="keys">The keys to try, in order.</param>
    /// <returns>The strings, or <see langword="null"/> when no key holds a string or an array.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="keys"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Reads a Steam game ID or a list of them from a table key.
    /// </summary>
    /// <param name="table">The rule table.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="log">The logger for warnings.</param>
    /// <returns>The IDs, or <see langword="null"/> when the key is missing or invalid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    public static uint[]? SteamGameIds(TomlTable table, string key, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.TryGetValue(key, out var value) ? SteamGameIds(value, key, log) : null;
    }

    /// <summary>
    /// Reads a Steam game ID or a list of them. It warns when the value is not a positive integer or a list of them.
    /// </summary>
    /// <param name="value">The TOML value, or <see langword="null"/>.</param>
    /// <param name="where">The key path for the warning.</param>
    /// <param name="log">The logger for warnings.</param>
    /// <returns>The IDs, or <see langword="null"/> when the value is missing or invalid.</returns>
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

    /// <summary>
    /// Compiles the regular expression in a string key. It warns when the pattern is invalid.
    /// </summary>
    /// <param name="table">The rule table.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="log">The logger for warnings.</param>
    /// <returns>The pattern, or <see langword="null"/> when the key is missing, empty or invalid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
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
