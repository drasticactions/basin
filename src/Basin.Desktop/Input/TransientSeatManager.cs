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

    private readonly WlServerDisplay _display;
    private readonly CompositorGlobal? _compositor;
    private readonly IKeymapSource? _keymaps;
    private readonly WlGlobal _global;
    private readonly List<Basin.Seat.Seat> _seats = [];
    private int _created;

    public TransientSeatManager(
        WlServerDisplay display,
        CompositorGlobal? compositor = null,
        IKeymapSource? keymaps = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        _display = display;
        _compositor = compositor;
        _keymaps = keymaps;
        _global = display.CreateGlobal(ExtTransientSeatManagerV1.Interface, Version, OnBind);
    }

    public sealed class SeatRequest
    {
        private readonly TransientSeatManager _owner;
        private readonly ExtTransientSeatV1Resource _resource;
        internal bool Handled;

        internal SeatRequest(TransientSeatManager owner, ExtTransientSeatV1Resource resource)
        {
            _owner = owner;
            _resource = resource;
        }

        public WlClient Client => _resource.Client;

        public Basin.Seat.Seat? Create(Func<Basin.Seat.Seat, IInputSink>? sink = null)
        {
            if (_owner._compositor is not { } compositor)
            {
                Deny();
                return null;
            }

            var seat = new Basin.Seat.Seat(
                _owner._display,
                compositor,
                $"seat-{++_owner._created}",
                SeatCapability.None);
            if (_owner._keymaps is { } keymaps)
            {
                seat.Keyboard.KeymapSource = keymaps;
            }

            seat.InputSink = sink is null ? new SeatInputSink(seat) : sink(seat);
            _owner._seats.Add(seat);
            _owner.SeatCreated?.Invoke(seat);

            if (_resource.IsDestroyed)
            {
                _owner.Release(seat);
                return null;
            }

            _resource.Destroyed += (_, _) => _owner.Release(seat);
            Ready(seat);
            return seat;
        }

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

    public event Action<Basin.Seat.Seat>? SeatCreated;

    public void Dispose()
    {
        for (var i = _seats.Count - 1; i >= 0; i--)
        {
            var seat = _seats[i];
            _seats.RemoveAt(i);
            seat.Dispose();
        }

        _global.Dispose();
    }

    private void Release(Basin.Seat.Seat seat)
    {
        if (!_seats.Remove(seat))
        {
            return;
        }

        seat.Retire();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ExtTransientSeatManagerV1Resource(client, version, id);
        manager.Create += (_, e) =>
        {
            var seat = new ExtTransientSeatV1Resource(client, manager.Version, e.Seat);
            var request = new SeatRequest(this, seat);
            SeatRequested?.Invoke(request);
            if (!request.Handled)
            {
                seat.SendDenied();
            }
        };
    }
}
