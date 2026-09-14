using Xkb;

namespace Basin.Config;

public static class TriggerSyntax
{
    public static bool TryParse(string trigger, out uint keysym, out Modifiers modifiers)
    {
        keysym = Keysym.NoSymbol;
        modifiers = Modifiers.None;
        if (string.IsNullOrWhiteSpace(trigger))
        {
            return false;
        }

        var tokens = trigger.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < tokens.Length - 1; i++)
        {
            switch (tokens[i].ToUpperInvariant())
            {
                case "SHIFT":
                    modifiers |= Modifiers.Shift;
                    break;
                case "CTRL" or "CONTROL":
                    modifiers |= Modifiers.Ctrl;
                    break;
                case "ALT":
                    modifiers |= Modifiers.Alt;
                    break;
                case "LOGO" or "SUPER" or "META":
                    modifiers |= Modifiers.Super;
                    break;
                default:
                    keysym = Keysym.NoSymbol;
                    modifiers = Modifiers.None;
                    return false;
            }
        }

        keysym = Keysym.FromName(tokens[^1]);
        if (keysym == Keysym.NoSymbol)
        {
            modifiers = Modifiers.None;
            return false;
        }

        return true;
    }

    public static string Format(uint keysym, Modifiers modifiers)
    {
        var name = new XkbKeysym(keysym).ToString();
        if (string.IsNullOrEmpty(name))
        {
            return "";
        }

        var parts = new List<string>(5);
        if ((modifiers & Modifiers.Ctrl) != 0)
        {
            parts.Add("CTRL");
        }

        if ((modifiers & Modifiers.Alt) != 0)
        {
            parts.Add("ALT");
        }

        if ((modifiers & Modifiers.Shift) != 0)
        {
            parts.Add("SHIFT");
        }

        if ((modifiers & Modifiers.Super) != 0)
        {
            parts.Add("LOGO");
        }

        parts.Add(name);
        return string.Join('+', parts);
    }

    public static string Describe(uint keysym, Modifiers modifiers)
    {
        var name = new XkbKeysym(keysym).ToString();
        if (string.IsNullOrEmpty(name))
        {
            return "";
        }

        var parts = new List<string>(5);
        if ((modifiers & Modifiers.Ctrl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & Modifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & Modifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & Modifiers.Super) != 0)
        {
            parts.Add("Super");
        }

        parts.Add(name);
        return string.Join('+', parts);
    }
}
