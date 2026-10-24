using System.Diagnostics;
using Basin.Diagnostics;
using Basin.Session;
using Libinput;
using Udev;

namespace Basin.Backend.Libinput;

/// <summary>
/// Reads input devices through libinput on the session's seat. Device fds come from the session, udev reports hotplug, and a session switch suspends reading.
/// Events pass through without policy.
/// </summary>
public sealed class LibinputBackend : IDisposable
{
    private static readonly BasinLogger Log = BasinLog.For("libinput");

    private readonly ICompositorEventLoop _loop;
    private readonly ISession _session;
    private readonly Dictionary<nint, InputDevice> _devices = [];
    private readonly Dictionary<string, int> _opening = [];
    private readonly HashSet<string> _deferred = [];
    private readonly HashSet<string> _known = [];

    private UdevContext? _udev;
    private UdevMonitor? _monitor;
    private IEventSource? _udevSource;
    private CompositorSynchronizationContext? _context;
    private SessionInterface? _interface;
    private LibinputContext? _libinput;
    private IEventSource? _source;
    private bool _suspended;
    private bool _disposed;
    private int _generation;
    private LibinputDeviceConfiguration? _configuration;

    /// <summary>Gets the session that opens the device fds and names the seat.</summary>
    public ISession Session => _session;

    /// <summary>Initializes a new instance of the <see cref="LibinputBackend"/> class. It opens nothing until <see cref="Start"/>.</summary>
    /// <param name="loop">The event loop that dispatches udev and libinput.</param>
    /// <param name="session">The session that opens the devices.</param>
    public LibinputBackend(ICompositorEventLoop loop, ISession session)
    {
        _loop = loop;
        _session = session;
    }

    /// <summary>Gets the devices that libinput has now. While the session is disabled, the collection is empty.</summary>
    public IReadOnlyCollection<InputDevice> Devices => _devices.Values;

    /// <summary>Gets the device configuration capability over this backend. It is created on first access.</summary>
    public LibinputDeviceConfiguration Configuration => _configuration ??= new LibinputDeviceConfiguration(this);

    /// <summary>Gets a value indicating whether one of the current devices has the touch capability.</summary>
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

    /// <summary>Occurs when libinput adds a device, at hotplug and when the session is enabled again.</summary>
    public event Action<InputDevice>? DeviceAdded;

    /// <summary>
    /// Occurs when libinput removes a device, at unplug and when the session is disabled. The device is disposed after the handlers return.
    /// </summary>
    public event Action<InputDevice>? DeviceRemoved;

    /// <summary>
    /// Occurs when a key changes state. The arguments are the device, the time in milliseconds, the Linux input event code and the pressed state.
    /// </summary>
    public event Action<InputDevice, uint, uint, bool>? Key;

    /// <summary>
    /// Occurs on relative pointer motion. The arguments are the device, the time in milliseconds, the accelerated delta and the unaccelerated delta.
    /// The deltas are in libinput's normalized 1000 DPI units.
    /// </summary>
    public event Action<InputDevice, uint, double, double, double, double>? PointerMotion;

    /// <summary>
    /// Occurs on absolute pointer motion. The arguments are the device, the time in milliseconds and X and Y from 0 to 1 across the device's area.
    /// </summary>
    public event Action<InputDevice, uint, double, double>? PointerMotionAbsolute;

    /// <summary>
    /// Occurs when a pointer button changes state. The arguments are the device, the time in milliseconds, the Linux input event code and the pressed state.
    /// </summary>
    public event Action<InputDevice, uint, uint, bool>? PointerButton;

    /// <summary>
    /// Occurs once per axis that a scroll event carries. The arguments are the device, the time in milliseconds and the axis value.
    /// Only wheel scroll carries a value120.
    /// </summary>
    public event Action<InputDevice, uint, PointerAxis>? PointerScroll;

    /// <summary>
    /// Occurs when a touch point goes down. The arguments are the device, the time in milliseconds, the seat slot and X and Y from 0 to 1 across the device's area.
    /// </summary>
    public event Action<InputDevice, uint, int, double, double>? TouchDown;

    /// <summary>Occurs when a touch point goes up. The arguments are the device, the time in milliseconds and the seat slot.</summary>
    public event Action<InputDevice, uint, int>? TouchUp;

    /// <summary>
    /// Occurs when a touch point moves. The arguments are the device, the time in milliseconds, the seat slot and X and Y from 0 to 1 across the device's area.
    /// </summary>
    public event Action<InputDevice, uint, int, double, double>? TouchMotion;

    /// <summary>Occurs when a device ends a group of touch events that belong together.</summary>
    public event Action<InputDevice>? TouchFrame;

    /// <summary>Occurs when the device cancels all touch points that are down.</summary>
    public event Action<InputDevice>? TouchCancel;

    /// <summary>
    /// Occurs for each swipe, pinch and hold gesture event. The event object is disposed after the handlers return, so a handler must not keep it.
    /// </summary>
    public event Action<InputDevice, LibinputEventType, LibinputGestureEvent>? Gesture;

    /// <summary>
    /// Occurs when a switch changes state. The event object is disposed after the handlers return, so a handler must not keep it.
    /// </summary>
    public event Action<InputDevice, LibinputSwitchEvent>? SwitchToggled;

    /// <summary>
    /// Occurs when a tablet tool comes into or goes out of proximity. The event object is disposed after the handlers return.
    /// </summary>
    public event Action<InputDevice, LibinputTabletToolEvent>? TabletToolProximity;

    /// <summary>Occurs when a tablet tool axis changes. The event object is disposed after the handlers return.</summary>
    public event Action<InputDevice, LibinputTabletToolEvent>? TabletToolAxis;

    /// <summary>Occurs when a tablet tool tip touches or leaves the surface. The event object is disposed after the handlers return.</summary>
    public event Action<InputDevice, LibinputTabletToolEvent>? TabletToolTip;

    /// <summary>Occurs when a tablet tool button changes state. The event object is disposed after the handlers return.</summary>
    public event Action<InputDevice, LibinputTabletToolEvent>? TabletToolButton;

    /// <summary>
    /// Occurs for each tablet pad button, ring, strip or key event. The event object is disposed after the handlers return.
    /// </summary>
    public event Action<InputDevice, LibinputEventType, LibinputTabletPadEvent>? TabletPad;

    /// <summary>
    /// Starts udev monitoring, adds every initialized input device on the session's seat and dispatches the first events. Call it once.
    /// </summary>
    public void Start()
    {
        _udev = new UdevContext();
        _context = new CompositorSynchronizationContext(_loop);
        _interface = new SessionInterface(_session);
        _libinput = LibinputContext.CreatePathContext(_interface);

        _monitor = _udev.CreateMonitor(UdevMonitorSource.Udev);
        _monitor.FilterMatchSubsystemDevtype("input");
        _monitor.EnableReceiving();
        _udevSource = _loop.AddFd(_monitor.Fd, FdReadiness.Readable, (_, _) => DrainUdev());

        using (var enumerator = _udev.CreateEnumerator())
        {
            enumerator.MatchSubsystem("input");
            enumerator.MatchIsInitialized();
            enumerator.ScanDevices();
            foreach (var device in enumerator.EnumerateDevices())
            {
                using (device)
                {
                    if (Accepts(device) is { } devnode)
                    {
                        AddToLibinput(devnode);
                    }
                }
            }
        }

        _source = _loop.AddFd(_libinput.Fd, FdReadiness.Readable, (_, _) => Dispatch());
        _session.Enabled += OnSessionEnabled;
        _session.Disabled += OnSessionDisabled;
        Dispatch();
    }

    /// <summary>
    /// Stops reading, releases every device and the libinput and udev contexts. It raises no <see cref="DeviceRemoved"/> event.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _session.Enabled -= OnSessionEnabled;
        _session.Disabled -= OnSessionDisabled;
        _source?.Remove();
        _udevSource?.Remove();
        _opening.Clear();
        _deferred.Clear();
        _known.Clear();
        foreach (var device in _devices.Values)
        {
            device.Native.Dispose();
        }

        _devices.Clear();
        _libinput?.Dispose();
        _interface?.Dispose();
        _monitor?.Dispose();
        _context?.Dispose();
        _udev?.Dispose();
    }

    private void OnSessionDisabled()
    {
        if (!_suspended && _libinput is not null)
        {
            _suspended = true;
            foreach (var device in _devices.Values.ToArray())
            {
                device.Native.RemoveFromPathContext();
            }

            _deferred.UnionWith(_known);
            Dispatch();
        }
    }

    private void OnSessionEnabled()
    {
        if (_suspended && _libinput is not null)
        {
            _suspended = false;
            Log.Debug($"session enabled: reopening {_deferred.Count} input devices off the loop");
            foreach (var devnode in _deferred)
            {
                BeginOpen(devnode);
            }

            _deferred.Clear();
        }
    }

    private string? Accepts(UdevDevice device)
    {
        if (device.Devnode is not { } devnode
            || !device.Sysname.StartsWith("event", StringComparison.Ordinal)
            || device.GetPropertyValue("ID_INPUT") is null)
        {
            return null;
        }

        var seat = device.GetPropertyValue("ID_SEAT") ?? "seat0";
        return string.Equals(seat, _session.SeatName, StringComparison.Ordinal) ? devnode : null;
    }

    private void DrainUdev()
    {
        while (_monitor!.TryReceiveDevice() is { } device)
        {
            using (device)
            {
                switch (device.Action)
                {
                    case "add" when Accepts(device) is { } devnode && !IsKnown(devnode):
                        if (_suspended)
                        {
                            _deferred.Add(devnode);
                        }
                        else
                        {
                            BeginOpen(devnode);
                        }

                        break;
                    case "remove" when device.Devnode is { } devnode:
                        OnUdevRemove(devnode);
                        break;
                }
            }
        }
    }

    private bool IsKnown(string devnode) => _opening.ContainsKey(devnode) || FindDevice(devnode) is not null;

    private InputDevice? FindDevice(string devnode)
    {
        var sysname = System.IO.Path.GetFileName(devnode);
        foreach (var device in _devices.Values)
        {
            if (string.Equals(device.Native.Sysname, sysname, StringComparison.Ordinal))
            {
                return device;
            }
        }

        return null;
    }

    private void OnUdevRemove(string devnode)
    {
        _opening.Remove(devnode);
        _deferred.Remove(devnode);
        _known.Remove(devnode);
        if (FindDevice(devnode) is { } device)
        {
            device.Native.RemoveFromPathContext();
            Dispatch();
        }
    }

    private void BeginOpen(string devnode)
    {
        var generation = ++_generation;
        _opening[devnode] = generation;
        var started = Stopwatch.GetTimestamp();
        _session.OpenDeviceAsync(devnode).ContinueWith(
            task => _context?.Post(_ => CompleteOpen(devnode, generation, task, started), null),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void CompleteOpen(string devnode, int generation, Task<ISessionDevice> task, long started)
    {
        var opened = task.IsCompletedSuccessfully ? task.Result : null;
        if (_disposed || !_opening.TryGetValue(devnode, out var current) || current != generation)
        {
            opened?.Dispose();
            return;
        }

        _opening.Remove(devnode);
        if (opened is null)
        {
            _known.Remove(devnode);
            Log.Debug($"{devnode}: open failed: {task.Exception?.InnerException?.Message}");
            return;
        }

        Log.Debug($"{devnode}: opened off the loop in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.0} ms");
        if (_suspended)
        {
            opened.Dispose();
            _deferred.Add(devnode);
            return;
        }

        _interface!.Preopen(devnode, opened);
        AddToLibinput(devnode);
        _interface.DropPreopened(devnode);
        Dispatch();
    }

    private void AddToLibinput(string devnode)
    {
        try
        {
            _libinput!.AddDevice(devnode).Dispose();
            _known.Add(devnode);
        }
        catch (LibinputException e)
        {
            _known.Remove(devnode);
            Log.Debug($"{devnode}: {e.Message}");
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

    private sealed class SessionInterface(ISession session) : ILibinputInterface, IDisposable
    {
        private readonly Dictionary<int, ISessionDevice> _open = [];
        private readonly Dictionary<string, ISessionDevice> _preopened = [];

        public void Preopen(string path, ISessionDevice device) => _preopened[path] = device;

        public void DropPreopened(string path)
        {
            if (_preopened.Remove(path, out var device))
            {
                device.Dispose();
            }
        }

        public void Dispose()
        {
            foreach (var device in _preopened.Values)
            {
                device.Dispose();
            }

            _preopened.Clear();
        }

        public int OpenRestricted(string path, int flags)
        {
            if (!_preopened.Remove(path, out var device))
            {
                device = session.OpenDevice(path);
            }

            _open[device.FileDescriptor] = device;
            return device.FileDescriptor;
        }

        public void CloseRestricted(int fd)
        {
            if (_open.Remove(fd, out var device))
            {
                _ = session.CloseDeviceAsync(device);
            }
        }
    }
}
