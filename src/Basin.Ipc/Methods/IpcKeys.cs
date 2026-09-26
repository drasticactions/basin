using Basin.Capabilities;
using Basin.Config;

namespace Basin.Ipc;

internal static class IpcKeys
{
    private const uint ShiftMask = 1;
    private const uint Level3Mask = 128;

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
            error = "the chord names no key";
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
                error = $"'{chord}' is not a chord";
                return false;
            }

            keysym = bare;
        }
        else if (!HotkeyParser.TryParseChord(chord, IpcLog.Log, out keysym, out modifiers))
        {
            error = $"'{chord}' is not a chord";
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
                error = $"the keymap has no {name} key";
                return false;
            }

            codes.Add(modifierCode);
        }

        if ((modifiers & Modifiers.Mod3) != 0)
        {
            error = "mod3 has no key to press";
            return false;
        }

        if (!lookup.TryKeycodeForKeysym(keysym, out var code, out _))
        {
            error = $"the keymap cannot produce the key of '{chord}'";
            return false;
        }

        codes.Add(code);
        return true;
    }

    public static bool TryResolveText(string text, IKeymapLookup lookup, List<(uint Code, uint Mask)> keys, out string? error)
    {
        keys.Clear();
        error = null;
        foreach (var rune in text.EnumerateRunes())
        {
            var keysym = rune.Value switch
            {
                '\n' or '\r' => 0xff0du,
                '\t' => 0xff09u,
                '\b' => 0xff08u,
                >= 0x20 and <= 0x7e or >= 0xa0 and <= 0xff => (uint)rune.Value,
                _ => 0x01000000u | (uint)rune.Value,
            };

            if (!lookup.TryKeycodeForKeysym(keysym, out var code, out var mask) || (mask & ~(ShiftMask | Level3Mask)) != 0)
            {
                error = $"the keymap cannot type '{rune}'";
                return false;
            }

            keys.Add((code, mask));
        }

        return true;
    }

    public static uint? ModifierCode(IKeymapLookup lookup, uint mask) =>
        lookup.TryKeycodeForKeysym(
            KeysymOfModifier(mask == ShiftMask ? "shift" : "mod5"), out var code, out _) ? code : null;
}
