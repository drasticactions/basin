using Basin.Capabilities;
using Libinput;

namespace Basin.Backend.Libinput;

public sealed class LibinputTabletSource : ITabletSource, IDisposable
{
    private readonly LibinputBackend _backend;
    private readonly Dictionary<InputDevice, ulong> _tabletIds = [];
    private readonly Dictionary<InputDevice, ulong> _padIds = [];
    private readonly Dictionary<nint, ulong> _toolIds = [];
    private readonly Dictionary<ulong, TabletInfo> _tablets = [];
    private readonly Dictionary<ulong, TabletToolInfo> _tools = [];
    private readonly Dictionary<ulong, TabletPadInfo> _pads = [];
    private ulong _nextId = 1;

    public LibinputTabletSource(LibinputBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        foreach (var device in backend.Devices)
        {
            Track(device);
        }

        backend.DeviceAdded += Track;
        backend.DeviceRemoved += Forget;
        backend.TabletToolProximity += OnProximity;
        backend.TabletToolAxis += OnAxis;
        backend.TabletToolTip += OnTip;
        backend.TabletToolButton += OnButton;
        backend.TabletPad += OnPad;
    }

    public event Action<TabletInfo>? TabletAdded;

    public event Action<ulong>? TabletRemoved;

    public event Action<TabletToolInfo>? ToolAdded;

    public event Action<ulong>? ToolRemoved;

    public event Action<TabletPadInfo>? PadAdded;

    public event Action<ulong>? PadRemoved;

    private readonly TabletObservers _tabletObservers = new();

    public void AddObserver(ITabletObserver observer) => _tabletObservers.Add(observer);

    public void RemoveObserver(ITabletObserver observer) => _tabletObservers.Remove(observer);

    public void Dispose()
    {
        _backend.DeviceAdded -= Track;
        _backend.DeviceRemoved -= Forget;
        _backend.TabletToolProximity -= OnProximity;
        _backend.TabletToolAxis -= OnAxis;
        _backend.TabletToolTip -= OnTip;
        _backend.TabletToolButton -= OnButton;
        _backend.TabletPad -= OnPad;
    }

    public int EnumerateTablets(Span<TabletInfo> tablets) => Copy(_tablets.Values, _tablets.Count, tablets);

    public int EnumerateTools(Span<TabletToolInfo> tools) => Copy(_tools.Values, _tools.Count, tools);

    public int EnumeratePads(Span<TabletPadInfo> pads) => Copy(_pads.Values, _pads.Count, pads);

    private void Track(InputDevice device)
    {
        if (device.HasTabletTool)
        {
            var id = _nextId++;
            _tabletIds[device] = id;
            var native = device.Native;
            var info = new TabletInfo(
                id,
                device.Name,
                native.IdVendor,
                native.IdProduct,
                native.Sysname,
                native.IdBustype);
            _tablets[id] = info;
            TabletAdded?.Invoke(info);
        }

        if (device.HasTabletPad)
        {
            var id = _nextId++;
            _padIds[device] = id;
            var info = new TabletPadInfo(
                id,
                device.Native.Sysname,
                (uint)device.Native.TabletPadButtonCount,
                (uint)Math.Max(0, device.Native.TabletPadDialCount));
            _pads[id] = info;
            PadAdded?.Invoke(info);
        }
    }

    private void Forget(InputDevice device)
    {
        if (_tabletIds.Remove(device, out var tabletId))
        {
            _tablets.Remove(tabletId);
            TabletRemoved?.Invoke(tabletId);
        }

        if (_padIds.Remove(device, out var padId))
        {
            _pads.Remove(padId);
            PadRemoved?.Invoke(padId);
        }
    }

    private ulong ToolIdFor(LibinputTabletToolEvent ev)
    {
        using var tool = ev.GetTool();
        if (_toolIds.TryGetValue(tool.NativeHandle, out var existing))
        {
            return existing;
        }

        var id = _nextId++;
        _toolIds[tool.NativeHandle] = id;
        var axes = TabletToolAxis.None;
        if (tool.HasPressure)
        {
            axes |= TabletToolAxis.Pressure;
        }

        if (tool.HasDistance)
        {
            axes |= TabletToolAxis.Distance;
        }

        if (tool.HasTilt)
        {
            axes |= TabletToolAxis.Tilt;
        }

        if (tool.HasRotation)
        {
            axes |= TabletToolAxis.Rotation;
        }

        if (tool.HasSlider)
        {
            axes |= TabletToolAxis.Slider;
        }

        if (tool.HasWheel)
        {
            axes |= TabletToolAxis.Wheel;
        }

        var info = new TabletToolInfo(id, MapType(tool.Type), tool.Serial, axes);
        _tools[id] = info;
        ToolAdded?.Invoke(info);
        return id;
    }

    private void OnProximity(InputDevice device, LibinputTabletToolEvent ev)
    {
        if (!_tabletIds.TryGetValue(device, out var tabletId))
        {
            return;
        }

        var toolId = ToolIdFor(ev);
        var inProximity = ev.ProximityState == LibinputTabletToolProximityState.In;

        if (inProximity)
        {
            _tabletObservers.ToolAxis(toolId, TimeMs(ev.TimestampMicroseconds), AxesOf(ev));
        }

        _tabletObservers.ToolProximity(toolId, tabletId, inProximity);

        if (!inProximity)
        {
            _toolIds.Remove(ToolHandle(ev));
            _tools.Remove(toolId);
            ToolRemoved?.Invoke(toolId);
        }
    }

    private void OnAxis(InputDevice device, LibinputTabletToolEvent ev)
    {
        if (_tabletIds.ContainsKey(device))
        {
            _tabletObservers.ToolAxis(ToolIdFor(ev), TimeMs(ev.TimestampMicroseconds), AxesOf(ev));
        }
    }

    private void OnTip(InputDevice device, LibinputTabletToolEvent ev)
    {
        if (!_tabletIds.ContainsKey(device))
        {
            return;
        }

        var toolId = ToolIdFor(ev);
        var time = TimeMs(ev.TimestampMicroseconds);

        _tabletObservers.ToolAxis(toolId, time, AxesOf(ev));
        _tabletObservers.ToolButton(toolId, time, BtnTouch, ev.TipState == LibinputTabletToolTipState.Down);
    }

    private void OnButton(InputDevice device, LibinputTabletToolEvent ev)
    {
        if (_tabletIds.ContainsKey(device))
        {
            _tabletObservers.ToolButton(
                ToolIdFor(ev),
                TimeMs(ev.TimestampMicroseconds),
                ev.Button,
                ev.ButtonState == LibinputButtonState.Pressed);
        }
    }

    private void OnPad(InputDevice device, LibinputEventType type, LibinputTabletPadEvent ev)
    {
        if (!_padIds.TryGetValue(device, out var padId))
        {
            return;
        }

        var time = TimeMs(ev.TimestampMicroseconds);
        TabletPadEvent? padEvent = type switch
        {
            LibinputEventType.TabletPadButton => new TabletPadEvent(
                TabletPadEventKind.Button, ev.Mode, ev.ButtonNumber, 0, ev.ButtonState == LibinputButtonState.Pressed),
            LibinputEventType.TabletPadRing => new TabletPadEvent(
                TabletPadEventKind.Ring, ev.Mode, ev.RingNumber, ev.RingPosition, false),
            LibinputEventType.TabletPadStrip => new TabletPadEvent(
                TabletPadEventKind.Strip, ev.Mode, ev.StripNumber, ev.StripPosition, false),
            LibinputEventType.TabletPadDial => new TabletPadEvent(
                TabletPadEventKind.Dial, ev.Mode, ev.DialNumber, ev.DialDeltaV120, false),
            _ => null,
        };

        if (padEvent is { } value)
        {
            _tabletObservers.PadEvent(padId, time, value);
        }
    }

    private static TabletToolAxes AxesOf(LibinputTabletToolEvent ev) => new(
        ev.TransformedX(1),
        ev.TransformedY(1),
        ev.Pressure,
        ev.Distance,
        ev.TiltX,
        ev.TiltY,
        ev.Rotation,
        ev.SliderPosition,
        ev.WheelDelta);

    private static nint ToolHandle(LibinputTabletToolEvent ev)
    {
        using var tool = ev.GetTool();
        return tool.NativeHandle;
    }

    private static TabletToolType MapType(LibinputTabletToolType type) => type switch
    {
        LibinputTabletToolType.Eraser => TabletToolType.Eraser,
        LibinputTabletToolType.Brush => TabletToolType.Brush,
        LibinputTabletToolType.Pencil => TabletToolType.Pencil,
        LibinputTabletToolType.Airbrush => TabletToolType.Airbrush,
        LibinputTabletToolType.Mouse => TabletToolType.Mouse,
        LibinputTabletToolType.Lens => TabletToolType.Lens,

        _ => TabletToolType.Pen,
    };

    private static uint TimeMs(ulong microseconds) => (uint)(microseconds / 1000);

    private const uint BtnTouch = 0x14a;

    private static int Copy<T>(IEnumerable<T> source, int count, Span<T> target)
    {
        if (target.Length < count)
        {
            return -1;
        }

        var written = 0;
        foreach (var item in source)
        {
            target[written++] = item;
        }

        return written;
    }
}
