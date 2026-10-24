using Basin.Backend.Drm;
using Basin.Backend.Libinput;
using Basin.Backend.Wayland;
using Basin.Capabilities.Defaults;
using Basin.Desktop;

namespace Basin.Seat.Backends;

/// <summary>
/// Handles one pointer motion that <see cref="SeatBinder"/> raises after it moved the layout pointer.
/// </summary>
/// <param name="timeMs">The event time in milliseconds.</param>
/// <param name="dx">The accelerated x motion in layout coordinates.</param>
/// <param name="dy">The accelerated y motion in layout coordinates.</param>
/// <param name="unacceleratedDx">The unaccelerated x motion, or <see langword="null"/> when the source has none.</param>
/// <param name="unacceleratedDy">The unaccelerated y motion, or <see langword="null"/> when the source has none.</param>
public delegate void SeatMotionHandler(uint timeMs, double dx, double dy, double? unacceleratedDx, double? unacceleratedDy);
