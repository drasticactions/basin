using System.Text.RegularExpressions;

namespace TinyComp;

internal static partial class SettingsMessages
{
    public static (string Text, bool Blocking) Explain(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (ExpectedOneOf().Match(raw) is { Success: true } oneOf)
        {
            return (ChooseOneOf(oneOf.Groups["list"].Value.Split(", ")), true);
        }

        if (IsNotAlternatives().Match(raw) is { Success: true } alternatives)
        {
            return (ChooseOneOf(alternatives.Groups["list"].Value.Split('|')), true);
        }

        if (raw.Contains("expected true or false", StringComparison.Ordinal))
        {
            return ("Use true or false.", true);
        }

        if (raw.Contains("expected a whole number", StringComparison.Ordinal))
        {
            return ("Enter a whole number.", true);
        }

        if (raw.Contains("expected a number or an array of numbers", StringComparison.Ordinal)
            || raw.Contains("expected an array of numbers", StringComparison.Ordinal))
        {
            return ("Enter a number, or a list of numbers.", true);
        }

        if (raw.Contains("expected a string", StringComparison.Ordinal))
        {
            return ("Put text in quotes, like \"warp\".", true);
        }

        if (raw.Contains("is not #rrggbb", StringComparison.Ordinal))
        {
            return ("Use a color like #1a2b3c.", true);
        }

        if (MustBeAtLeast().Match(raw) is { Success: true } least)
        {
            return ($"Use {least.Groups["floor"].Value} or more.", true);
        }

        if (raw.Contains("neither app_id nor title_regex", StringComparison.Ordinal))
        {
            return ("A rule needs an app id or a title pattern.", true);
        }

        if (raw.StartsWith("rule pattern", StringComparison.Ordinal))
        {
            return ("That title pattern isn't valid.", true);
        }

        if (UnknownKeysym().Match(raw) is { Success: true } keysym)
        {
            return ($"\"{keysym.Groups["name"].Value}\" isn't a key name.", true);
        }

        if (UnknownModifier().Match(raw) is { Success: true } modifier)
        {
            return ($"\"{modifier.Groups["name"].Value}\" isn't a modifier. Use Ctrl, Alt, Shift or Super.", true);
        }

        if (raw.Contains("names no action and no command", StringComparison.Ordinal))
        {
            return ("Pick an action or enter a command.", true);
        }

        if (raw.Contains("names no key", StringComparison.Ordinal))
        {
            return ("Add a key to the shortcut.", true);
        }

        if (raw.Contains("is not WIDTHxHEIGHT", StringComparison.Ordinal))
        {
            return ("Use a mode like 1920x1080 or 1920x1080@60.", true);
        }

        if (raw.Contains("names no theme in [frame.metacity] theme", StringComparison.Ordinal))
        {
            return ("Pick a Metacity theme under the style.", true);
        }

        if (raw.StartsWith("[frame.metacity] theme", StringComparison.Ordinal))
        {
            return ("That theme couldn't be loaded.", true);
        }

        if (raw.Contains("names no profile in [color] icc", StringComparison.Ordinal))
        {
            return ("Choose an ICC profile file first.", true);
        }

        if (raw.StartsWith("effects.post: unknown stage", StringComparison.Ordinal))
        {
            return ("That isn't a post stage.", true);
        }

        if (raw.Contains("an entry names no path", StringComparison.Ordinal))
        {
            return ("Each preset needs a file.", true);
        }

        if (raw.Contains("sides is empty", StringComparison.Ordinal))
        {
            return ("Pick at least one side.", true);
        }

        if (raw.Contains("is not app_id:id", StringComparison.Ordinal))
        {
            return ("Name it app_id:id, like org.example.app:toggle.", true);
        }

        if (raw.Contains("names no chord", StringComparison.Ordinal))
        {
            return ("Set a key combination for this shortcut.", true);
        }

        if (raw.Contains("is not a number", StringComparison.Ordinal))
        {
            return ("Enter a number.", true);
        }

        if (raw.Contains("unknown key or wrong type", StringComparison.Ordinal) || raw.StartsWith("unknown key", StringComparison.Ordinal))
        {
            return ("This value isn't the right kind for this setting.", true);
        }

        if (raw.Contains("did not parse", StringComparison.Ordinal))
        {
            return ("That value can't be read.", true);
        }

        if (EdgeScaleCap().Match(raw) is { Success: true } cap)
        {
            return ($"Too high for this zone and extension, so {cap.Groups["value"].Value} is used.", false);
        }

        if (raw.Contains("has no effect", StringComparison.Ordinal))
        {
            return ("This has no effect with the current edge scale.", false);
        }

        if (raw.Contains("must be below threshold_in", StringComparison.Ordinal))
        {
            return ("Threshold out should be lower than threshold in, so they are swapped.", false);
        }

        if (raw.Contains("leave no flat center", StringComparison.Ordinal))
        {
            return ("The shelf and zone leave no room in the middle, so both are scaled down.", false);
        }

        if (raw.Contains("is below zone", StringComparison.Ordinal))
        {
            return ("The extension can't be smaller than the zone, so it is raised.", false);
        }

        if (raw.Contains("workspace swipe's count", StringComparison.Ordinal))
        {
            return ("Three fingers is the workspace swipe, so four are used.", false);
        }

        if (raw.Contains("overview clamps it", StringComparison.Ordinal))
        {
            return ("Overview caps the shelf scale at its own scale.", false);
        }

        if (raw.Contains("overview is off on every output with the canvas enabled", StringComparison.Ordinal))
        {
            return ("Overview is off wherever the canvas is on.", false);
        }

        if (raw.Contains("wall = \"step\" ignores", StringComparison.Ordinal))
        {
            return ("The step wall ignores some overview settings.", false);
        }

        if (raw.Contains("overrides corner", StringComparison.Ordinal))
        {
            return ("The corner radius takes the place of the corner shape.", false);
        }

        if (raw.Contains("the fit shrink is off", StringComparison.Ordinal))
        {
            return ("Windows too wide for the shelf won't shrink to fit.", false);
        }

        if (raw.Contains("is ignored with [[effects.shader]]", StringComparison.Ordinal))
        {
            return ("Set parameters on each preset in the chain instead.", false);
        }

        return (Plain(raw), true);
    }

    private static string ChooseOneOf(IReadOnlyList<string> choices)
    {
        var names = choices.Select(static choice => choice.Trim().Trim('"')).Where(static choice => choice.Length > 0).ToArray();
        return names.Length switch
        {
            0 => "Choose one of the listed values.",
            1 => $"Use {names[0]}.",
            _ => $"Choose {string.Join(", ", names[..^1])} or {names[^1]}.",
        };
    }

    private static string Plain(string raw)
    {
        var text = SectionPrefix().Replace(raw, string.Empty);
        text = Tail().Replace(text, string.Empty).Trim();
        if (text.Length == 0)
        {
            return "That value can't be used.";
        }

        return char.ToUpperInvariant(text[0]) + text[1..] + (text.EndsWith('.') ? string.Empty : ".");
    }

    [GeneratedRegex(@"expected one of (?<list>.+?), keeping")]
    private static partial Regex ExpectedOneOf();

    [GeneratedRegex(@"is not (?<list>[\w-]+(\|[\w-]+)+)")]
    private static partial Regex IsNotAlternatives();

    [GeneratedRegex(@"must be at least (?<floor>[\d.]+)")]
    private static partial Regex MustBeAtLeast();

    [GeneratedRegex(@"unknown keysym '(?<name>[^']*)'")]
    private static partial Regex UnknownKeysym();

    [GeneratedRegex(@"unknown modifier '(?<name>[^']*)'")]
    private static partial Regex UnknownModifier();

    [GeneratedRegex(@"clamping it to (?<value>[\d.]+)")]
    private static partial Regex EdgeScaleCap();

    [GeneratedRegex(@"^\[[^\]]*\]\s*|^[\w.]+:\s*")]
    private static partial Regex SectionPrefix();

    [GeneratedRegex(@",?\s*(ignored|skipping|keeping [^,]*)\s*$")]
    private static partial Regex Tail();
}
