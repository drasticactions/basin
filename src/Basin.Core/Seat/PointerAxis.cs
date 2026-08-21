using Wayland;

namespace Basin;

public readonly record struct PointerAxis(
    WlPointer.Axis Axis,
    double Value,
    int Value120 = 0,
    WlPointer.AxisSource Source = WlPointer.AxisSource.Wheel,
    WlPointer.AxisRelativeDirection RelativeDirection = WlPointer.AxisRelativeDirection.Identical)
{
    public bool IsStop => Value == 0 && Value120 == 0;
}
