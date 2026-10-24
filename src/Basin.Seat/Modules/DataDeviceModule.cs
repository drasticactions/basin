using Basin.Capabilities;

namespace Basin.Seat;

/// <summary>
/// Installs the <c>wl_data_device_manager</c> global for the <see cref="Seat"/> in the registry.
/// It also registers the <see cref="Seat.DataDevice"/> of that seat as the default <see cref="IDragTracker"/>.
/// </summary>
public sealed class DataDeviceModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "wl_data_device_manager";

    /// <inheritdoc/>
    public int Version => DataDeviceManager.Version;

    /// <summary>
    /// Creates the global for the registered <see cref="Seat"/>.
    /// The registry must hold a <see cref="Seat"/>, or the call throws.
    /// </summary>
    /// <param name="services">The registry to read the seat from and to add the drag tracker to.</param>
    /// <returns>The global. Disposing it removes the global.</returns>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var seat = services.Require<Seat>();
        services.UseDefault<IDragTracker>(seat.DataDevice);
        return new DataDeviceManager(services.Display, seat);
    }
}
