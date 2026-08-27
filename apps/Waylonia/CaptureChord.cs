using Basin.Diagnostics;

namespace Waylonia;

internal sealed record CaptureChord(string Text, bool DoubleTap, uint Code, HotkeyModifiers Modifiers)
{
    public const int DoubleTapMillis = 400;

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

    public static CaptureChord? Parse(string text, BasinLogger log)
    {
        if (text is null || text.Trim().Length == 0)
        {
            return null;
        }

        var trimmed = text.Trim();
        var doubleTap = trimmed.StartsWith("double:", StringComparison.OrdinalIgnoreCase);
        var body = doubleTap ? trimmed["double:".Length..] : trimmed;
        var tokens = body.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            log.Warn($"capture-chord '{trimmed}' names no key, capture is off");
            return null;
        }

        var modifiers = HotkeyModifiers.None;
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            switch (tokens[i].ToLowerInvariant())
            {
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "ctrl" or "control":
                    modifiers |= HotkeyModifiers.Ctrl;
                    break;
                case "alt" or "option":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "super" or "cmd" or "command" or "win" or "logo":
                    modifiers |= HotkeyModifiers.Super;
                    break;
                default:
                    log.Warn($"unknown modifier '{tokens[i]}' in capture-chord '{trimmed}', capture is off");
                    return null;
            }
        }

        var code = CodeFor(tokens[^1]);
        if (code == 0)
        {
            log.Warn($"unknown key '{tokens[^1]}' in capture-chord '{trimmed}', capture is off");
            return null;
        }

        if (doubleTap && modifiers != HotkeyModifiers.None)
        {
            log.Warn($"a double-tap capture-chord names one key alone, ignoring the modifiers in '{trimmed}'");
            modifiers = HotkeyModifiers.None;
        }

        return new CaptureChord(trimmed, doubleTap, code, modifiers);
    }

    public static HotkeyModifiers ModifierOf(uint code) => code switch
    {
        42 or 54 => HotkeyModifiers.Shift,
        29 or 97 => HotkeyModifiers.Ctrl,
        56 or 100 => HotkeyModifiers.Alt,
        125 or 126 => HotkeyModifiers.Super,
        _ => HotkeyModifiers.None,
    };
}
