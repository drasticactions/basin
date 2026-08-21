using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

public sealed class ShmModule : IProtocolModule
{
    private readonly DrmFormat[] _extraFormats;

    public ShmModule(params DrmFormat[] extraFormats) => _extraFormats = extraFormats;

    public ShmLimits? Limits { get; init; }

    public string WireInterface => "wl_shm";

    public int Version => ShmGlobal.AdvertisedVersion;

    public ShmGlobal? Global { get; private set; }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Global = new ShmGlobal(services.Display, _extraFormats, services.Find<ClientBufferRegistry>(), Limits);
        services.Use(Global);

        return Global;
    }
}
