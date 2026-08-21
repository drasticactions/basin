using System.Runtime.InteropServices;
using Wayland;
using Wayland.Server;

namespace Basin.Seat;

public sealed class DataDeviceManager : IDisposable
{
    public const int Version = 4;

    private readonly WlGlobal _global;
    private readonly Seat[] _seats;

    public DataDeviceManager(WlServerDisplay display, params Seat[] seats)
    {
        _seats = seats;
        _global = display.CreateGlobal(WlDataDeviceManager.Interface, Version, OnBind);
    }

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
