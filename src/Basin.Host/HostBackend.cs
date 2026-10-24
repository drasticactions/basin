using Basin.Backend.Drm;
using Basin.Backend.Headless;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Session;
using Wayland.Server;

namespace Basin.Host;

/// <summary>
/// Identifies the backend that <see cref="BasinHost.Create"/> starts.
/// </summary>
public enum HostBackend
{
    /// <summary>
    /// KMS outputs on a seat that seatd or logind opens.
    /// </summary>
    Drm,

    /// <summary>
    /// Windows inside the parent Wayland session.
    /// </summary>
    Nested,

    /// <summary>
    /// Outputs in memory only.
    /// </summary>
    Headless,
}
