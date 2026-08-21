using Wayland;

namespace Basin.Backend.Libinput;

public static class ScrollTranslation
{
    public static WlPointer.Axis ToAxis(this ScrollOrientation orientation) =>
        orientation == ScrollOrientation.Horizontal
            ? WlPointer.Axis.HorizontalScroll
            : WlPointer.Axis.VerticalScroll;

    public static WlPointer.AxisSource ToAxisSource(this ScrollSource source) => source switch
    {
        ScrollSource.Finger => WlPointer.AxisSource.Finger,
        ScrollSource.Continuous => WlPointer.AxisSource.Continuous,
        _ => WlPointer.AxisSource.Wheel,
    };
}
