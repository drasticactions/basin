using Basin.Config;
using Basin.Diagnostics;
using Xkb;

namespace Basin.Seat.Backends;

/// <summary>
/// Matches keys against one key chord, a keysym and a set of held modifiers. A matched press makes the matching release match too.
/// </summary>
public sealed class ChordMatcher
{
    private readonly SeatKeyboard _keyboard;
    private XkbKeysym _keysym;
    private Modifiers _modifiers;
    private uint? _held;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChordMatcher"/> class. It starts with no chord.
    /// </summary>
    /// <param name="keyboard">The seat keyboard that gives the keysym and the modifier state.</param>
    /// <exception cref="ArgumentNullException"><paramref name="keyboard"/> is <see langword="null"/>.</exception>
    public ChordMatcher(SeatKeyboard keyboard)
    {
        ArgumentNullException.ThrowIfNull(keyboard);
        _keyboard = keyboard;
    }

    /// <summary>
    /// Gets a value indicating whether a chord is set.
    /// </summary>
    public bool IsSet => _keysym.Value != 0;

    /// <summary>
    /// Sets the chord. The keysym is compared in lower case.
    /// </summary>
    /// <param name="keysym">The keysym value.</param>
    /// <param name="modifiers">The modifiers that must be held, and no others.</param>
    public void Set(uint keysym, Modifiers modifiers)
    {
        _keysym = new XkbKeysym(keysym).ToLower();
        _modifiers = modifiers;
        _held = null;
    }

    /// <summary>
    /// Parses a chord text, for example "Super+Return", and sets it. An empty text clears the chord.
    /// </summary>
    /// <param name="chord">The chord text, or <see langword="null"/>.</param>
    /// <param name="log">The logger that receives parse errors.</param>
    /// <returns><see langword="false"/> when the text does not parse. The chord is then cleared.</returns>
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

    /// <summary>
    /// Removes the chord, so that no key matches.
    /// </summary>
    public void Clear()
    {
        _keysym = default;
        _modifiers = Modifiers.None;
        _held = null;
    }

    /// <summary>
    /// Tells whether a key event is the chord. A press matches by keysym and modifiers. A release matches only for the key whose press matched last.
    /// </summary>
    /// <param name="key">The evdev key code.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    /// <returns><see langword="true"/> when the event belongs to the chord.</returns>
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

    /// <summary>
    /// Tells whether a keysym with the current held modifiers is the chord.
    /// </summary>
    /// <param name="symbol">The keysym to test.</param>
    /// <returns><see langword="true"/> when a chord is set and both the keysym and the modifiers match.</returns>
    public bool MatchesKeysym(XkbKeysym symbol) =>
        IsSet && symbol.ToLower() == _keysym && HeldModifiers.From(_keyboard.State) == _modifiers;
}
