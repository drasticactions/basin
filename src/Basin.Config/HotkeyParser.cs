using Basin.Diagnostics;
using Tomlyn.Model;

namespace Basin.Config;

public static class HotkeyParser
{
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

    public static Hotkey? Parse(string chord, object? value, BasinLogger log, Func<string, bool>? isAction = null)
    {
        if (!TryParseChord(chord, log, out var keysym, out var modifiers))
        {
            return null;
        }

        if (value is false or "none")
        {
            return new Hotkey(keysym, modifiers, null, null);
        }

        if (value is string named && isAction?.Invoke(named) == true)
        {
            return new Hotkey(keysym, modifiers, named, null);
        }

        var command = Words(value);
        if (command.Length == 0)
        {
            log.Warn($"binding '{chord}' names no action and no command, skipping");
            return null;
        }

        return new Hotkey(keysym, modifiers, null, command);
    }

    private static string[] Words(object? value) => value switch
    {
        string text => text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        TomlArray array => [.. array.OfType<string>().Select(static part => part.Trim()).Where(static part => part.Length > 0)],
        TomlTable table when table.TryGetValue("exec", out var exec) => Words(exec),
        _ => [],
    };
}
