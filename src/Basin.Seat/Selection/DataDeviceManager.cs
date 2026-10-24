using System.Runtime.InteropServices;
using Wayland;
using Wayland.Server;

namespace Basin.Seat;

/// <summary>
/// Advertises the <c>wl_data_device_manager</c> global, creates data sources and binds each data device to its seat.
/// </summary>
public sealed class DataDeviceManager : IDisposable
{
    /// <summary>
    /// The highest <c>wl_data_device_manager</c> version that the global advertises.
    /// </summary>
    public const int Version = 4;

    private readonly WlGlobal _global;
    private readonly Seat[] _seats;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataDeviceManager"/> class. The global is advertised at once.
    /// </summary>
    /// <param name="display">The display that gets the global.</param>
    /// <param name="seats">The seats to search first for the seat that a client names. A seat that is not in this list is found from its resource.</param>
    public DataDeviceManager(WlServerDisplay display, params Seat[] seats)
    {
        _seats = seats;
        _global = display.CreateGlobal(WlDataDeviceManager.Interface, Version, OnBind);
    }

    /// <summary>
    /// Removes the global.
    /// </summary>
    public void Dispose() => _global.Dispose();

    private Seat? Resolve(WlSeatResource? resource)
    {
        if (resource is null)
        {
            return null;
        }

        foreach (var seat in _seats)
        {
            if (seat.OwnsResource(resource))
            {
                return seat;
            }
        }

        return Seat.FromResource(resource);
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new WlDataDeviceManagerResource(client, version, id);

        manager.CreateDataSource += (_, e) =>
        {
            var resource = new WlDataSourceResource(client, manager.Version, e.Id);
            DataSourceRegistry.Register(new DataSource(resource));
        };

        manager.GetDataDevice += (_, e) =>
        {
            if (Resolve(e.Seat) is not { } seat)
            {
                manager.PostError(0, "unknown seat");
                return;
            }

            var device = new WlDataDeviceResource(client, manager.Version, e.Id);
            var seatClient = seat.ClientFor(client);
            seatClient.AddDataDevice(device);
            seat.DataDevice.WireDevice(seatClient, device);
        };
    }
}
