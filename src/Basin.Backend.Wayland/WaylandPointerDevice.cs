using System.Runtime.InteropServices;
using Basin.Backend.Wayland.Protocol;
using Wayland;

namespace Basin.Backend.Wayland;

public sealed class WaylandPointerDevice : IParentCursor, IDisposable
{
    private readonly ParentCursorSurface _cursor;
    private readonly WlPointer _pointer;
    private ZwpPointerGestureSwipeV1? _swipe;
    private ZwpRelativePointerV1? _relative;
    private ZwpPointerGesturePinchV1? _pinch;
    private ZwpPointerGestureHoldV1? _hold;
    private WaylandOutput? _current;

    private WaylandHostFrame? _frame;
    private Point _frameOrigin;
    private WlPointer.AxisSource _axisSource = WlPointer.AxisSource.Wheel;
    private ScrollAxisState _verticalAxis;
    private ScrollAxisState _horizontalAxis;
    private readonly List<uint> _held = [];

    internal WaylandPointerDevice(WaylandBackend backend, WlPointer pointer)
    {
        _cursor = new ParentCursorSurface(backend, pointer);
        _pointer = pointer;
        pointer.Enter += (_, e) =>
        {
            backend.LastPointerSerial = e.Serial;
            var output = backend.FindOutput(e.Surface);
            if (output is not null)
            {
                _current = output;
                _frame = null;
                _cursor.NotifyEnter(e.Serial);
                Enter?.Invoke(output, e.SurfaceX.ToDouble() * output.SurfaceToPhysical, e.SurfaceY.ToDouble() * output.SurfaceToPhysical);
                return;
            }

            if (backend.FindHostFrame(e.Surface, out var origin) is { } frame)
            {
                _current = null;
                _frame = frame;
                _frameOrigin = origin;

                _cursor.NotifyEnter(e.Serial);
                frame.OnPointerEnter(
                    e.SurfaceX.ToDouble() + origin.X,
                    e.SurfaceY.ToDouble() + origin.Y);
            }
        };
        pointer.Leave += (_, _) =>
        {
            var frame = _frame;
            _current = null;
            _frame = null;
            _cursor.NotifyLeave();

            if (frame is not null)
            {
                frame.OnPointerLeave();
            }
            else
            {
                Leave?.Invoke();
            }
        };
        pointer.Motion += (_, e) =>
        {
            if (_frame is { } frame)
            {
                frame.OnPointerMotion(
                    e.Time,
                    e.SurfaceX.ToDouble() + _frameOrigin.X,
                    e.SurfaceY.ToDouble() + _frameOrigin.Y);
                return;
            }

            var factor = _current?.SurfaceToPhysical ?? 1;
            Motion?.Invoke(e.Time, e.SurfaceX.ToDouble() * factor, e.SurfaceY.ToDouble() * factor);
        };
        pointer.Button += (_, e) =>
        {
            backend.LastPointerSerial = e.Serial;
            var pressed = e.State == WlPointer.ButtonState.Pressed;
            if (pressed)
            {
                backend.LastPointerButtonSerial = e.Serial;
            }

            if (_frame is { } frame)
            {
                frame.OnPointerButton(e.Time, e.Button, pressed);
                return;
            }

            if (pressed)
            {
                if (!_held.Contains(e.Button))
                {
                    _held.Add(e.Button);
                }
            }
            else
            {
                _held.Remove(e.Button);
            }

            Button?.Invoke(e.Time, e.Button, pressed);
        };
        pointer.AxisSourceEvent += (_, e) => _axisSource = e.AxisSource;
        pointer.AxisRelativeDirectionEvent += (_, e) => AxisState(e.Axis).Direction = e.Direction;
        pointer.AxisValue120 += (_, e) => AxisState(e.Axis).Value120 += e.Value120;
        pointer.AxisEvent += (_, e) =>
        {
            ref var state = ref AxisState(e.Axis);
            state.Time = e.Time;
            state.Value += e.Value.ToDouble();
            state.Pending = true;
            if (pointer.Version < 5)
            {
                FlushAxis(e.Axis);
            }
        };
        pointer.AxisStop += (_, e) =>
        {
            ref var state = ref AxisState(e.Axis);
            state.Time = e.Time;
            state.Stopped = true;
            state.Pending = true;
        };
        pointer.Frame += (_, _) =>
        {
            FlushAxis(WlPointer.Axis.VerticalScroll);
            FlushAxis(WlPointer.Axis.HorizontalScroll);
        };
    }

    private ref ScrollAxisState AxisState(WlPointer.Axis axis) =>
        ref (axis == WlPointer.Axis.HorizontalScroll ? ref _horizontalAxis : ref _verticalAxis);

    private void FlushAxis(WlPointer.Axis axis)
    {
        ref var state = ref AxisState(axis);
        var pending = state.Pending;
        var (time, value, value120, stopped, direction) =
            (state.Time, state.Value, state.Value120, state.Stopped, state.Direction);
        state = default;
        if (!pending || _frame is not null)
        {
            return;
        }

        Axis?.Invoke(
            time,
            new PointerAxis(
                axis,
                stopped ? 0 : value,
                stopped ? 0 : value120,
                _axisSource,
                direction));
    }

    public event Action<WaylandOutput, double, double>? Enter;

    public event Action? Leave;

    public event Action<uint, double, double>? Motion;

    public event Action<uint, uint, bool>? Button;

    public event Action<uint, PointerAxis>? Axis;

    public event Action<uint, uint>? SwipeBegin;

    public event Action<uint, double, double>? SwipeUpdate;

    public event Action<uint, bool>? SwipeEnd;

    public event Action<uint, uint>? PinchBegin;

    public event Action<uint, double, double, double, double>? PinchUpdate;

    public event Action<uint, bool>? PinchEnd;

    public event Action<uint, uint>? HoldBegin;

    public event Action<uint, bool>? HoldEnd;

    public event Action<ulong, double, double, double, double>? RelativeMotion;

    internal WlPointer Proxy => _pointer;

    internal WaylandOutput? CurrentOutput => _current;

    internal void ReleaseHeldButtons(uint timeMs)
    {
        for (var i = _held.Count - 1; i >= 0; i--)
        {
            var button = _held[i];
            _held.RemoveAt(i);
            Button?.Invoke(timeMs, button, false);
        }
    }

    internal void AttachRelativePointer(ZwpRelativePointerManagerV1 manager)
    {
        if (_relative is not null)
        {
            return;
        }

        _relative = manager.GetRelativePointer(_pointer);
        _relative.RelativeMotion += (_, e) =>
        {
            var factor = _current?.SurfaceToPhysical ?? 1;
            RelativeMotion?.Invoke(
                ((ulong)e.UtimeHi << 32) | e.UtimeLo,
                e.Dx.ToDouble() * factor,
                e.Dy.ToDouble() * factor,
                e.DxUnaccel.ToDouble(),
                e.DyUnaccel.ToDouble());
        };
    }

    internal void AttachGestures(ZwpPointerGesturesV1 gestures)
    {
        if (_swipe is not null)
        {
            return;
        }

        _swipe = gestures.GetSwipeGesture(_pointer);
        _swipe.Begin += (_, e) => SwipeBegin?.Invoke(e.Time, e.Fingers);
        _swipe.Update += (_, e) =>
        {
            var factor = _current?.SurfaceToPhysical ?? 1;
            SwipeUpdate?.Invoke(e.Time, e.Dx.ToDouble() * factor, e.Dy.ToDouble() * factor);
        };
        _swipe.End += (_, e) => SwipeEnd?.Invoke(e.Time, e.Cancelled != 0);

        _pinch = gestures.GetPinchGesture(_pointer);
        _pinch.Begin += (_, e) => PinchBegin?.Invoke(e.Time, e.Fingers);
        _pinch.Update += (_, e) =>
        {
            var factor = _current?.SurfaceToPhysical ?? 1;
            PinchUpdate?.Invoke(
                e.Time,
                e.Dx.ToDouble() * factor,
                e.Dy.ToDouble() * factor,
                e.Scale.ToDouble(),
                e.Rotation.ToDouble());
        };
        _pinch.End += (_, e) => PinchEnd?.Invoke(e.Time, e.Cancelled != 0);

        if (gestures.Version < 3)
        {
            return;
        }

        _hold = gestures.GetHoldGesture(_pointer);
        _hold.Begin += (_, e) => HoldBegin?.Invoke(e.Time, e.Fingers);
        _hold.End += (_, e) => HoldEnd?.Invoke(e.Time, e.Cancelled != 0);
    }

    public bool SetCursor(IBuffer image, int hotspotX, int hotspotY, double scale = 1.0)
    {
        ArgumentNullException.ThrowIfNull(image);
        return _cursor.Show(image, hotspotX, hotspotY, scale);
    }

    public void HideCursor() => _cursor.Hide();

    public void Dispose()
    {
        _relative?.Dispose();
        _relative = null;
        _hold?.Dispose();
        _pinch?.Dispose();
        _swipe?.Dispose();
        _hold = null;
        _pinch = null;
        _swipe = null;
        _cursor.Dispose();
    }

    private struct ScrollAxisState
    {
        public uint Time;
        public double Value;
        public int Value120;
        public WlPointer.AxisRelativeDirection Direction;
        public bool Stopped;
        public bool Pending;
    }
}
