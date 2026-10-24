using Basin.Backend.Drm;
using Basin.Backend.Headless;
using Basin.Backend.Wayland;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Session;
using Wayland.Server;

namespace Basin.Host;

/// <summary>
/// Holds the options that <see cref="BasinHost.Create"/> builds a host from.
/// </summary>
public sealed record HostOptions
{
    /// <summary>
    /// Gets the backend to start. The default is <see cref="HostBackend.Headless"/>.
    /// </summary>
    public HostBackend Backend { get; init; } = HostBackend.Headless;

    /// <summary>
    /// Gets the Wayland wire implementation. The default is <see cref="HostTransport.LibWayland"/>.
    /// </summary>
    public HostTransport Transport { get; init; } = HostTransport.LibWayland;

    /// <summary>
    /// Gets the DRM card path, or <see langword="null"/> to use <c>BASIN_DRM_DEVICE</c> or the backend's own choice.
    /// </summary>
    public string? DrmDevice { get; init; }

    /// <summary>
    /// Gets an inherited listening socket fd, or -1 to create a new socket with an automatic name.
    /// </summary>
    public int SocketFd { get; init; } = -1;

    /// <summary>
    /// Gets the resolved DRM handoff mode. The default is <see cref="HandoffMode.Auto"/>.
    /// </summary>
    public HandoffMode Handoff { get; init; } = HandoffMode.Auto;

    /// <summary>
    /// Gets a value indicating whether the compositor is a greeter.
    /// </summary>
    public bool Greeter { get; init; }

    /// <summary>
    /// Gets a value indicating whether the greeter runs under greetd.
    /// </summary>
    public bool Greetd { get; init; }

    /// <summary>
    /// Creates options from a <c>--backend</c> value: <c>drm</c>, <c>nested</c> or <c>headless</c>, with an optional <c>:FD</c> suffix.
    /// </summary>
    /// <param name="name">The backend value.</param>
    /// <returns>The options with <see cref="Backend"/> and <see cref="SocketFd"/> set and every other option at its default.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or names an unknown backend.</exception>
    /// <exception cref="FormatException">The text after the colon is not a number.</exception>
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
