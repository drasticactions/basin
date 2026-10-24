using Xkb;

namespace Basin.Config;

/// <summary>
/// Parses and formats key triggers in the uppercase <c>LOGO+SHIFT+key</c> form.
/// </summary>
public static class TriggerSyntax
{
    /// <summary>
    /// Parses a trigger. Modifier names are SHIFT, CTRL, CONTROL, ALT, LOGO, SUPER and META, in any case. The last token is the keysym name.
    /// </summary>
    /// <param name="trigger">The trigger text, with tokens separated by <c>+</c>.</param>
    /// <param name="keysym">The keysym, or <see cref="Keysym.NoSymbol"/> on failure.</param>
    /// <param name="modifiers">The modifiers, or <see cref="Modifiers.None"/> on failure.</param>
    /// <returns><see langword="false"/> when the text is blank, names an unknown modifier or names an unknown keysym.</returns>
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

    /// <summary>
    /// Formats a trigger that <see cref="TryParse"/> reads, for example <c>CTRL+ALT+Delete</c>. Only Ctrl, Alt, Shift and Super are written.
    /// </summary>
    /// <param name="keysym">The xkb keysym.</param>
    /// <param name="modifiers">The modifiers.</param>
    /// <returns>The trigger text, or an empty string when the keysym has no name.</returns>
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

    /// <summary>
    /// Formats a trigger for display, for example <c>Ctrl+Alt+Delete</c>. Only Ctrl, Alt, Shift and Super are written.
    /// </summary>
    /// <param name="keysym">The xkb keysym.</param>
    /// <param name="modifiers">The modifiers.</param>
    /// <returns>The display text, or an empty string when the keysym has no name.</returns>
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
