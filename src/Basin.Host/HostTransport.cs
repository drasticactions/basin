using Basin.Backend.Drm;
using Basin.Backend.Headless;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Session;
using Wayland.Server;

namespace Basin.Host;

/// <summary>
/// Identifies the implementation of the Wayland wire protocol that the host display uses.
/// </summary>
public enum HostTransport
{
    /// <summary>
    /// The native libwayland-server library.
    /// </summary>
    LibWayland,

    /// <summary>
    /// The managed wire implementation in .NET.
    /// </summary>
    Managed,
}
