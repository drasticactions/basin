using Basin.Diagnostics;
using Wayland;
using Wayland.Server;

namespace Basin.Seat;

[Flags]
public enum SeatCapability
{
    None = 0,
    Pointer = 1,
    Keyboard = 2,
    Touch = 4,
}
