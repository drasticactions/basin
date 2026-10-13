namespace Basin.SoftKeyboard;

internal sealed class XkbModifierMask
{
    private Xkb.XkbKeymap? _keymap;
    private uint _shift;
    private uint _control;
    private uint _alt;
    private uint _super;

    public uint For(Xkb.XkbKeymap keymap, KeyModifiers modifiers)
    {
        if (!ReferenceEquals(keymap, _keymap))
        {
            _keymap = keymap;
            _shift = Bit(keymap, "Shift");
            _control = Bit(keymap, "Control");
            _alt = Bit(keymap, "Mod1");
            _super = Bit(keymap, "Mod4");
        }

        var mask = 0u;
        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            mask |= _shift;
        }

        if ((modifiers & KeyModifiers.Control) != 0)
        {
            mask |= _control;
        }

        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            mask |= _alt;
        }

        if ((modifiers & KeyModifiers.Super) != 0)
        {
            mask |= _super;
        }

        return mask;
    }

    private static uint Bit(Xkb.XkbKeymap keymap, string name) =>
        keymap.GetModIndex(name) is { } index and < 32 ? 1u << (int)index : 0;
}
