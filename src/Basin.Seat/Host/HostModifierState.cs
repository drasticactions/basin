namespace Basin.Seat;

public sealed class HostModifierState
{
    public const uint PhantomWindowMillis = 50;

    private bool _holding;
    private HostKeyEvent _held;

    public bool MasksPhantomControl { get; init; } = OperatingSystem.IsWindows();

    public HostModifiers Modifiers { get; private set; }

    public int Feed(in HostKeyEvent input, Span<HostKeyEvent> output)
    {
        var count = 0;

        if (_holding)
        {
            var cancelled = input.Pressed
                && input.Code == HostKeyCode.AltRight
                && input.TimeMs - _held.TimeMs <= PhantomWindowMillis;

            _holding = false;
            if (!cancelled)
            {
                Apply(_held);
                output[count++] = _held;
            }
        }

        if (MasksPhantomControl && input.Pressed && input.Code == HostKeyCode.ControlLeft)
        {
            _holding = true;
            _held = input;
            return count;
        }

        Apply(input);
        output[count++] = input;
        return count;
    }

    public int Idle(uint timeMs, Span<HostKeyEvent> output)
    {
        if (!_holding || timeMs - _held.TimeMs <= PhantomWindowMillis)
        {
            return 0;
        }

        _holding = false;
        Apply(_held);
        output[0] = _held;
        return 1;
    }

    public void Clear()
    {
        _holding = false;
        Modifiers &= HostModifiers.CapsLock | HostModifiers.NumLock;
    }

    private void Apply(in HostKeyEvent input)
    {
        var bit = input.Code switch
        {
            HostKeyCode.ShiftLeft or HostKeyCode.ShiftRight => HostModifiers.Shift,
            HostKeyCode.ControlLeft or HostKeyCode.ControlRight => HostModifiers.Control,
            HostKeyCode.AltLeft => HostModifiers.Alt,
            HostKeyCode.AltRight => HostModifiers.AltGr,
            HostKeyCode.MetaLeft or HostKeyCode.MetaRight => HostModifiers.Meta,
            _ => HostModifiers.None,
        };

        if (bit != HostModifiers.None)
        {
            Modifiers = input.Pressed ? Modifiers | bit : Modifiers & ~bit;
            return;
        }

        if (!input.Pressed)
        {
            return;
        }

        if (input.Code == HostKeyCode.CapsLock)
        {
            Modifiers ^= HostModifiers.CapsLock;
        }
        else if (input.Code == HostKeyCode.NumLock)
        {
            Modifiers ^= HostModifiers.NumLock;
        }
    }
}
