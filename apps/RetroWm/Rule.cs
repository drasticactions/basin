using System.Text.RegularExpressions;
using Basin.WindowManager;

namespace RetroWm;

internal sealed class Rule
{
    public string[]? AppIds { get; init; }

    public string[]? AppIdPrefixes { get; init; }

    public string[]? Titles { get; init; }

    public Regex? AppIdRegex { get; init; }

    public Regex? TitleRegex { get; init; }

    public bool? RequireCsdOnly { get; init; }

    public bool? RequireNoParent { get; init; }

    public bool ForceSsd { get; init; }

    public int? SwallowTop { get; init; }

    public bool Matches(string? appId, string? title, DecorationHint hint, bool hasParent)
    {
        var hasAppCriteria = AppIds is not null || AppIdRegex is not null || AppIdPrefixes is not null;
        var hasTitleCriteria = Titles is not null || TitleRegex is not null;
        if (!hasAppCriteria && !hasTitleCriteria)
        {
            if (!string.IsNullOrEmpty(appId) && !string.IsNullOrEmpty(title))
            {
                return false;
            }
        }

        if (hasAppCriteria)
        {
            if (appId is null)
            {
                return false;
            }

            var appMatched = (AppIds is not null && Array.IndexOf(AppIds, appId) >= 0)
                || AppIdRegex?.IsMatch(appId) == true
                || (AppIdPrefixes is not null
                    && AppIdPrefixes.Any(prefix => appId.StartsWith(prefix, StringComparison.Ordinal)));
            if (!appMatched)
            {
                return false;
            }
        }

        if (Titles is not null && title is not null)
        {
            if (Array.IndexOf(Titles, title) < 0)
            {
                return false;
            }
        }
        else if (TitleRegex is not null)
        {
            if (title is null || !TitleRegex.IsMatch(title))
            {
                return false;
            }
        }
        else if (hasTitleCriteria)
        {
            return false;
        }

        var csdOnly = hint == DecorationHint.OnlySupportsClientSide;
        if (RequireCsdOnly is { } requireCsd && requireCsd != csdOnly)
        {
            return false;
        }

        if (RequireNoParent is { } requireToplevel && requireToplevel == hasParent)
        {
            return false;
        }

        return true;
    }
}
