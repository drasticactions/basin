using Basin.Seat;

namespace Basin.Backend.Headless;

/// <summary>
/// A keyboard that converts injected host key codes to evdev key events and tracks the modifier state.
/// </summary>
public sealed class HeadlessKeyboard
{
    private readonly HostModifierState _modifiers = new();

    internal HeadlessKeyboard(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets the device name that <see cref="HeadlessBackend.CreateKeyboard"/> gave this keyboard.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the modifiers that the injected keys hold down or lock at this time.
    /// </summary>
    public HostModifiers Modifiers => _modifiers.Modifiers;

    /// <summary>
    /// Occurs for each key event with an evdev code. The arguments are the time in milliseconds, the evdev key code and the pressed state.
    /// Keys with no evdev code do not raise it.
    /// </summary>
    public event Action<uint, uint, bool>? Key;

    /// <summary>
    /// Occurs after <see cref="Modifiers"/> changes, with the new value.
    /// <see cref="InjectFocusLost"/> always raises it.
    /// </summary>
    public event Action<HostModifiers>? ModifiersChanged;

    /// <summary>
    /// Feeds one host key event through the modifier tracker and raises <see cref="Key"/> for each event that comes out.
    /// On Windows, a left Control press can be held back to detect a phantom AltGr Control.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="code">The host key code.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    public void InjectKey(uint timeMs, HostKeyCode code, bool pressed)
    {
        Span<HostKeyEvent> filtered = stackalloc HostKeyEvent[2];
        var before = _modifiers.Modifiers;
        var count = _modifiers.Feed(new HostKeyEvent(timeMs, code, pressed), filtered);
        Emit(filtered, count, before);
    }

    /// <summary>
    /// Releases a left Control press that the tracker holds back, when its wait window is over at <paramref name="timeMs"/>.
    /// </summary>
    /// <param name="timeMs">The current time in milliseconds.</param>
    public void InjectIdle(uint timeMs)
    {
        Span<HostKeyEvent> filtered = stackalloc HostKeyEvent[1];
        var before = _modifiers.Modifiers;
        Emit(filtered, _modifiers.Idle(timeMs, filtered), before);
    }

    /// <summary>
    /// Releases all held modifiers and keeps only Caps Lock and Num Lock. It then raises <see cref="ModifiersChanged"/>.
    /// </summary>
    public void InjectFocusLost()
    {
        _modifiers.Clear();
        ModifiersChanged?.Invoke(_modifiers.Modifiers);
    }

    private void Emit(ReadOnlySpan<HostKeyEvent> events, int count, HostModifiers before)
    {
        for (var i = 0; i < count; i++)
        {
            if (HostKeyMap.TryToEvdev(events[i].Code, out var evdev))
            {
                Key?.Invoke(events[i].TimeMs, evdev, events[i].Pressed);
            }
        }

        if (count > 0 && before != _modifiers.Modifiers)
        {
            ModifiersChanged?.Invoke(_modifiers.Modifiers);
        }
    }
}
