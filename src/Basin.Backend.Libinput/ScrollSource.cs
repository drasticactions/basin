using Basin.Session;
using Libinput;
using Udev;

namespace Basin.Backend.Libinput;

/// <summary>Names the physical source of a libinput scroll event.</summary>
public enum ScrollSource
{
    /// <summary>A mouse wheel that moves in discrete steps.</summary>
    Wheel,
    /// <summary>Fingers on a touchpad or touchscreen. The sequence ends with a zero value.</summary>
    Finger,
    /// <summary>A continuous source with no discrete steps, for example button scrolling on a trackpoint.</summary>
    Continuous,
}
