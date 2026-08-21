using Wayland;

namespace Basin.Seat;

public interface IPointerGrab
{
    void Enter(Surface? surface, double x, double y);

    void Motion(uint timeMs, double x, double y);

    uint Button(uint timeMs, uint button, WlPointer.ButtonState state);

    void Axis(uint timeMs, in PointerAxis axis);

    void Cancel();
}
