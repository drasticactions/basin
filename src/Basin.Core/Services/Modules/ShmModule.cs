using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

/// <summary>
/// Installs <c>wl_shm</c> and registers the <see cref="ShmGlobal"/>.
/// </summary>
public sealed class ShmModule : IProtocolModule
{
    private readonly DrmFormat[] _extraFormats;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShmModule"/> class.
    /// </summary>
    /// <param name="extraFormats">The formats to advertise in addition to ARGB8888 and XRGB8888.</param>
    public ShmModule(params DrmFormat[] extraFormats) => _extraFormats = extraFormats;

    /// <summary>
    /// Gets the size limits for client pools, or <see langword="null"/> for the defaults of <see cref="ShmGlobal"/>.
    /// </summary>
    public ShmLimits? Limits { get; init; }

    /// <inheritdoc/>
    public string WireInterface => "wl_shm";

    /// <inheritdoc/>
    public int Version => ShmGlobal.AdvertisedVersion;

    /// <summary>
    /// Gets the <c>wl_shm</c> global that <see cref="Install"/> made, or <see langword="null"/> before the install.
    /// </summary>
    public ShmGlobal? Global { get; private set; }

    /// <inheritdoc/>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Global = new ShmGlobal(services.Display, _extraFormats, services.Find<ClientBufferRegistry>(), Limits);
        services.Use(Global);

        return Global;
    }
}
