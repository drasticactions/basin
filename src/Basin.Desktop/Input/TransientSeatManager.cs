using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Basin.Seat;
using Wayland;
using Wayland.Server;
using Xkb;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>ext_transient_seat_manager_v1</c> global. The consumer answers each request through <see cref="SeatRequested"/>, and an unanswered request is denied.
/// </summary>
public sealed class TransientSeatManager : IDisposable
{
    /// <summary>
    /// The highest global version that the manager advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlServerDisplay _display;
    private readonly CompositorGlobal? _compositor;
    private readonly IKeymapSource? _keymaps;
    private readonly WlGlobal _global;
    private readonly List<Basin.Seat.Seat> _seats = [];
    private int _created;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransientSeatManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display to create the global on.</param>
    /// <param name="compositor">The compositor that new seats need. When it is <see langword="null"/>, <see cref="SeatRequest.Create"/> denies every request.</param>
    /// <param name="keymaps">The keymap source for the keyboards of new seats, or <see langword="null"/> for the seat default.</param>
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

    /// <summary>
    /// One client request for a transient seat. The <see cref="SeatRequested"/> handler must answer it before it returns.
    /// </summary>
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

        /// <summary>
        /// Gets the client that asked for the seat.
        /// </summary>
        public WlClient Client => _resource.Client;

        /// <summary>
        /// Creates a seat that the manager owns, sends <c>ready</c> with its global name, and retires the seat when the client destroys the transient seat.
        /// </summary>
        /// <param name="sink">A factory for the input sink of the new seat. When it is <see langword="null"/>, the seat gets a plain <see cref="SeatInputSink"/>.</param>
        /// <returns>The new seat, or <see langword="null"/> when the manager has no compositor or the client already destroyed the request.</returns>
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

        /// <summary>
        /// Answers the request with an existing seat that the consumer owns.
        /// </summary>
        /// <param name="seat">The seat whose global name the client receives.</param>
        public void Ready(Basin.Seat.Seat seat)
        {
            ArgumentNullException.ThrowIfNull(seat);
            Ready(seat.NameFor(_resource.Client));
        }

        /// <summary>
        /// Answers the request with an existing <c>wl_seat</c> global.
        /// </summary>
        /// <param name="seatGlobal">The seat global whose name the client receives.</param>
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

        /// <summary>
        /// Answers the request with <c>denied</c>.
        /// </summary>
        public void Deny()
        {
            Handled = true;
            if (!_resource.IsDestroyed)
            {
                _resource.SendDenied();
            }
        }
    }

    /// <summary>
    /// Occurs when a client asks for a transient seat. A request that the handler does not answer is denied.
    /// </summary>
    public event Action<SeatRequest>? SeatRequested;

    /// <summary>
    /// Occurs when <see cref="SeatRequest.Create"/> creates a seat, before the client receives <c>ready</c>.
    /// </summary>
    public event Action<Basin.Seat.Seat>? SeatCreated;

    /// <summary>
    /// Disposes every seat that the manager created and removes the global.
    /// </summary>
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
