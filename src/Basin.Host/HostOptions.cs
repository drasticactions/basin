using Basin.Backend.Drm;
using Basin.Backend.Headless;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Session;
using Wayland.Server;

namespace Basin.Host;

public sealed record HostOptions
{
    public HostBackend Backend { get; init; } = HostBackend.Headless;

    public HostTransport Transport { get; init; } = HostTransport.LibWayland;

    public string? DrmDevice { get; init; }

    public int SocketFd { get; init; } = -1;
}
