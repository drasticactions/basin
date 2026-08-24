using Basin;

namespace Westonia;

public sealed class ShellModifierState
{
    private readonly HashSet<uint> _pressed = [];

    public ShellModifiers Current { get; private set; }

    public void Clear()
    {
        _pressed.Clear();
        Current = ShellModifiers.None;
    }

    public bool Track(uint key, bool pressed)
    {
        var flag = FlagOf(key);
        if (flag == ShellModifiers.None)
        {
            return false;
        }

        if (pressed)
        {
            _pressed.Add(key);
        }
        else
        {
            _pressed.Remove(key);
        }

        var state = ShellModifiers.None;
        foreach (var code in _pressed)
        {
            state |= FlagOf(code);
        }

        Current = state;
        return true;
    }

    public bool Holds(ShellModifiers modifiers) => (Current & modifiers) == modifiers;

    public bool Exactly(ShellModifiers modifiers) => Current == modifiers;

    private static ShellModifiers FlagOf(uint key) => key switch
    {
        InputCodes.KeyLeftCtrl or InputCodes.KeyRightCtrl => ShellModifiers.Ctrl,
        InputCodes.KeyLeftAlt or InputCodes.KeyRightAlt => ShellModifiers.Alt,
        InputCodes.KeyLeftShift or InputCodes.KeyRightShift => ShellModifiers.Shift,
        InputCodes.KeyLeftMeta or InputCodes.KeyRightMeta => ShellModifiers.Super,
        _ => ShellModifiers.None,
    };
}
