using Basin.Scene;
using Basin.Seat;

namespace Basin.SoftKeyboard;

public sealed class UISurfaceKeyOutput : ISoftKeyOutput
{
    private readonly UISurfaceRouter _router;
    private readonly SeatKeyboard _keyboard;
    private Xkb.XkbKeymap? _controlKeymap;
    private uint? _control;
    private readonly XkbModifierMask _mask = new();

    public UISurfaceKeyOutput(UISurfaceRouter router, SeatKeyboard keyboard)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(keyboard);
        _router = router;
        _keyboard = keyboard;
    }

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

    public int Type(string text, uint timeMs)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length > 0 && _router.TextCommit(text) ? text.Length : 0;
    }

    public bool TryResolve(uint keysym, out uint keycode, out uint modifiers) =>
        _keyboard.TryKeycodeForKeysym(keysym, out keycode, out modifiers);
}
