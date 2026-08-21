using System.Text;

namespace Basin.Seat;

public static class HostKeymapWriter
{
    public readonly record struct Levels(HostKeyCode Code, string? Plain, string? Shift, string? Level3, string? Level4);

    private static string? _fallback;

    public static string Fallback => _fallback ??= Write("us", UsLevels());

    private static Levels[] UsLevels()
    {
        const string lower = "abcdefghijklmnopqrstuvwxyz";
        var keys = new List<Levels>();

        foreach (var letter in lower)
        {
            var code = Enum.Parse<HostKeyCode>($"Key{char.ToUpperInvariant(letter)}");
            keys.Add(new Levels(code, letter.ToString(), char.ToUpperInvariant(letter).ToString(), null, null));
        }

        (HostKeyCode Code, string Plain, string Shift)[] rest =
        [
            (HostKeyCode.Digit1, "1", "exclam"),
            (HostKeyCode.Digit2, "2", "at"),
            (HostKeyCode.Digit3, "3", "numbersign"),
            (HostKeyCode.Digit4, "4", "dollar"),
            (HostKeyCode.Digit5, "5", "percent"),
            (HostKeyCode.Digit6, "6", "asciicircum"),
            (HostKeyCode.Digit7, "7", "ampersand"),
            (HostKeyCode.Digit8, "8", "asterisk"),
            (HostKeyCode.Digit9, "9", "parenleft"),
            (HostKeyCode.Digit0, "0", "parenright"),
            (HostKeyCode.Minus, "minus", "underscore"),
            (HostKeyCode.Equal, "equal", "plus"),
            (HostKeyCode.BracketLeft, "bracketleft", "braceleft"),
            (HostKeyCode.BracketRight, "bracketright", "braceright"),
            (HostKeyCode.Semicolon, "semicolon", "colon"),
            (HostKeyCode.Quote, "apostrophe", "quotedbl"),
            (HostKeyCode.Backquote, "grave", "asciitilde"),
            (HostKeyCode.Backslash, "backslash", "bar"),
            (HostKeyCode.Comma, "comma", "less"),
            (HostKeyCode.Period, "period", "greater"),
            (HostKeyCode.Slash, "slash", "question"),
            (HostKeyCode.Space, "space", "space"),
        ];

        foreach (var (code, plain, shift) in rest)
        {
            keys.Add(new Levels(code, plain, shift, null, null));
        }

        return [.. keys];
    }

    public static string DeadKeysymName(char value) => value switch
    {
        '`' or 'ˋ' => "dead_grave",
        '\'' or '´' or 'ˊ' => "dead_acute",
        '^' or 'ˆ' => "dead_circumflex",
        '~' or '˜' or '̃' => "dead_tilde",
        '"' or '¨' => "dead_diaeresis",
        '¯' or 'ˉ' => "dead_macron",
        '˘' => "dead_breve",
        '˙' => "dead_abovedot",
        '˚' or '°' => "dead_abovering",
        '˝' => "dead_doubleacute",
        'ˇ' => "dead_caron",
        '¸' => "dead_cedilla",
        '˛' => "dead_ogonek",
        _ => KeysymName(value),
    };

    public static string KeysymName(char value) => value switch
    {
        ' ' => "space",
        '!' => "exclam",
        '"' => "quotedbl",
        '#' => "numbersign",
        '$' => "dollar",
        '%' => "percent",
        '&' => "ampersand",
        '\'' => "apostrophe",
        '(' => "parenleft",
        ')' => "parenright",
        '*' => "asterisk",
        '+' => "plus",
        ',' => "comma",
        '-' => "minus",
        '.' => "period",
        '/' => "slash",
        ':' => "colon",
        ';' => "semicolon",
        '<' => "less",
        '=' => "equal",
        '>' => "greater",
        '?' => "question",
        '@' => "at",
        '[' => "bracketleft",
        '\\' => "backslash",
        ']' => "bracketright",
        '^' => "asciicircum",
        '_' => "underscore",
        '`' => "grave",
        '{' => "braceleft",
        '|' => "bar",
        '}' => "braceright",
        '~' => "asciitilde",
        >= '0' and <= '9' => value.ToString(),
        >= 'a' and <= 'z' => value.ToString(),
        >= 'A' and <= 'Z' => value.ToString(),
        _ => $"U{(int)value:X4}",
    };

    private static readonly (HostKeyCode Code, string Name)[] WritingKeys =
    [
        (HostKeyCode.Backquote, "TLDE"),
        (HostKeyCode.Digit1, "AE01"),
        (HostKeyCode.Digit2, "AE02"),
        (HostKeyCode.Digit3, "AE03"),
        (HostKeyCode.Digit4, "AE04"),
        (HostKeyCode.Digit5, "AE05"),
        (HostKeyCode.Digit6, "AE06"),
        (HostKeyCode.Digit7, "AE07"),
        (HostKeyCode.Digit8, "AE08"),
        (HostKeyCode.Digit9, "AE09"),
        (HostKeyCode.Digit0, "AE10"),
        (HostKeyCode.Minus, "AE11"),
        (HostKeyCode.Equal, "AE12"),
        (HostKeyCode.IntlYen, "AE13"),
        (HostKeyCode.KeyQ, "AD01"),
        (HostKeyCode.KeyW, "AD02"),
        (HostKeyCode.KeyE, "AD03"),
        (HostKeyCode.KeyR, "AD04"),
        (HostKeyCode.KeyT, "AD05"),
        (HostKeyCode.KeyY, "AD06"),
        (HostKeyCode.KeyU, "AD07"),
        (HostKeyCode.KeyI, "AD08"),
        (HostKeyCode.KeyO, "AD09"),
        (HostKeyCode.KeyP, "AD10"),
        (HostKeyCode.BracketLeft, "AD11"),
        (HostKeyCode.BracketRight, "AD12"),
        (HostKeyCode.KeyA, "AC01"),
        (HostKeyCode.KeyS, "AC02"),
        (HostKeyCode.KeyD, "AC03"),
        (HostKeyCode.KeyF, "AC04"),
        (HostKeyCode.KeyG, "AC05"),
        (HostKeyCode.KeyH, "AC06"),
        (HostKeyCode.KeyJ, "AC07"),
        (HostKeyCode.KeyK, "AC08"),
        (HostKeyCode.KeyL, "AC09"),
        (HostKeyCode.Semicolon, "AC10"),
        (HostKeyCode.Quote, "AC11"),
        (HostKeyCode.Backslash, "BKSL"),
        (HostKeyCode.IntlBackslash, "LSGT"),
        (HostKeyCode.KeyZ, "AB01"),
        (HostKeyCode.KeyX, "AB02"),
        (HostKeyCode.KeyC, "AB03"),
        (HostKeyCode.KeyV, "AB04"),
        (HostKeyCode.KeyB, "AB05"),
        (HostKeyCode.KeyN, "AB06"),
        (HostKeyCode.KeyM, "AB07"),
        (HostKeyCode.Comma, "AB08"),
        (HostKeyCode.Period, "AB09"),
        (HostKeyCode.Slash, "AB10"),
        (HostKeyCode.IntlRo, "AB11"),
        (HostKeyCode.Space, "SPCE"),
    ];

    public static bool TryKeycodeName(HostKeyCode code, out string name)
    {
        foreach (var (candidate, spelling) in WritingKeys)
        {
            if (candidate == code)
            {
                name = spelling;
                return true;
            }
        }

        name = string.Empty;
        return false;
    }

    public static string Write(string name, IReadOnlyList<Levels> keys)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(keys);

        var anyLevel3 = false;
        foreach (var key in keys)
        {
            if (key.Level3 is not null || key.Level4 is not null)
            {
                anyLevel3 = true;
                break;
            }
        }

        var text = new StringBuilder();
        text.Append("xkb_keymap {\n");
        text.Append("    xkb_keycodes {\n");
        text.Append("        minimum = 8;\n");
        text.Append("        maximum = 255;\n");

        foreach (var key in keys)
        {
            if (TryKeycodeName(key.Code, out var writing) && HostKeyMap.TryToEvdev(key.Code, out var evdev))
            {
                text.Append($"        <{writing}> = {evdev + 8};\n");
            }
        }

        foreach (var fixedKey in HostKeymapLayout.Keys)
        {
            if (HostKeyMap.TryToEvdev(fixedKey.Code, out var evdev))
            {
                text.Append($"        <{fixedKey.Name}> = {evdev + 8};\n");
            }
        }

        if (anyLevel3 && HostKeyMap.TryToEvdev(HostKeyCode.AltRight, out var altGr))
        {
            text.Append($"        <RALT> = {altGr + 8};\n");
        }

        text.Append("    };\n");
        text.Append(HostKeymapLayout.Preamble);
        text.Append("    xkb_symbols {\n");
        text.Append($"        name[Group1] = \"{Escape(name)}\";\n");

        foreach (var key in keys)
        {
            if (!TryKeycodeName(key.Code, out var keycode) || key.Plain is null)
            {
                continue;
            }

            var alphabetic = IsLetterPair(key.Plain, key.Shift);
            var four = key.Level3 is not null || key.Level4 is not null;
            var type = (four, alphabetic) switch
            {
                (true, true) => "FOUR_LEVEL_SEMIALPHABETIC",
                (true, false) => "FOUR_LEVEL",
                (false, true) => "ALPHABETIC",
                _ => "TWO_LEVEL",
            };

            text.Append($"        key <{keycode}> {{ type[Group1] = \"{type}\", [ ");
            text.Append(key.Plain);
            text.Append(", ");
            text.Append(key.Shift ?? key.Plain);
            if (key.Level3 is not null || key.Level4 is not null)
            {
                text.Append(", ");
                text.Append(key.Level3 ?? "NoSymbol");
                text.Append(", ");
                text.Append(key.Level4 ?? "NoSymbol");
            }

            text.Append(" ] };\n");
        }

        foreach (var fixedKey in HostKeymapLayout.Keys)
        {
            if (!HostKeyMap.TryToEvdev(fixedKey.Code, out _))
            {
                continue;
            }

            text.Append(
                $"        key <{fixedKey.Name}> {{ type[Group1] = \"{fixedKey.Type}\", [ {fixedKey.Symbols} ] }};\n");
        }

        text.Append("        modifier_map Shift { <LFSH>, <RTSH> };\n");
        text.Append("        modifier_map Lock { <CAPS> };\n");
        text.Append("        modifier_map Control { <LCTL>, <RCTL> };\n");
        text.Append("        modifier_map Mod1 { <LALT> };\n");
        text.Append("        modifier_map Mod2 { <NMLK> };\n");
        text.Append("        modifier_map Mod4 { <LWIN>, <RWIN> };\n");

        if (anyLevel3)
        {
            text.Append("        key <RALT> { type[Group1] = \"ONE_LEVEL\", [ ISO_Level3_Shift ] };\n");
            text.Append("        modifier_map Mod5 { <RALT> };\n");
        }

        text.Append("    };\n");
        text.Append("};\n");
        return text.ToString();
    }

    private static bool IsLetterPair(string plain, string? shift) =>
        plain.Length == 1 && char.IsLower(plain[0]) &&
        shift is { Length: 1 } && char.IsUpper(shift[0]) &&
        char.ToUpperInvariant(plain[0]) == shift[0];

    private static string Escape(string value) => value.Replace("\"", string.Empty, StringComparison.Ordinal);
}
