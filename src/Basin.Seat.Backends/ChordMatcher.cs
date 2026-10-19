using Basin.Config;
using Basin.Diagnostics;
using Xkb;

namespace Basin.Seat.Backends;

public sealed class ChordMatcher
{
    private readonly SeatKeyboard _keyboard;
    private XkbKeysym _keysym;
    private Modifiers _modifiers;
    private uint? _held;

    public ChordMatcher(SeatKeyboard keyboard)
    {
        ArgumentNullException.ThrowIfNull(keyboard);
        _keyboard = keyboard;
    }

    public bool IsSet => _keysym.Value != 0;

    public void Set(uint keysym, Modifiers modifiers)
    {
        _keysym = new XkbKeysym(keysym).ToLower();
        _modifiers = modifiers;
        _held = null;
    }

    public bool TrySet(string? chord, BasinLogger log)
    {
        if (string.IsNullOrWhiteSpace(chord))
        {
            Clear();
            return true;
        }

        if (!HotkeyParser.TryParseChord(chord, log, out var keysym, out var modifiers))
        {
            Clear();
            return false;
        }

        Set(keysym, modifiers);
        return true;
    }

    public void Clear()
    {
        _keysym = default;
        _modifiers = Modifiers.None;
        _held = null;
    }

    public bool Matches(uint key, bool pressed)
    {
        if (!pressed)
        {
            if (_held != key)
            {
                return false;
            }

            _held = null;
            return true;
        }

        if (!MatchesKeysym(_keyboard.RawKeysymFor(key)))
        {
            return false;
        }

        _held = key;
        return true;
    }

    public bool MatchesKeysym(XkbKeysym symbol) =>
        IsSet && symbol.ToLower() == _keysym && HeldModifiers.From(_keyboard.State) == _modifiers;
}
