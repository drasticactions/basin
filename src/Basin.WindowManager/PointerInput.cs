using Basin.WindowManager.Protocol;
using Wayland;

namespace Basin.WindowManager;

/// <summary>
/// Raw pointer input on the surfaces a manager draws, through its own binding of each <c>wl_seat</c>. It also sets cursor shapes through <c>wp_cursor_shape_manager_v1</c>.
/// </summary>
public sealed class PointerInput
{
    private readonly Dictionary<uint, SeatState> _seats = [];
    private WpCursorShapeManagerV1? _cursorShapes;

    /// <summary>
    /// Occurs when the pointer enters a surface. The arguments are the seat's registry name, the surface id, and the surface-local x and y in logical pixels.
    /// </summary>
    public event Action<uint, uint, double, double>? SurfaceEntered;

    /// <summary>
    /// Occurs when the pointer leaves a surface. The arguments are the seat's registry name and the surface id.
    /// </summary>
    public event Action<uint, uint>? SurfaceLeft;

    /// <summary>
    /// Occurs when the pointer moves on the entered surface. The arguments are the seat's registry name and the surface-local x and y in logical pixels.
    /// </summary>
    public event Action<uint, double, double>? PointerMoved;

    /// <summary>
    /// Occurs when a button goes down or up on the entered surface. The arguments are the seat's registry name, the Linux button code, and <see langword="true"/> for a press.
    /// </summary>
    public event Action<uint, uint, bool>? ButtonChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="PointerInput"/> class. The seats arrive on the manager's next dispatch.
    /// </summary>
    /// <param name="wm">The manager whose connection this object binds on.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wm"/> is <see langword="null"/>.</exception>
    public PointerInput(RiverWindowManager wm)
    {
        ArgumentNullException.ThrowIfNull(wm);
        var registry = wm.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            switch (e.Interface)
            {
                case "wl_seat":
                    var seat = registry.Bind<WlSeat>(e.Name, Math.Min(e.Version, 7));
                    var state = new SeatState(e.Name, seat);
                    _seats[e.Name] = state;
                    seat.Capabilities += (_, ce) => OnCapabilities(state, ce.Capabilities);
                    break;
                case "wp_cursor_shape_manager_v1":
                    _cursorShapes = registry.Bind<WpCursorShapeManagerV1>(e.Name, Math.Min(e.Version, 1));
                    break;
            }
        };
        registry.GlobalRemove += (_, e) =>
        {
            if (_seats.Remove(e.Name, out var removed))
            {
                removed.Release();
            }
        };
    }

    /// <summary>
    /// Sets the cursor shape on a seat's pointer while it is over one of the manager's surfaces. It sends nothing when the shape is already set since the last enter.
    /// </summary>
    /// <param name="seatName">The seat's registry name.</param>
    /// <param name="shape">The cursor shape.</param>
    /// <remarks>It does nothing when the compositor has no cursor-shape global, the seat is unknown, or the pointer has not entered a surface yet.</remarks>
    public void SetShape(uint seatName, WpCursorShapeDeviceV1.Shape shape)
    {
        if (_cursorShapes is null
            || !_seats.TryGetValue(seatName, out var state)
            || state.Pointer is null
            || state.EnterSerial == 0
            || state.LastShape == shape)
        {
            return;
        }

        state.Device ??= _cursorShapes.GetPointer(state.Pointer);
        state.Device.SetShape(state.EnterSerial, shape);
        state.LastShape = shape;
    }

    /// <summary>
    /// Hides the cursor on a seat's pointer. It does nothing before the pointer entered a surface.
    /// </summary>
    /// <param name="seatName">The seat's registry name.</param>
    public void HideCursor(uint seatName)
    {
        if (!_seats.TryGetValue(seatName, out var state)
            || state.Pointer is null
            || state.EnterSerial == 0)
        {
            return;
        }

        state.Pointer.SetCursor(state.EnterSerial, null, 0, 0);
        state.LastShape = null;
    }

    private void OnCapabilities(SeatState state, WlSeat.Capability capabilities)
    {
        if ((capabilities & WlSeat.Capability.Pointer) == 0 || state.Pointer is not null)
        {
            return;
        }

        var pointer = state.Seat.GetPointer();
        state.Pointer = pointer;
        pointer.Enter += (_, e) =>
        {
            state.EnterSerial = e.Serial;
            state.LastShape = null;
            if (e.Surface is { } surface)
            {
                state.EnteredSurfaceId = surface.Id;
                SurfaceEntered?.Invoke(state.Name, surface.Id, e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble());
            }
        };
        pointer.Leave += (_, e) =>
        {
            var id = state.EnteredSurfaceId;
            state.EnteredSurfaceId = 0;
            if (e.Surface is { } surface)
            {
                id = surface.Id;
            }

            if (id != 0)
            {
                SurfaceLeft?.Invoke(state.Name, id);
            }
        };
        pointer.Motion += (_, e) =>
            PointerMoved?.Invoke(state.Name, e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble());
        pointer.Button += (_, e) =>
            ButtonChanged?.Invoke(state.Name, e.Button, e.State == WlPointer.ButtonState.Pressed);
    }

    private sealed class SeatState(uint name, WlSeat seat)
    {
        public uint Name { get; } = name;

        public WlSeat Seat { get; } = seat;

        public WlPointer? Pointer { get; set; }

        public WpCursorShapeDeviceV1? Device { get; set; }

        public uint EnterSerial { get; set; }

        public uint EnteredSurfaceId { get; set; }

        public WpCursorShapeDeviceV1.Shape? LastShape { get; set; }

        public void Release()
        {
            if (Device is { IsDestroyed: false } device)
            {
                device.Destroy();
            }

            if (Pointer is { IsDestroyed: false } pointer)
            {
                pointer.Dispose();
            }

            if (!Seat.IsDestroyed)
            {
                Seat.Dispose();
            }
        }
    }
}
