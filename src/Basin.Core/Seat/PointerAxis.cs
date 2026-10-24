using Wayland;

namespace Basin;

/// <summary>
/// One pointer scroll event on one axis.
/// </summary>
/// <param name="Axis">The scroll axis.</param>
/// <param name="Value">The scroll distance in surface-local logical pixels.</param>
/// <param name="Value120">The high-resolution wheel distance, where 120 is one detent. It is zero when the source has no detents.</param>
/// <param name="Source">The kind of device that produced the scroll.</param>
/// <param name="RelativeDirection">Whether the physical motion runs with the scroll direction or inverts it.</param>
public readonly record struct PointerAxis(
    WlPointer.Axis Axis,
    double Value,
    int Value120 = 0,
    WlPointer.AxisSource Source = WlPointer.AxisSource.Wheel,
    WlPointer.AxisRelativeDirection RelativeDirection = WlPointer.AxisRelativeDirection.Identical)
{
    /// <summary>
    /// Gets a value indicating whether the event is an axis stop, with both distances zero.
    /// </summary>
    public bool IsStop => Value == 0 && Value120 == 0;
}
