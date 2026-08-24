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

    public static HostOptions ForBackend(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var backendName = name;
        var socketFd = -1;
        var colon = name.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            backendName = name[..colon];
            socketFd = int.Parse(name[(colon + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        }

        return new HostOptions
        {
            Backend = backendName switch
            {
                "drm" => HostBackend.Drm,
                "nested" => HostBackend.Nested,
                "headless" => HostBackend.Headless,
                _ => throw new ArgumentException($"unknown backend '{backendName}'", nameof(name)),
            },
            SocketFd = socketFd,
        };
    }
}
