namespace Basin.Seat;

/// <summary>
/// Tracks the host modifiers from a stream of key events.
/// It can also mask the phantom left Control press that Windows sends with each AltGr press.
/// </summary>
public sealed class HostModifierState
{
    /// <summary>
    /// The time, in milliseconds, that a left Control press is held back. An AltGr press in this time cancels the held press.
    /// </summary>
    public const uint PhantomWindowMillis = 50;

    private bool _holding;
    private HostKeyEvent _held;

    /// <summary>
    /// Gets a value indicating whether a left Control press is held back to find a phantom press.
    /// The default is <see langword="true"/> on Windows only.
    /// </summary>
    public bool MasksPhantomControl { get; init; } = OperatingSystem.IsWindows();

    /// <summary>
    /// Gets the modifiers after the events that this object released.
    /// Caps Lock and Num Lock change state on each press.
    /// </summary>
    public HostModifiers Modifiers { get; private set; }

    /// <summary>
    /// Processes one key event and writes the events to deliver now.
    /// A held left Control press comes out first, unless this event is an AltGr press in time to cancel it.
    /// </summary>
    /// <param name="input">The key event from the host.</param>
    /// <param name="output">The destination, with space for at least two events.</param>
    /// <returns>The number of events written to <paramref name="output"/>, from zero to two.</returns>
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

    /// <summary>
    /// Releases a held left Control press after <see cref="PhantomWindowMillis"/> has passed with no other key event.
    /// </summary>
    /// <param name="timeMs">The current time in milliseconds, on the clock of <see cref="HostKeyEvent.TimeMs"/>.</param>
    /// <param name="output">The destination, with space for at least one event.</param>
    /// <returns>One when a held press was released, or zero.</returns>
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

    /// <summary>
    /// Drops a held press and clears every modifier except Caps Lock and Num Lock.
    /// The dropped press is not delivered.
    /// </summary>
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
