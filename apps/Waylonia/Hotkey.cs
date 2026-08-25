using Basin.Diagnostics;

namespace Waylonia;

internal sealed record Hotkey(string Chord, HotkeyModifiers Modifiers, string Key, string Command)
{
    public static Hotkey? Parse(string chord, string? command, BasinLogger log)
    {
        if (command is null)
        {
            log.Warn($"hotkey '{chord}' has no command, skipping");
            return null;
        }

        var tokens = chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            log.Warn($"hotkey '{chord}' names no key, skipping");
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
                    log.Warn($"unknown modifier '{(tokens[i])}' in hotkey '{chord}', skipping");
                    return null;
            }
        }

        return new Hotkey(chord, modifiers, tokens[^1].ToLowerInvariant(), command);
    }
}
