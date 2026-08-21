using Wayland;
using Basin.Capabilities;
using Xkb;

namespace Basin.Seat;

public interface IKeyboardGrab
{
    void Enter(Surface? surface, ReadOnlySpan<uint> pressedKeys);

    void Key(uint timeMs, uint key, WlKeyboard.KeyState state);

    void Modifiers();

    void Cancel();
}
