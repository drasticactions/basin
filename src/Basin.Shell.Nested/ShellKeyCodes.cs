namespace Basin.Shell.Nested;

public static class ShellKeyCodes
{
    private static readonly (string Name, uint Code)[] Keys =
    [
        ("leftshift", 42), ("lshift", 42), ("shiftleft", 42),
        ("rightshift", 54), ("rshift", 54), ("shiftright", 54),
        ("leftcontrol", 29), ("leftctrl", 29), ("lctrl", 29), ("controlleft", 29),
        ("rightcontrol", 97), ("rightctrl", 97), ("rctrl", 97), ("controlright", 97),
        ("leftalt", 56), ("lalt", 56), ("altleft", 56),
        ("rightalt", 100), ("ralt", 100), ("altright", 100), ("altgr", 100),
        ("leftsuper", 125), ("lsuper", 125), ("leftmeta", 125), ("super", 125), ("meta", 125),
        ("rightsuper", 126), ("rsuper", 126), ("rightmeta", 126),
        ("escape", 1), ("esc", 1), ("space", 57), ("tab", 15), ("enter", 28), ("return", 28),
        ("f1", 59), ("f2", 60), ("f3", 61), ("f4", 62), ("f5", 63), ("f6", 64),
        ("f7", 65), ("f8", 66), ("f9", 67), ("f10", 68), ("f11", 87), ("f12", 88),
        ("left", 105), ("right", 106), ("up", 103), ("down", 108),
        ("grave", 41), ("backquote", 41), ("minus", 12), ("equal", 13),
        ("backspace", 14), ("delete", 111), ("insert", 110), ("home", 102), ("end", 107),
        ("pageup", 104), ("prior", 104), ("pagedown", 109), ("next", 109), ("print", 99),
        ("comma", 51), ("period", 52), ("slash", 53), ("semicolon", 39), ("apostrophe", 40),
        ("bracketleft", 26), ("bracketright", 27), ("backslash", 43),
    ];

    private const string Letters = "abcdefghijklmnopqrstuvwxyz";

    private static readonly uint[] LetterCodes =
    [
        30, 48, 46, 32, 18, 33, 34, 35, 23, 36, 37, 38, 50,
        49, 24, 25, 16, 19, 31, 20, 22, 47, 17, 45, 21, 44,
    ];

    private static readonly uint[] DigitCodes = [11, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    public static uint CodeFor(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var lowered = name.Trim().ToLowerInvariant();
        foreach (var (candidate, code) in Keys)
        {
            if (candidate == lowered)
            {
                return code;
            }
        }

        if (lowered.Length == 1)
        {
            var letter = Letters.IndexOf(lowered[0], StringComparison.Ordinal);
            if (letter >= 0)
            {
                return LetterCodes[letter];
            }

            if (char.IsAsciiDigit(lowered[0]))
            {
                return DigitCodes[lowered[0] - '0'];
            }
        }

        return 0;
    }

    public static ShellModifiers ModifierOf(uint code) => code switch
    {
        42 or 54 => ShellModifiers.Shift,
        29 or 97 => ShellModifiers.Ctrl,
        56 or 100 => ShellModifiers.Alt,
        125 or 126 => ShellModifiers.Super,
        _ => ShellModifiers.None,
    };

    public static ShellModifiers? ModifierNamed(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return token.Trim().ToLowerInvariant() switch
        {
            "shift" => ShellModifiers.Shift,
            "ctrl" or "control" => ShellModifiers.Ctrl,
            "alt" or "option" => ShellModifiers.Alt,
            "super" or "cmd" or "command" or "win" or "logo" => ShellModifiers.Super,
            _ => null,
        };
    }
}
