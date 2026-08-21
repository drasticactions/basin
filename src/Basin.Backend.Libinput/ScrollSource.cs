using Basin.Session;
using Libinput;
using Udev;

namespace Basin.Backend.Libinput;

public enum ScrollSource
{
    Wheel,
    Finger,
    Continuous,
}
