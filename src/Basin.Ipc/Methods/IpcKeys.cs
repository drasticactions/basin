using Basin.Capabilities;
using Basin.Config;

namespace Basin.Ipc;

internal static class IpcKeys
{
    public static uint KeysymOfModifier(string name) => name.ToLowerInvariant() switch
    {
        "shift" => Keysym.FromName("Shift_L"),
        "ctrl" or "control" => Keysym.FromName("Control_L"),
        "alt" or "mod1" => Keysym.FromName("Alt_L"),
        "super" or "logo" or "win" or "mod4" => Keysym.FromName("Super_L"),
        "mod5" => Keysym.FromName("ISO_Level3_Shift"),
        _ => Keysym.NoSymbol,
    };

    public static bool TryResolveChord(string chord, IKeymapLookup lookup, List<uint> codes, out string? error)
    {
        codes.Clear();
        error = null;
        var tokens = chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            error = "The chord has no key. Give a chord such as super+Return.";
            return false;
        }

        uint keysym;
        Modifiers modifiers;
        if (KeysymOfModifier(tokens[^1]) is var bare and not Keysym.NoSymbol)
        {
            var rest = string.Join('+', tokens[..^1]);
            modifiers = Modifiers.None;
            if (rest.Length > 0 && !HotkeyParser.TryParseChord(rest + "+a", IpcLog.Log, out _, out modifiers))
            {
                error = $"'{chord}' is not a chord. Give modifiers and one key joined by '+', for example super+Return.";
                return false;
            }

            keysym = bare;
        }
        else if (!HotkeyParser.TryParseChord(chord, IpcLog.Log, out keysym, out modifiers))
        {
            error = $"'{chord}' is not a chord. Give modifiers and one key joined by '+', for example super+Return.";
            return false;
        }

        foreach (var (flag, name) in new[]
                 {
                     (Modifiers.Ctrl, "ctrl"), (Modifiers.Alt, "alt"), (Modifiers.Shift, "shift"), (Modifiers.Super, "super"),
                     (Modifiers.Mod5, "mod5"),
                 })
        {
            if ((modifiers & flag) == 0)
            {
                continue;
            }

            if (!lookup.TryKeycodeForKeysym(KeysymOfModifier(name), out var modifierCode, out _))
            {
                error = $"The keymap has no {name} key.";
                return false;
            }

            codes.Add(modifierCode);
        }

        if ((modifiers & Modifiers.Mod3) != 0)
        {
            error = "The keymap has no key for mod3.";
            return false;
        }

        if (!lookup.TryKeycodeForKeysym(keysym, out var code, out _))
        {
            error = $"The keymap cannot produce the key of '{chord}'.";
            return false;
        }

        codes.Add(code);
        return true;
    }
}
