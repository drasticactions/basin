using System.Globalization;

namespace Basin.Frames.Metacity;

internal sealed class MetacityColorSpec
{
    private MetacityColorSpec(MetacityColorSpecKind kind) => Kind = kind;

    public MetacityColorSpecKind Kind { get; }

    public int Index { get; set; } = -1;

    public MetacityColor Basic { get; private init; }

    public MetacityGtkComponent Component { get; private init; }

    public MetacityStateFlag State { get; private init; }

    public string? CustomName { get; private init; }

    public MetacityColorSpec? Fallback { get; private init; }

    public MetacityColorSpec? Background { get; private init; }

    public MetacityColorSpec? Foreground { get; private init; }

    public double Alpha { get; private init; }

    public MetacityColorSpec? Base { get; private init; }

    public double Factor { get; private init; }

    public MetacityColor Resolve(MetacityPalette palette) => Kind switch
    {
        MetacityColorSpecKind.Basic => Basic,
        MetacityColorSpecKind.Gtk => palette.Component(Component, State),
        MetacityColorSpecKind.GtkCustom => palette.Custom.TryGetValue(CustomName!, out var custom) ? custom : Fallback!.Resolve(palette),
        MetacityColorSpecKind.Blend => Background!.Resolve(palette).Blend(Foreground!.Resolve(palette), Alpha),
        _ => Base!.Resolve(palette).Shade(Factor),
    };

    public static MetacityColorSpec Parse(string str, out string? error)
    {
        error = null;
        if (str.StartsWith("gtk:custom", StringComparison.Ordinal))
        {
            if (str.Length < 11 || str[10] != '(')
            {
                error = $"GTK custom color specification must have color name and fallback in parentheses, e.g. gtk:custom(foo,bar); could not parse \"{str}\"";
                return Failed();
            }

            var nameStart = 11;
            var comma = nameStart;
            while (comma < str.Length && str[comma] != ',')
            {
                var ch = str[comma];
                if (!(char.IsAsciiLetterOrDigit(ch) || ch == '-' || ch == '_'))
                {
                    error = $"Invalid character '{ch}' in color_name parameter of gtk:custom, only A-Za-z0-9-_ are valid";
                    return Failed();
                }

                comma++;
            }

            var fallbackStart = comma + 1;
            var end = str.LastIndexOf(')');
            if (end < 0 || fallbackStart > str.Length)
            {
                error = $"Gtk:custom format is \"gtk:custom(color_name,fallback)\", \"{str}\" does not fit the format";
                return Failed();
            }

            var fallbackText = end >= fallbackStart ? str[fallbackStart..end] : string.Empty;
            var fallback = Parse(fallbackText, out error);
            if (error is not null)
            {
                return Failed();
            }

            var nameLength = Math.Max(0, Math.Min(comma, str.Length) - nameStart);
            return new MetacityColorSpec(MetacityColorSpecKind.GtkCustom)
            {
                CustomName = str.Substring(nameStart, nameLength),
                Fallback = fallback,
            };
        }

        if (str.StartsWith("gtk:", StringComparison.Ordinal))
        {
            var bracket = str.IndexOf('[');
            if (bracket < 0)
            {
                error = $"GTK color specification must have the state in brackets, e.g. gtk:fg[NORMAL] where NORMAL is the state; could not parse \"{str}\"";
                return Failed();
            }

            var endBracket = str.IndexOf(']', bracket + 1);
            if (endBracket < 0)
            {
                error = $"GTK color specification must have a close bracket after the state, e.g. gtk:fg[NORMAL] where NORMAL is the state; could not parse \"{str}\"";
                return Failed();
            }

            var stateText = str[(bracket + 1)..endBracket];
            if (!TryParseState(stateText, out var state))
            {
                error = $"Did not understand state \"{stateText}\" in color specification";
                return Failed();
            }

            var componentText = str[4..bracket];
            if (!TryParseComponent(componentText, out var component))
            {
                error = $"Did not understand color component \"{componentText}\" in color specification";
                return Failed();
            }

            return new MetacityColorSpec(MetacityColorSpecKind.Gtk) { Component = component, State = state };
        }

        if (str.StartsWith("blend/", StringComparison.Ordinal))
        {
            var split = str.Split('/', 4);
            if (split.Length < 4)
            {
                error = $"Blend format is \"blend/bg_color/fg_color/alpha\", \"{str}\" does not fit the format";
                return Failed();
            }

            if (!TryParseLeadingDouble(split[3], out var alpha))
            {
                error = $"Could not parse alpha value \"{split[3]}\" in blended color";
                return Failed();
            }

            if (alpha < 0.0 - 1e6 || alpha > 1.0 + 1e6)
            {
                error = $"Alpha value \"{split[3]}\" in blended color is not between 0.0 and 1.0";
                return Failed();
            }

            var bg = Parse(split[1], out error);
            if (error is not null)
            {
                return Failed();
            }

            var fg = Parse(split[2], out error);
            if (error is not null)
            {
                return Failed();
            }

            return new MetacityColorSpec(MetacityColorSpecKind.Blend) { Background = bg, Foreground = fg, Alpha = alpha };
        }

        if (str.StartsWith("shade/", StringComparison.Ordinal))
        {
            var split = str.Split('/', 3);
            if (split.Length < 3)
            {
                error = $"Shade format is \"shade/base_color/factor\", \"{str}\" does not fit the format";
                return Failed();
            }

            if (!TryParseLeadingDouble(split[2], out var factor))
            {
                error = $"Could not parse shade factor \"{split[2]}\" in shaded color";
                return Failed();
            }

            if (factor < 0.0 - 1e6)
            {
                error = $"Shade factor \"{split[2]}\" in shaded color is negative";
                return Failed();
            }

            var @base = Parse(split[1], out error);
            if (error is not null)
            {
                return Failed();
            }

            return new MetacityColorSpec(MetacityColorSpecKind.Shade) { Base = @base, Factor = factor };
        }

        if (!TryParseBasic(str, out var color))
        {
            error = $"Could not parse color \"{str}\"";
            return Failed();
        }

        return new MetacityColorSpec(MetacityColorSpecKind.Basic) { Basic = color };
    }

    private static MetacityColorSpec Failed() => new(MetacityColorSpecKind.Basic);

    private static bool TryParseLeadingDouble(string text, out double value)
    {
        var end = 0;
        var span = text.AsSpan().TrimStart();
        while (end < span.Length && (char.IsAsciiDigit(span[end]) || span[end] is '.' or '+' or '-' or 'e' or 'E'))
        {
            end++;
        }

        while (end > 0 && !double.TryParse(span[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            end--;
        }

        if (end == 0)
        {
            value = 0;
            return false;
        }

        value = double.Parse(span[..end], NumberStyles.Float, CultureInfo.InvariantCulture);
        return true;
    }

    public static bool TryParseState(string text, out MetacityStateFlag state)
    {
        switch (text.ToLowerInvariant())
        {
            case "normal":
                state = MetacityStateFlag.Normal;
                return true;
            case "prelight":
                state = MetacityStateFlag.Prelight;
                return true;
            case "active":
                state = MetacityStateFlag.Active;
                return true;
            case "selected":
                state = MetacityStateFlag.Selected;
                return true;
            case "insensitive":
                state = MetacityStateFlag.Insensitive;
                return true;
            case "inconsistent":
                state = MetacityStateFlag.Inconsistent;
                return true;
            case "focused":
                state = MetacityStateFlag.Focused;
                return true;
            case "backdrop":
                state = MetacityStateFlag.Backdrop;
                return true;
            default:
                state = default;
                return false;
        }
    }

    private static bool TryParseComponent(string text, out MetacityGtkComponent component)
    {
        switch (text)
        {
            case "fg":
                component = MetacityGtkComponent.Fg;
                return true;
            case "bg":
                component = MetacityGtkComponent.Bg;
                return true;
            case "light":
                component = MetacityGtkComponent.Light;
                return true;
            case "dark":
                component = MetacityGtkComponent.Dark;
                return true;
            case "mid":
                component = MetacityGtkComponent.Mid;
                return true;
            case "text":
                component = MetacityGtkComponent.Text;
                return true;
            case "base":
                component = MetacityGtkComponent.Base;
                return true;
            case "text_aa":
                component = MetacityGtkComponent.TextAa;
                return true;
            default:
                component = default;
                return false;
        }
    }

    public static bool TryParseBasic(string text, out MetacityColor color)
    {
        color = default;
        var s = text.Trim();
        if (s.Length == 0)
        {
            return false;
        }

        if (s[0] == '#')
        {
            var hex = s.AsSpan(1);
            if (hex.Length is not (3 or 6 or 9 or 12))
            {
                return false;
            }

            var digits = hex.Length / 3;
            var max = (1 << (4 * digits)) - 1;
            Span<double> channels = stackalloc double[3];
            for (var i = 0; i < 3; i++)
            {
                if (!int.TryParse(hex.Slice(i * digits, digits), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
                {
                    return false;
                }

                channels[i] = value / (double)max;
            }

            color = new MetacityColor(channels[0], channels[1], channels[2], 1.0);
            return true;
        }

        if (s.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) || s.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
        {
            var hasAlpha = s[3] is 'a' or 'A';
            var open = s.IndexOf('(');
            if (!s.EndsWith(')'))
            {
                return false;
            }

            var parts = s[(open + 1)..^1].Split(',');
            if (parts.Length != (hasAlpha ? 4 : 3))
            {
                return false;
            }

            Span<double> channels = stackalloc double[4];
            channels[3] = 1.0;
            for (var i = 0; i < 3; i++)
            {
                var part = parts[i].Trim();
                if (part.EndsWith('%'))
                {
                    if (!double.TryParse(part[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
                    {
                        return false;
                    }

                    channels[i] = Math.Clamp(percent / 100.0, 0.0, 1.0);
                }
                else
                {
                    if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        return false;
                    }

                    channels[i] = Math.Clamp(value / 255.0, 0.0, 1.0);
                }
            }

            if (hasAlpha)
            {
                if (!double.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var alpha))
                {
                    return false;
                }

                channels[3] = Math.Clamp(alpha, 0.0, 1.0);
            }

            color = new MetacityColor(channels[0], channels[1], channels[2], channels[3]);
            return true;
        }

        return MetacityColorNames.TryGet(s, out color);
    }
}
