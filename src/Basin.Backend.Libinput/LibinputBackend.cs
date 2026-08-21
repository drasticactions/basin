using Basin.Session;
using Libinput;
using Udev;

namespace Basin.Backend.Libinput;

public sealed class LibinputBackend : IDisposable
{
    private readonly ICompositorEventLoop _loop;
    private readonly ISession _session;
    private readonly Dictionary<nint, InputDevice> _devices = [];

    private UdevContext? _udev;
    private LibinputContext? _libinput;
    private IEventSource? _source;
    private bool _suspended;

    public LibinputBackend(ICompositorEventLoop loop, ISession session)
    {
        _loop = loop;
        _session = session;
    }

    public IReadOnlyCollection<InputDevice> Devices => _devices.Values;

    public bool HasTouchDevice
    {
        get
        {
            foreach (var device in _devices.Values)
            {
                if (device.HasTouch)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public event Action<InputDevice>? DeviceAdded;

    public event Action<InputDevice>? DeviceRemoved;

    public event Action<InputDevice, uint, uint, bool>? Key;

    public event Action<InputDevice, uint, double, double, double, double>? PointerMotion;

    public event Action<InputDevice, uint, double, double>? PointerMotionAbsolute;

    public event Action<InputDevice, uint, uint, bool>? PointerButton;

    public event Action<InputDevice, uint, PointerAxis>? PointerScroll;

    public event Action<InputDevice, uint, int, double, double>? TouchDown;

    public event Action<InputDevice, uint, int>? TouchUp;

    public event Action<InputDevice, uint, int, double, double>? TouchMotion;

    public event Action<InputDevice>? TouchFrame;

    public event Action<InputDevice>? TouchCancel;

    public event Action<InputDevice, LibinputEventType, LibinputGestureEvent>? Gesture;

    public event Action<InputDevice, LibinputSwitchEvent>? SwitchToggled;

    public event Action<InputDevice, LibinputTabletToolEvent>? TabletToolProximity;

    public event Action<InputDevice, LibinputTabletToolEvent>? TabletToolAxis;

    public event Action<InputDevice, LibinputTabletToolEvent>? TabletToolTip;

    public event Action<InputDevice, LibinputTabletToolEvent>? TabletToolButton;

    public event Action<InputDevice, LibinputEventType, LibinputTabletPadEvent>? TabletPad;

    public void Start()
    {
        _udev = new UdevContext();
        _libinput = LibinputContext.CreateUdevContext(_udev, new SessionInterface(_session));
        _libinput.AssignSeat(_session.SeatName);
        _source = _loop.AddFd(_libinput.Fd, FdReadiness.Readable, (_, _) => Dispatch());
        _session.Enabled += OnSessionEnabled;
        _session.Disabled += OnSessionDisabled;
        Dispatch();
    }

    public void Dispose()
    {
        _session.Enabled -= OnSessionEnabled;
        _session.Disabled -= OnSessionDisabled;
        _source?.Remove();
        foreach (var device in _devices.Values)
        {
            device.Native.Dispose();
        }

        _devices.Clear();
        _libinput?.Dispose();
        _udev?.Dispose();
    }

    private void OnSessionDisabled()
    {
        if (!_suspended && _libinput is not null)
        {
            _suspended = true;
            _libinput.Suspend();
        }
    }

    private void OnSessionEnabled()
    {
        if (_suspended && _libinput is not null)
        {
            _suspended = false;
            _libinput.Resume();
            Dispatch();
        }
    }

    private void Dispatch()
    {
        var libinput = _libinput!;
        libinput.Dispatch();
        while (libinput.TryGetEvent() is { } ev)
        {
            using (ev)
            {
                Handle(ev);
            }
        }
    }

    private void Handle(LibinputEvent ev)
    {
        switch (ev)
        {
            case LibinputDeviceNotifyEvent:
                OnDeviceNotify(ev);
                return;
        }

        using var nativeDevice = ev.GetDevice();
        if (!_devices.TryGetValue(nativeDevice.NativeHandle, out var device))
        {
            return;
        }

        switch (ev)
        {
            case LibinputKeyboardEvent key:
                Key?.Invoke(device, TimeMs(key.TimestampMicroseconds), key.Key, key.KeyState == LibinputKeyState.Pressed);
                break;

            case LibinputPointerMotionEvent motion:
                PointerMotion?.Invoke(device, TimeMs(motion.TimestampMicroseconds), motion.Dx, motion.Dy, motion.DxUnaccelerated, motion.DyUnaccelerated);
                break;

            case LibinputPointerMotionAbsoluteEvent absolute:
                PointerMotionAbsolute?.Invoke(device, TimeMs(absolute.TimestampMicroseconds), absolute.TransformedX(1), absolute.TransformedY(1));
                break;

            case LibinputPointerButtonEvent button:
                PointerButton?.Invoke(device, TimeMs(button.TimestampMicroseconds), button.Button, button.ButtonState == LibinputButtonState.Pressed);
                break;

            case LibinputPointerScrollEvent { Type: not LibinputEventType.PointerAxis } scroll:
                var source = ev.Type switch
                {
                    LibinputEventType.PointerScrollFinger => ScrollSource.Finger,
                    LibinputEventType.PointerScrollContinuous => ScrollSource.Continuous,
                    _ => ScrollSource.Wheel,
                };
                EmitScroll(device, scroll, LibinputPointerAxis.ScrollVertical, ScrollOrientation.Vertical, source);
                EmitScroll(device, scroll, LibinputPointerAxis.ScrollHorizontal, ScrollOrientation.Horizontal, source);
                break;

            case LibinputTouchEvent touch:
                switch (ev.Type)
                {
                    case LibinputEventType.TouchDown:
                        TouchDown?.Invoke(device, TimeMs(touch.TimestampMicroseconds), touch.SeatSlot, touch.TransformedX(1), touch.TransformedY(1));
                        break;
                    case LibinputEventType.TouchUp:
                        TouchUp?.Invoke(device, TimeMs(touch.TimestampMicroseconds), touch.SeatSlot);
                        break;
                    case LibinputEventType.TouchMotion:
                        TouchMotion?.Invoke(device, TimeMs(touch.TimestampMicroseconds), touch.SeatSlot, touch.TransformedX(1), touch.TransformedY(1));
                        break;
                    case LibinputEventType.TouchFrame:
                        TouchFrame?.Invoke(device);
                        break;
                    case LibinputEventType.TouchCancel:
                        TouchCancel?.Invoke(device);
                        break;
                }

                break;

            case LibinputGestureEvent gesture:
                Gesture?.Invoke(device, ev.Type, gesture);
                break;

            case LibinputSwitchEvent switchEvent:
                SwitchToggled?.Invoke(device, switchEvent);
                break;

            case LibinputTabletToolEvent tool:
                switch (ev.Type)
                {
                    case LibinputEventType.TabletToolProximity:
                        TabletToolProximity?.Invoke(device, tool);
                        break;
                    case LibinputEventType.TabletToolAxis:
                        TabletToolAxis?.Invoke(device, tool);
                        break;
                    case LibinputEventType.TabletToolTip:
                        TabletToolTip?.Invoke(device, tool);
                        break;
                    case LibinputEventType.TabletToolButton:
                        TabletToolButton?.Invoke(device, tool);
                        break;
                }

                break;

            case LibinputTabletPadEvent pad:
                TabletPad?.Invoke(device, ev.Type, pad);
                break;
        }
    }

    private void OnDeviceNotify(LibinputEvent ev)
    {
        var native = ev.GetDevice();
        if (ev.Type == LibinputEventType.DeviceAdded)
        {
            var device = new InputDevice(native);
            _devices[native.NativeHandle] = device;
            DeviceAdded?.Invoke(device);
        }
        else
        {
            if (_devices.Remove(native.NativeHandle, out var device))
            {
                DeviceRemoved?.Invoke(device);
                device.Native.Dispose();
            }

            native.Dispose();
        }
    }

    private void EmitScroll(InputDevice device, LibinputPointerScrollEvent scroll, LibinputPointerAxis axis, ScrollOrientation orientation, ScrollSource source)
    {
        if (!scroll.HasAxis(axis))
        {
            return;
        }

        var value = scroll.ScrollValue(axis);
        var value120 = source == ScrollSource.Wheel ? scroll.ScrollValueV120(axis) : 0;
        var direction = device.Native.Config.Scroll.NaturalScrollEnabled
            ? Wayland.WlPointer.AxisRelativeDirection.Inverted
            : Wayland.WlPointer.AxisRelativeDirection.Identical;
        PointerScroll?.Invoke(
            device,
            TimeMs(scroll.TimestampMicroseconds),
            new PointerAxis(orientation.ToAxis(), value, (int)value120, source.ToAxisSource(), direction));
    }

    private static uint TimeMs(ulong microseconds) => (uint)(microseconds / 1000);

    private sealed class SessionInterface(ISession session) : ILibinputInterface
    {
        private readonly Dictionary<int, ISessionDevice> _open = [];

        public int OpenRestricted(string path, int flags)
        {
            var device = session.OpenDevice(path);
            _open[device.FileDescriptor] = device;
            return device.FileDescriptor;
        }

        public void CloseRestricted(int fd)
        {
            if (_open.Remove(fd, out var device))
            {
                device.Dispose();
            }
        }
    }
}
