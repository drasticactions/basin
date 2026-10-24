using Basin.Diagnostics;
using Tomlyn.Model;

namespace Basin.Config;

/// <summary>
/// Parses key bindings from configuration files and formats them as text.
/// </summary>
public static class HotkeyParser
{
    /// <summary>
    /// Parses a chord such as <c>Super+Shift+Return</c>. Modifier names are case-insensitive and the last token is the keysym name.
    /// Each failure writes a warning to the log.
    /// </summary>
    /// <param name="chord">The chord text, with tokens separated by <c>+</c>.</param>
    /// <param name="log">The logger for warnings.</param>
    /// <param name="keysym">The keysym, or <see cref="Keysym.NoSymbol"/> on failure.</param>
    /// <param name="modifiers">The modifiers. They can be partly set on failure.</param>
    /// <returns><see langword="false"/> when the chord is empty, names an unknown modifier or names an unknown keysym.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="chord"/> is <see langword="null"/>.</exception>
    public static bool TryParseChord(string chord, BasinLogger log, out uint keysym, out Modifiers modifiers)
    {
        ArgumentNullException.ThrowIfNull(chord);
        keysym = Keysym.NoSymbol;
        modifiers = Modifiers.None;

        var tokens = chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            log.Warn($"binding '{chord}' names no key, skipping");
            return false;
        }

        for (var i = 0; i < tokens.Length - 1; i++)
        {
            switch (tokens[i].ToLowerInvariant())
            {
                case "shift":
                    modifiers |= Modifiers.Shift;
                    break;
                case "ctrl" or "control":
                    modifiers |= Modifiers.Ctrl;
                    break;
                case "alt" or "mod1":
                    modifiers |= Modifiers.Alt;
                    break;
                case "super" or "logo" or "win" or "mod4":
                    modifiers |= Modifiers.Super;
                    break;
                case "mod3":
                    modifiers |= Modifiers.Mod3;
                    break;
                case "mod5":
                    modifiers |= Modifiers.Mod5;
                    break;
                default:
                    log.Warn($"unknown modifier '{tokens[i]}' in binding '{chord}', skipping");
                    return false;
            }
        }

        keysym = Keysym.FromName(tokens[^1]);
        if (keysym == Keysym.NoSymbol)
        {
            log.Warn($"unknown keysym '{tokens[^1]}' in binding '{chord}', skipping");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Formats a binding in the form that <see cref="TryParseChord"/> reads, for example <c>Ctrl+Alt+Delete</c>.
    /// </summary>
    /// <param name="keysym">The xkb keysym.</param>
    /// <param name="modifiers">The modifiers. They come out in the order Ctrl, Alt, Shift, Super, Mod3, Mod5.</param>
    /// <returns>The chord text.</returns>
    public static string Format(uint keysym, Modifiers modifiers)
    {
        var builder = new System.Text.StringBuilder();
        Append(builder, modifiers, Modifiers.Ctrl, "Ctrl");
        Append(builder, modifiers, Modifiers.Alt, "Alt");
        Append(builder, modifiers, Modifiers.Shift, "Shift");
        Append(builder, modifiers, Modifiers.Super, "Super");
        Append(builder, modifiers, Modifiers.Mod3, "Mod3");
        Append(builder, modifiers, Modifiers.Mod5, "Mod5");
        return builder.Append(new Xkb.XkbKeysym(keysym).Name).ToString();
    }

    /// <summary>
    /// Parses one binding entry. The value can be <see langword="false"/> or <c>"none"</c> to unbind, an action name, a command string or array, or a table with <c>when</c>, <c>action</c> or <c>exec</c>.
    /// </summary>
    /// <param name="chord">The chord text from the key of the entry.</param>
    /// <param name="value">The TOML value of the entry.</param>
    /// <param name="log">The logger for warnings.</param>
    /// <param name="isAction">Tells whether a string names an action. If <see langword="null"/>, every string is a command.</param>
    /// <returns>The binding, or <see langword="null"/> when the chord does not parse or the value names nothing.</returns>
    public static Hotkey? Parse(string chord, object? value, BasinLogger log, Func<string, bool>? isAction = null)
    {
        if (!TryParseChord(chord, log, out var keysym, out var modifiers))
        {
            return null;
        }

        var when = HotkeyScope.Always;
        if (value is TomlTable table)
        {
            if (table.TryGetValue("when", out var scope))
            {
                when = scope switch
                {
                    "always" => HotkeyScope.Always,
                    "windowed" => HotkeyScope.Windowed,
                    "fullscreen" => HotkeyScope.Fullscreen,
                    _ => Warn(log, chord, scope),
                };
            }

            if (table.TryGetValue("action", out var action))
            {
                value = action;
            }
        }

        if (value is false or "none")
        {
            return new Hotkey(keysym, modifiers, null, null);
        }

        if (value is string named && isAction?.Invoke(named) == true)
        {
            return new Hotkey(keysym, modifiers, named, null) { When = when };
        }

        var command = Words(value);
        if (command.Length == 0)
        {
            log.Warn($"binding '{chord}' names no action and no command, skipping");
            return null;
        }

        return new Hotkey(keysym, modifiers, null, command) { When = when };
    }

    private static HotkeyScope Warn(BasinLogger log, string chord, object? scope)
    {
        log.Warn($"binding '{chord}' when = \"{scope}\" is not always, windowed or fullscreen; always is used");
        return HotkeyScope.Always;
    }

    private static string[] Words(object? value) => value switch
    {
        string text => text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        TomlArray array => [.. array.OfType<string>().Select(static part => part.Trim()).Where(static part => part.Length > 0)],
        TomlTable table when table.TryGetValue("exec", out var exec) => Words(exec),
        _ => [],
    };

    private static void Append(System.Text.StringBuilder builder, Modifiers held, Modifiers flag, string name)
    {
        if ((held & flag) != 0)
        {
            builder.Append(name).Append('+');
        }
    }
}
