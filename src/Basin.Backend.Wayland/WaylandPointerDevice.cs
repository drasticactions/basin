using System.Runtime.InteropServices;
using Basin.Backend.Wayland.Protocol;
using Wayland;

namespace Basin.Backend.Wayland;

/// <summary>
/// Reports the parent seat's pointer on this backend's outputs, in output-local physical pixels. Events over a host frame go to that frame instead.
/// </summary>
public sealed class WaylandPointerDevice : IParentCursor, IDisposable
{
    private readonly ParentCursorSurface _cursor;
    private readonly ParentCursorSurface _frameCursor;
    private readonly WlPointer _pointer;
    private ZwpPointerGestureSwipeV1? _swipe;
    private ZwpRelativePointerV1? _relative;
    private ZwpPointerGesturePinchV1? _pinch;
    private ZwpPointerGestureHoldV1? _hold;
    private WaylandOutput? _current;

    private WaylandHostFrame? _frame;
    private Point _frameOrigin;
    private uint _frameSerial;
    private WlPointer.AxisSource _axisSource = WlPointer.AxisSource.Wheel;
    private ScrollAxisState _verticalAxis;
    private ScrollAxisState _horizontalAxis;
    private readonly List<uint> _held = [];

    internal WaylandPointerDevice(WaylandBackend backend, WlPointer pointer)
    {
        _cursor = new ParentCursorSurface(backend, pointer);
        _frameCursor = new ParentCursorSurface(backend, pointer);
        _pointer = pointer;
        pointer.Enter += (_, e) =>
        {
            backend.LastPointerSerial = e.Serial;
            var output = backend.FindOutput(e.Surface);
            if (output is not null)
            {
                _current = output;
                _frame = null;
                _frameCursor.NotifyLeave();
                _cursor.NotifyEnter(e.Serial);
                Enter?.Invoke(output, e.SurfaceX.ToDouble() * output.SurfaceToPhysical, e.SurfaceY.ToDouble() * output.SurfaceToPhysical);
                return;
            }

            if (backend.FindHostFrame(e.Surface, out var origin) is { } frame)
            {
                _current = null;
                _frame = frame;
                _frameOrigin = origin;

                _frameSerial = e.Serial;
                if (_frameCursor.HasImage)
                {
                    _cursor.NotifyLeave();
                    _frameCursor.NotifyEnter(e.Serial);
                }
                else
                {
                    _cursor.NotifyEnter(e.Serial);
                }

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
            _frameCursor.NotifyLeave();

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

    /// <summary>Occurs when the pointer enters an output. The arguments are the output and X and Y in output-local physical pixels.</summary>
    public event Action<WaylandOutput, double, double>? Enter;

    /// <summary>Occurs when the pointer leaves an output, or when the parent removes the pointer while it is on one.</summary>
    public event Action? Leave;

    /// <summary>
    /// Occurs when the pointer moves on an output. The arguments are the time in milliseconds and X and Y in output-local physical pixels.
    /// </summary>
    public event Action<uint, double, double>? Motion;

    /// <summary>
    /// Occurs when a button changes state outside the host frame. The arguments are the time in milliseconds, the Linux input event code and the pressed state.
    /// </summary>
    public event Action<uint, uint, bool>? Button;

    /// <summary>
    /// Occurs once per axis at the end of each parent pointer frame that carried scroll events. The first argument is the time in milliseconds.
    /// Scroll over the host frame is dropped.
    /// </summary>
    public event Action<uint, PointerAxis>? Axis;

    /// <summary>Occurs when a swipe gesture starts. The arguments are the time in milliseconds and the finger count.</summary>
    public event Action<uint, uint>? SwipeBegin;

    /// <summary>Occurs when a swipe gesture moves. The arguments are the time in milliseconds and the delta in physical pixels.</summary>
    public event Action<uint, double, double>? SwipeUpdate;

    /// <summary>Occurs when a swipe gesture ends. The arguments are the time in milliseconds and whether the gesture was cancelled.</summary>
    public event Action<uint, bool>? SwipeEnd;

    /// <summary>Occurs when a pinch gesture starts. The arguments are the time in milliseconds and the finger count.</summary>
    public event Action<uint, uint>? PinchBegin;

    /// <summary>
    /// Occurs when a pinch gesture changes. The arguments are the time in milliseconds, the delta in physical pixels, the absolute scale and the rotation delta in degrees.
    /// </summary>
    public event Action<uint, double, double, double, double>? PinchUpdate;

    /// <summary>Occurs when a pinch gesture ends. The arguments are the time in milliseconds and whether the gesture was cancelled.</summary>
    public event Action<uint, bool>? PinchEnd;

    /// <summary>
    /// Occurs when a hold gesture starts. The arguments are the time in milliseconds and the finger count. A parent below gestures version 3 never raises it.
    /// </summary>
    public event Action<uint, uint>? HoldBegin;

    /// <summary>Occurs when a hold gesture ends. The arguments are the time in milliseconds and whether the gesture was cancelled.</summary>
    public event Action<uint, bool>? HoldEnd;

    /// <summary>
    /// Occurs on relative pointer motion. The arguments are the time in microseconds, the accelerated delta in physical pixels and the unaccelerated delta.
    /// </summary>
    public event Action<ulong, double, double, double, double>? RelativeMotion;

    /// <summary>Gets a value indicating whether the parent has a relative pointer manager, so <see cref="RelativeMotion"/> can occur.</summary>
    public bool HasRelativeMotion => _relative is not null;

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

    internal void Remove(uint timeMs)
    {
        ReleaseHeldButtons(timeMs);
        var frame = _frame;
        var inside = _current is not null;
        _current = null;
        _frame = null;
        if (frame is not null)
        {
            frame.OnPointerLeave();
        }
        else if (inside)
        {
            Leave?.Invoke();
        }

        Dispose();
        WaylandBackend.ReleaseInputProxy(_pointer);
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

    /// <summary>
    /// Shows <paramref name="image"/> as the parent cursor over the outputs. The pixels are copied, so the caller keeps the buffer.
    /// </summary>
    /// <param name="image">The cursor image in physical pixels.</param>
    /// <param name="hotspotX">The hotspot X in physical pixels.</param>
    /// <param name="hotspotY">The hotspot Y in physical pixels.</param>
    /// <param name="scale">The density the image is meant for, usually the scale of the output under the pointer.</param>
    /// <returns><see langword="false"/> when the parent cursor surface cannot show the image.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    public bool SetCursor(IBuffer image, int hotspotX, int hotspotY, double scale = 1.0)
    {
        ArgumentNullException.ThrowIfNull(image);
        return _cursor.Show(image, hotspotX, hotspotY, scale);
    }

    /// <summary>Hides the parent cursor over the outputs. The cursor over the host frame is not changed.</summary>
    public void HideCursor() => _cursor.Hide();

    internal bool SetFrameCursor(IBuffer image, int hotspotX, int hotspotY, double scale)
    {
        if (_frame is not null)
        {
            _cursor.NotifyLeave();
            _frameCursor.NotifyEnter(_frameSerial);
        }

        return _frameCursor.Show(image, hotspotX, hotspotY, scale);
    }

    /// <summary>Destroys the gesture and relative pointer objects and both parent cursor surfaces.</summary>
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
        _frameCursor.Dispose();
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
