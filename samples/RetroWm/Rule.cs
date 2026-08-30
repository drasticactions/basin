using Basin.Config;
using Basin.WindowManager;

namespace RetroWm;

internal sealed class Rule : WindowRule
{
    public bool? RequireCsdOnly { get; init; }

    public bool? RequireNoParent { get; init; }

    public bool ForceSsd { get; init; }

    public int? SwallowTop { get; init; }

    public bool Matches(string? appId, string? title, DecorationHint hint, bool hasParent)
    {
        if (!MatchesText(appId, title))
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
