using Microsoft.Extensions.Logging;

namespace Waylonia;

internal sealed record Hotkey(string Chord, HotkeyModifiers Modifiers, string Key, string Command)
{
    public static Hotkey? Parse(string chord, string? command, ILogger log)
    {
        if (command is null)
        {
            log.LogWarning("hotkey '{Chord}' has no command, skipping", chord);
            return null;
        }

        var tokens = chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            log.LogWarning("hotkey '{Chord}' names no key, skipping", chord);
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
                    log.LogWarning("unknown modifier '{Modifier}' in hotkey '{Chord}', skipping", tokens[i], chord);
                    return null;
            }
        }

        return new Hotkey(chord, modifiers, tokens[^1].ToLowerInvariant(), command);
    }
}
