using Basin.Backend.Drm;
using Basin.Backend.Libinput;
using Basin.Backend.Wayland;
using Basin.Capabilities.Defaults;
using Basin.Desktop;

namespace Basin.Seat.Backends;

public delegate void SeatMotionHandler(uint timeMs, double dx, double dy, double? unacceleratedDx, double? unacceleratedDy);
