using Wayland;

namespace Basin.Backend.Libinput;

/// <summary>Maps the libinput scroll vocabulary onto the <c>wl_pointer</c> axis enumerations.</summary>
public static class ScrollTranslation
{
    /// <summary>Converts a scroll orientation to a <c>wl_pointer</c> axis.</summary>
    /// <param name="orientation">The libinput scroll orientation.</param>
    /// <returns>The matching <see cref="WlPointer.Axis"/>.</returns>
    public static WlPointer.Axis ToAxis(this ScrollOrientation orientation) =>
        orientation == ScrollOrientation.Horizontal
            ? WlPointer.Axis.HorizontalScroll
            : WlPointer.Axis.VerticalScroll;

    /// <summary>Converts a scroll source to a <c>wl_pointer</c> axis source. An unknown value maps to <see cref="WlPointer.AxisSource.Wheel"/>.</summary>
    /// <param name="source">The libinput scroll source.</param>
    /// <returns>The matching <see cref="WlPointer.AxisSource"/>.</returns>
    public static WlPointer.AxisSource ToAxisSource(this ScrollSource source) => source switch
    {
        ScrollSource.Finger => WlPointer.AxisSource.Finger,
        ScrollSource.Continuous => WlPointer.AxisSource.Continuous,
        _ => WlPointer.AxisSource.Wheel,
    };
}
