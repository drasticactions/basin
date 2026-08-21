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
        EvdevKeys.LeftCtrl or EvdevKeys.RightCtrl => ShellModifiers.Ctrl,
        EvdevKeys.LeftAlt or EvdevKeys.RightAlt => ShellModifiers.Alt,
        EvdevKeys.LeftShift or EvdevKeys.RightShift => ShellModifiers.Shift,
        EvdevKeys.LeftMeta or EvdevKeys.RightMeta => ShellModifiers.Super,
        _ => ShellModifiers.None,
    };
}
