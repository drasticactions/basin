using Basin.Scene;
using Basin.Seat;

namespace Basin.SoftKeyboard;

/// <summary>
/// The <see cref="ISoftKeyOutput"/> that types into the <see cref="Basin.Capabilities.IUISurface"/> with the keyboard focus of a <see cref="UISurfaceRouter"/>, such as a login box in the compositor's own chrome.
/// </summary>
public sealed class UISurfaceKeyOutput : ISoftKeyOutput
{
    private readonly UISurfaceRouter _router;
    private readonly SeatKeyboard _keyboard;
    private Xkb.XkbKeymap? _controlKeymap;
    private uint? _control;
    private readonly XkbModifierMask _mask = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="UISurfaceKeyOutput"/> class.
    /// </summary>
    /// <param name="router">The router that delivers keys and text to the focused UI surface.</param>
    /// <param name="keyboard">The seat keyboard that gives the keymap, the modifier state and the text of each key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="router"/> or <paramref name="keyboard"/> is <see langword="null"/>.</exception>
    public UISurfaceKeyOutput(UISurfaceRouter router, SeatKeyboard keyboard)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(keyboard);
        _router = router;
        _keyboard = keyboard;
    }

    /// <summary>
    /// Sends a press and a release of one key. The text that the seat keymap gives for the key is committed between them.
    /// </summary>
    /// <param name="keycode">The evdev keycode.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    public void Key(uint keycode, uint timeMs)
    {
        _router.Key(timeMs, keycode, pressed: true);
        Span<char> text = stackalloc char[16];
        var length = _keyboard.TextFor(keycode, text);
        if (length > 0)
        {
            _router.TextCommit(text[..length]);
        }

        _router.Key(timeMs, keycode, pressed: false);
    }

    /// <summary>
    /// Sends a press and a release of one key with the Control bit set in the router's modifier state for that key only.
    /// Without a keymap, or with no Control modifier in the keymap, the key goes as <see cref="Key"/> does.
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
        _router.Modifiers(depressed | (1u << (int)control), latched, locked, group);
        _router.Key(timeMs, keycode, pressed: true);
        _router.Key(timeMs, keycode, pressed: false);
        _router.Modifiers(depressed, latched, locked, group);
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
        _router.Modifiers(depressed | extra, latched, locked, group);
        _router.Key(timeMs, keycode, pressed: true);
        _router.Key(timeMs, keycode, pressed: false);
        _router.Modifiers(depressed, latched, locked, group);
    }

    /// <summary>
    /// Commits the text to the focused UI surface as one commit. It sends no keys.
    /// </summary>
    /// <param name="text">The text to commit.</param>
    /// <param name="timeMs">The event time in milliseconds. The commit does not use it.</param>
    /// <returns>The length of <paramref name="text"/> in UTF-16 code units, or 0 when no surface has the focus.</returns>
    public int Type(string text, uint timeMs)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length > 0 && _router.TextCommit(text) ? text.Length : 0;
    }

    /// <inheritdoc/>
    public bool TryResolve(uint keysym, out uint keycode, out uint modifiers) =>
        _keyboard.TryKeycodeForKeysym(keysym, out keycode, out modifiers);
}
