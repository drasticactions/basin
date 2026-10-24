namespace Basin.AppMenu;

/// <summary>
/// The key chord that a menu item shows: modifiers and one xkb keysym name. A single-letter keysym is lowercase.
/// </summary>
/// <param name="Modifiers">The modifier keys that the chord holds.</param>
/// <param name="Keysym">The xkb keysym name of the key, for example <c>q</c> or <c>F5</c>.</param>
public readonly record struct AppMenuShortcut(AppMenuModifiers Modifiers, string Keysym)
{
    /// <summary>
    /// Reads one chord in the dbusmenu list form, for example <c>["Control", "q"]</c>. The last part that is not a modifier is the key.
    /// </summary>
    /// <param name="parts">The modifier names and the key name.</param>
    /// <returns>The shortcut, or <see langword="null"/> when no part names a key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parts"/> is <see langword="null"/>.</exception>
    public static AppMenuShortcut? FromDbusMenu(IReadOnlyList<string> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var modifiers = AppMenuModifiers.None;
        string? key = null;
        foreach (var part in parts)
        {
            var modifier = ModifierNamed(part);
            if (modifier != AppMenuModifiers.None)
            {
                modifiers |= modifier;
            }
            else
            {
                key = part;
            }
        }

        return key is { Length: > 0 } ? new AppMenuShortcut(modifiers, Normalize(key)) : null;
    }

    /// <summary>
    /// Reads a GTK accelerator: modifier names in angle brackets, then the key name. The modifier name <c>Primary</c> means Control.
    /// </summary>
    /// <param name="accelerator">The accelerator text.</param>
    /// <returns>The shortcut, or <see langword="null"/> when a modifier bracket is not closed or no key follows the modifiers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="accelerator"/> is <see langword="null"/>.</exception>
    public static AppMenuShortcut? FromGtkAccelerator(string accelerator)
    {
        ArgumentNullException.ThrowIfNull(accelerator);
        var modifiers = AppMenuModifiers.None;
        var rest = accelerator.AsSpan().Trim();
        while (rest.Length > 0 && rest[0] == '<')
        {
            var end = rest.IndexOf('>');
            if (end < 0)
            {
                return null;
            }

            modifiers |= ModifierNamed(rest[1..end].ToString());
            rest = rest[(end + 1)..];
        }

        return rest.Length > 0 ? new AppMenuShortcut(modifiers, Normalize(rest.ToString())) : null;
    }

    private static string Normalize(string key) => key.Length == 1 ? key.ToLowerInvariant() : key;

    private static AppMenuModifiers ModifierNamed(string name) => name.ToLowerInvariant() switch
    {
        "control" or "ctrl" or "primary" => AppMenuModifiers.Control,
        "shift" => AppMenuModifiers.Shift,
        "alt" or "mod1" => AppMenuModifiers.Alt,
        "super" or "meta" or "mod4" or "logo" => AppMenuModifiers.Super,
        _ => AppMenuModifiers.None,
    };
}
