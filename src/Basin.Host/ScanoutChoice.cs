using Basin.Backend.Drm;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Basin.Scene;
using Wayland.Server;

namespace Basin.Host;

public enum ScanoutChoice
{
    DeviceBuffers,

    DumbLinear,

    RefusedByPlane,
}
