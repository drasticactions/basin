using Basin.Seat;

namespace Basin.Backend.Headless;

public sealed class HeadlessKeyboard
{
    private readonly HostModifierState _modifiers = new();

    internal HeadlessKeyboard(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public HostModifiers Modifiers => _modifiers.Modifiers;

    public event Action<uint, uint, bool>? Key;

    public event Action<HostModifiers>? ModifiersChanged;

    public void InjectKey(uint timeMs, HostKeyCode code, bool pressed)
    {
        Span<HostKeyEvent> filtered = stackalloc HostKeyEvent[2];
        var before = _modifiers.Modifiers;
        var count = _modifiers.Feed(new HostKeyEvent(timeMs, code, pressed), filtered);
        Emit(filtered, count, before);
    }

    public void InjectIdle(uint timeMs)
    {
        Span<HostKeyEvent> filtered = stackalloc HostKeyEvent[1];
        var before = _modifiers.Modifiers;
        Emit(filtered, _modifiers.Idle(timeMs, filtered), before);
    }

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
