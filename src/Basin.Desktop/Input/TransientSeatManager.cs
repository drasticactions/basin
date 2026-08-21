using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Basin.Seat;
using Wayland;
using Wayland.Server;
using Xkb;

namespace Basin.Desktop;

public sealed class TransientSeatManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;

    public TransientSeatManager(WlServerDisplay display)
    {
        _global = display.CreateGlobal(ExtTransientSeatManagerV1.Interface, Version, OnBind);
    }

    public sealed class SeatRequest
    {
        private readonly ExtTransientSeatV1Resource _resource;
        internal bool Handled;

        internal SeatRequest(ExtTransientSeatV1Resource resource) => _resource = resource;

        public void Ready(Basin.Seat.Seat seat)
        {
            ArgumentNullException.ThrowIfNull(seat);
            Ready(seat.NameFor(_resource.Client));
        }

        public void Ready(WlGlobal seatGlobal)
        {
            ArgumentNullException.ThrowIfNull(seatGlobal);
            Ready(seatGlobal.NameFor(_resource.Client));
        }

        private void Ready(uint seatGlobalName)
        {
            Handled = true;
            if (!_resource.IsDestroyed)
            {
                _resource.SendReady(seatGlobalName);
            }
        }

        public void Deny()
        {
            Handled = true;
            if (!_resource.IsDestroyed)
            {
                _resource.SendDenied();
            }
        }
    }

    public event Action<SeatRequest>? SeatRequested;

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ExtTransientSeatManagerV1Resource(client, version, id);
        manager.Create += (_, e) =>
        {
            var seat = new ExtTransientSeatV1Resource(client, manager.Version, e.Seat);
            var request = new SeatRequest(seat);
            SeatRequested?.Invoke(request);
            if (!request.Handled)
            {
                seat.SendDenied();
            }
        };
    }
}
