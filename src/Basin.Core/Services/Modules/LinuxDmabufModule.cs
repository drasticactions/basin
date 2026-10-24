using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

/// <summary>
/// Installs <c>zwp_linux_dmabuf_v1</c> with the formats and devices of the renderer.
/// </summary>
public sealed class LinuxDmabufModule : IProtocolModule
{
    private readonly DrmFormatSet _formats;
    private readonly string _mainDevicePath;
    private readonly FdLedger? _ledger;
    private readonly IReadOnlyList<(string DevicePath, DrmFormatSet Formats)>? _extraTranches;
    private readonly DrmFormatSet? _captureFormats;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxDmabufModule"/> class.
    /// </summary>
    /// <param name="formats">The formats and modifiers that the main device can import.</param>
    /// <param name="mainDevicePath">The path of the main DRM device node. It must not be empty.</param>
    /// <param name="ledger">The ledger that records the descriptors of client planes, or <see langword="null"/>.</param>
    /// <param name="extraTranches">More devices and their formats, advertised as extra feedback tranches.</param>
    /// <param name="captureFormats">The formats that capture can write into dmabufs. When the set is not empty, the module seeds an <see cref="ICaptureDmabufConstraints"/>.</param>
    public LinuxDmabufModule(
        DrmFormatSet formats,
        string mainDevicePath,
        FdLedger? ledger = null,
        IReadOnlyList<(string DevicePath, DrmFormatSet Formats)>? extraTranches = null,
        DrmFormatSet? captureFormats = null)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentException.ThrowIfNullOrEmpty(mainDevicePath);
        _formats = formats;
        _mainDevicePath = mainDevicePath;
        _ledger = ledger;
        _extraTranches = extraTranches;
        _captureFormats = captureFormats;
    }

    /// <inheritdoc/>
    public string WireInterface => "zwp_linux_dmabuf_v1";

    /// <inheritdoc/>
    public int Version => LinuxDmabufGlobal.Version;

    /// <summary>
    /// Gets the <c>zwp_linux_dmabuf_v1</c> global that <see cref="Install"/> made, or <see langword="null"/> before the install.
    /// </summary>
    public LinuxDmabufGlobal? Global { get; private set; }

    /// <inheritdoc/>
    public void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (_captureFormats is { Count: > 0 } captureFormats)
        {
            services.UseDefault<ICaptureDmabufConstraints>(
                new CaptureDmabufConstraints(captureFormats, _mainDevicePath) { Wrappable = _formats });
        }
    }

    /// <inheritdoc/>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Global = new LinuxDmabufGlobal(
            services.Display,
            services.Require<ClientBufferRegistry>(),
            _formats,
            _mainDevicePath,
            _ledger,
            services.Find<CompositorGlobal>(),
            _extraTranches);
        services.Use(Global);
        return Global;
    }
}
