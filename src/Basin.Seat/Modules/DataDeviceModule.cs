using Basin.Capabilities;

namespace Basin.Seat;

public sealed class DataDeviceModule : IProtocolModule
{
    public string WireInterface => "wl_data_device_manager";

    public int Version => DataDeviceManager.Version;

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var seat = services.Require<Seat>();
        services.UseDefault<IDragTracker>(seat.DataDevice);
        return new DataDeviceManager(services.Display, seat);
    }
}
