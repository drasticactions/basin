using Basin.Session;
using Libinput;
using Udev;

namespace Basin.Backend.Libinput;

/// <summary>Names the axis of a libinput scroll event.</summary>
public enum ScrollOrientation
{
    /// <summary>The vertical scroll axis.</summary>
    Vertical,
    /// <summary>The horizontal scroll axis.</summary>
    Horizontal,
}
