using Basin.WindowManager.Protocol;
using Wayland;

namespace Basin.WindowManager;

public sealed class PointerInput
{
    private readonly Dictionary<uint, SeatState> _seats = [];
    private WpCursorShapeManagerV1? _cursorShapes;

    public event Action<uint, uint, double, double>? SurfaceEntered;

    public event Action<uint, uint>? SurfaceLeft;

    public event Action<uint, double, double>? PointerMoved;

    public event Action<uint, uint, bool>? ButtonChanged;

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
