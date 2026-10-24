using Basin.Seat;
using Xkb;

namespace Basin.SoftKeyboard;

/// <summary>
/// The <see cref="ISoftKeyOutput"/> that types into the focused client through a <see cref="SeatKeyboard"/>.
/// Text goes through its own <see cref="KeySynthesizer"/>, synced to the seat's keymap and effective layout before each use.
/// </summary>
public sealed class SeatKeyOutput : ISoftKeyOutput
{
    private readonly SeatKeyboard _keyboard;
    private readonly KeySynthesizer _synthesizer;
    private Xkb.XkbKeymap? _controlKeymap;
    private uint? _control;
    private readonly XkbModifierMask _mask = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SeatKeyOutput"/> class.
    /// </summary>
    /// <param name="keyboard">The seat keyboard that delivers the keys.</param>
    /// <exception cref="ArgumentNullException"><paramref name="keyboard"/> is <see langword="null"/>.</exception>
    public SeatKeyOutput(SeatKeyboard keyboard)
    {
        ArgumentNullException.ThrowIfNull(keyboard);
        _keyboard = keyboard;
        _synthesizer = new KeySynthesizer(keyboard);
    }

    /// <inheritdoc/>
    public void Key(uint keycode, uint timeMs)
    {
        _keyboard.NotifyKey(timeMs, keycode, pressed: true);
        _keyboard.NotifyKey(timeMs, keycode, pressed: false);
    }

    /// <summary>
    /// Sends a press and a release of one key with the Control bit set in the seat's xkb state for that key only.
    /// Without a keymap, or with no Control modifier in the keymap, the key goes without Control.
    /// </summary>
    /// <param name="keycode">The evdev keycode.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    public void ControlKey(uint keycode, uint timeMs)
    {
        if (_keyboard.Keymap is not { } keymap)
        {
            Key(keycode, timeMs);
            return;
        }

        if (!ReferenceEquals(keymap, _controlKeymap))
        {
            _controlKeymap = keymap;
            _control = keymap.GetModIndex("Control");
        }

        if (_control is not { } control)
        {
            Key(keycode, timeMs);
            return;
        }

        var (depressed, latched, locked, group) = _keyboard.ModifierState;
        _keyboard.NotifyModifiers(depressed | (1u << (int)control), latched, locked, group);
        _keyboard.SendKey(timeMs, keycode, Wayland.WlKeyboard.KeyState.Pressed);
        _keyboard.SendKey(timeMs, keycode, Wayland.WlKeyboard.KeyState.Released);
        _keyboard.NotifyModifiers(depressed, latched, locked, group);
    }

    /// <inheritdoc/>
    public void ModifiedKey(uint keycode, uint xkbModifiers, KeyModifiers modifiers, uint timeMs)
    {
        if (_keyboard.Keymap is not { } keymap)
        {
            Key(keycode, timeMs);
            return;
        }

        var extra = xkbModifiers | _mask.For(keymap, modifiers);
        if (extra == 0)
        {
            Key(keycode, timeMs);
            return;
        }

        var (depressed, latched, locked, group) = _keyboard.ModifierState;
        _keyboard.NotifyModifiers(depressed | extra, latched, locked, group);
        _keyboard.SendKey(timeMs, keycode, Wayland.WlKeyboard.KeyState.Pressed);
        _keyboard.SendKey(timeMs, keycode, Wayland.WlKeyboard.KeyState.Released);
        _keyboard.NotifyModifiers(depressed, latched, locked, group);
    }

    /// <inheritdoc/>
    public int Type(string text, uint timeMs)
    {
        Sync();
        return _synthesizer.Type(text, timeMs);
    }

    /// <inheritdoc/>
    public bool TryResolve(uint keysym, out uint keycode, out uint modifiers)
    {
        Sync();
        return _synthesizer.TryResolve(keysym, out keycode, out modifiers);
    }

    private void Sync()
    {
        _synthesizer.Keymap = _keyboard.Keymap;
        _synthesizer.Layout = _keyboard.State?.SerializeLayout(XkbStateComponent.LayoutEffective) ?? 0;
    }
}
