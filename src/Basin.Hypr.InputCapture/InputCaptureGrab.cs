using Basin.Seat;
using Wayland;

namespace Basin.Hypr.InputCapture;

internal sealed class InputCaptureGrab(HyprlandInputCaptureManager owner) : IPointerGrab, IKeyboardGrab
{
    public void Enter(Surface? surface, double x, double y)
    {
    }

    public void Motion(uint timeMs, double x, double y)
    {
    }

    public uint Button(uint timeMs, uint button, WlPointer.ButtonState state)
    {
        owner.Active?.Button(button, state == WlPointer.ButtonState.Pressed);
        return 0;
    }

    public void Axis(uint timeMs, in PointerAxis axis) => owner.Active?.Axis(in axis);

    void IPointerGrab.Cancel() => owner.ForceRelease();

    public void Enter(Surface? surface, ReadOnlySpan<uint> pressedKeys)
    {
    }

    public void Key(uint timeMs, uint key, WlKeyboard.KeyState state) =>
        owner.Active?.Key(key, state == WlKeyboard.KeyState.Pressed);

    public void Modifiers()
    {
        var (depressed, latched, locked, group) = owner.Seat.Keyboard.ModifierState;
        owner.Active?.Modifiers(depressed, latched, locked, group);
    }

    void IKeyboardGrab.Cancel() => owner.ForceRelease();
}
