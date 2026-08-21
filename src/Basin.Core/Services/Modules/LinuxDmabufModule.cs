using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

public sealed class LinuxDmabufModule : IProtocolModule
{
    private readonly DrmFormatSet _formats;
    private readonly string _mainDevicePath;
    private readonly FdLedger? _ledger;
    private readonly IReadOnlyList<(string DevicePath, DrmFormatSet Formats)>? _extraTranches;

    public LinuxDmabufModule(
        DrmFormatSet formats,
        string mainDevicePath,
        FdLedger? ledger = null,
        IReadOnlyList<(string DevicePath, DrmFormatSet Formats)>? extraTranches = null)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentException.ThrowIfNullOrEmpty(mainDevicePath);
        _formats = formats;
        _mainDevicePath = mainDevicePath;
        _ledger = ledger;
        _extraTranches = extraTranches;
    }

    public string WireInterface => "zwp_linux_dmabuf_v1";

    public int Version => LinuxDmabufGlobal.Version;

    public LinuxDmabufGlobal? Global { get; private set; }

    public void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.UseDefault<ICaptureDmabufConstraints>(
            new CaptureDmabufConstraints(_formats, _mainDevicePath));
    }

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
