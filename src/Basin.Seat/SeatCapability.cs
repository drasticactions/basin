using Basin.Diagnostics;
using Wayland;
using Wayland.Server;

namespace Basin.Seat;

/// <summary>
/// The input device kinds that a seat advertises in <c>wl_seat.capabilities</c>. Combine values for more than one kind.
/// </summary>
[Flags]
public enum SeatCapability
{
    /// <summary>No device.</summary>
    None = 0,
    /// <summary>A pointer.</summary>
    Pointer = 1,
    /// <summary>A keyboard.</summary>
    Keyboard = 2,
    /// <summary>A touch screen.</summary>
    Touch = 4,
}
