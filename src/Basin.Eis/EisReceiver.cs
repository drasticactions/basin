using Basin.Capabilities;
using Basin.Diagnostics;
using Libei;
using Libei.Server;
using static Basin.Eis.EisLog;

namespace Basin.Eis;

public sealed unsafe class EisReceiver : IDisposable
{
    private const uint AxisVertical = 0;
    private const uint AxisHorizontal = 1;
    private const uint SourceWheel = 0;
    private const uint SourceFinger = 1;

    private readonly EisContext _context;
    private readonly IInputSink _sink;
    private readonly IActiveKeymap? _keymap;
    private readonly InputDeviceCapability _granted;
    private readonly IEventSource _source;
    private readonly List<(string MappingId, int X, int Y, int Width, int Height, double Scale)> _regions = [];
    private readonly HashSet<uint> _pressedButtons = [];
    private readonly HashSet<uint> _pressedKeys = [];
    private readonly HashSet<int> _touches = [];
    private EisClient? _client;
    private EisSeat? _seat;
    private EisDevice? _pointer;
    private EisDevice? _absolute;
    private EisDevice? _keyboardDevice;
    private EisDevice? _touch;
    private IInjectedKeyboard? _keyboard;
    private bool _touchPending;
    private bool _disposed;

    public EisReceiver(ICompositorEventLoop loop, IInputSink sink, IActiveKeymap? keymap, InputDeviceCapability granted)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
        _keymap = keymap;
        _granted = granted;
        _context = new EisContext();
        _context.UseFdBackend();
        _source = loop.AddFd(_context.Fd, FdReadiness.Readable, (_, _) => Pump());
        if (keymap is not null)
        {
            keymap.KeymapChanged += OnKeymapChanged;
        }

        BasinCounters.Track();
    }

    public bool HasClient => _client is not null;

    public bool IsEmulating { get; private set; }

    public int DeviceCount =>
        (_pointer is null ? 0 : 1) + (_absolute is null ? 0 : 1) + (_keyboardDevice is null ? 0 : 1) + (_touch is null ? 0 : 1);

    public int AddClientFd() => _context.AddClient();

    public void AddRegion(string mappingId, int x, int y, int width, int height, double scale)
    {
        ArgumentNullException.ThrowIfNull(mappingId);
        _regions.Add((mappingId, x, y, width, height, scale));
        if (_absolute is not null)
        {
            ClearDevice(ref _absolute);
            EnsureAbsolute();
        }

        if (_touch is not null)
        {
            ClearDevice(ref _touch);
            EnsureTouch();
        }
    }

    public void Pump()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _context.Dispatch();
            while (_context.TryGetEvent(out var @event))
            {
                using (@event)
                {
                    Handle(@event);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn($"eis dispatch failed: {e.Message}");
        }
    }

    public void ReleaseAll()
    {
        var time = Now;
        foreach (var button in _pressedButtons)
        {
            _sink.PointerButton(time, button, false);
        }

        if (_pressedButtons.Count > 0)
        {
            _sink.Frame();
        }

        _pressedButtons.Clear();
        foreach (var key in _pressedKeys)
        {
            _sink.Key(_keyboard, time, key, false);
        }

        _pressedKeys.Clear();
        if (_touches.Count > 0)
        {
            _sink.TouchCancel();
            _touches.Clear();
        }

        _touchPending = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseAll();
        if (_keymap is not null)
        {
            _keymap.KeymapChanged -= OnKeymapChanged;
        }

        ClearDevices();
        if (_seat is { } seat)
        {
            seat.Remove();
            seat.Dispose();
            _seat = null;
        }

        if (_client is { } client)
        {
            client.Disconnect();
            client.Dispose();
            _client = null;
        }

        _keyboard?.Dispose();
        _keyboard = null;
        _source.Remove();
        _context.Dispose();
        BasinCounters.Untrack();
    }

    private static uint Now => (uint)(MonotonicClock.Nanos / 1_000_000);

    private void Handle(EisEvent @event)
    {
        switch (@event.Type)
        {
            case EisEventType.ClientConnect:
                OnClientConnect(@event);
                break;

            case EisEventType.ClientDisconnect:
                OnClientDisconnect(@event);
                break;

            case EisEventType.SeatBind:
                OnSeatBind((EisSeatBindEvent)@event);
                break;

            case EisEventType.DeviceClosed:
                OnDeviceClosed(@event);
                break;

            case EisEventType.DeviceStartEmulating:
                if (!IsEmulating)
                {
                    IsEmulating = true;
                    _keyboard ??= _sink.CreateKeyboard();
                }

                break;

            case EisEventType.DeviceStopEmulating:
                IsEmulating = false;
                ReleaseAll();
                break;

            case EisEventType.Frame:
                _sink.Frame();
                if (_touchPending)
                {
                    _touchPending = false;
                    _sink.TouchFrame();
                }

                break;

            case EisEventType.PointerMotion:
                var motion = (EisPointerMotionEvent)@event;
                _sink.PointerMotion(Now, motion.Dx, motion.Dy);
                break;

            case EisEventType.PointerMotionAbsolute:
                var absolute = (EisPointerMotionAbsoluteEvent)@event;
                var (width, height) = Bounds();
                _sink.PointerMotionAbsolute(Now, absolute.X, absolute.Y, width, height);
                break;

            case EisEventType.ButtonButton:
                var button = (EisButtonEvent)@event;
                if (button.IsPress)
                {
                    _pressedButtons.Add(button.Button);
                }
                else
                {
                    _pressedButtons.Remove(button.Button);
                }

                _sink.PointerButton(Now, button.Button, button.IsPress);
                break;

            case EisEventType.ScrollDelta:
                var scroll = (EisScrollEvent)@event;
                _sink.PointerAxisSource(SourceFinger);
                if (scroll.Dy != 0)
                {
                    _sink.PointerAxis(Now, AxisVertical, scroll.Dy);
                }

                if (scroll.Dx != 0)
                {
                    _sink.PointerAxis(Now, AxisHorizontal, scroll.Dx);
                }

                break;

            case EisEventType.ScrollDiscrete:
                var discrete = (EisScrollDiscreteEvent)@event;
                _sink.PointerAxisSource(SourceWheel);
                if (discrete.Dy != 0)
                {
                    _sink.PointerAxis(Now, AxisVertical, discrete.Dy / 120.0 * 15);
                }

                if (discrete.Dx != 0)
                {
                    _sink.PointerAxis(Now, AxisHorizontal, discrete.Dx / 120.0 * 15);
                }

                break;

            case EisEventType.ScrollStop:
            case EisEventType.ScrollCancel:
                var stop = (EisScrollStopEvent)@event;
                if (stop.StopY)
                {
                    _sink.PointerAxisStop(Now, AxisVertical);
                }

                if (stop.StopX)
                {
                    _sink.PointerAxisStop(Now, AxisHorizontal);
                }

                break;

            case EisEventType.KeyboardKey:
                var key = (EisKeyboardKeyEvent)@event;
                if (key.IsPress)
                {
                    _pressedKeys.Add(key.Key);
                }
                else
                {
                    _pressedKeys.Remove(key.Key);
                }

                _sink.Key(_keyboard, Now, key.Key, key.IsPress);
                break;

            case EisEventType.TouchDown:
                var down = (EisTouchEvent)@event;
                var (downWidth, downHeight) = Bounds();
                _touches.Add((int)down.TouchId);
                _touchPending |= _sink.TouchDown(Now, (int)down.TouchId, down.X, down.Y, downWidth, downHeight);
                break;

            case EisEventType.TouchMotion:
                var touchMotion = (EisTouchEvent)@event;
                var (motionWidth, motionHeight) = Bounds();
                _touchPending |= _sink.TouchMotion(Now, (int)touchMotion.TouchId, touchMotion.X, touchMotion.Y, motionWidth, motionHeight);
                break;

            case EisEventType.TouchUp:
                var up = (EisTouchEvent)@event;
                _touches.Remove((int)up.TouchId);
                _touchPending |= _sink.TouchUp(Now, (int)up.TouchId);
                break;
        }
    }

    private (double Width, double Height) Bounds()
    {
        var right = 0;
        var bottom = 0;
        foreach (var (_, x, y, width, height, _) in _regions)
        {
            right = Math.Max(right, x + width);
            bottom = Math.Max(bottom, y + height);
        }

        return right > 0 && bottom > 0 ? (right, bottom) : (1, 1);
    }

    private void OnClientConnect(EisEvent @event)
    {
        var client = @event.GetClient();
        if (client is null)
        {
            return;
        }

        if (!client.IsSender)
        {
            Log.Warn($"eis receiver client {client.Name ?? "<unnamed>"} refused: a remote desktop only sends");
            client.Disconnect();
            client.Dispose();
            return;
        }

        if (_client is not null)
        {
            Log.Warn($"second eis client {client.Name ?? "<unnamed>"} refused");
            client.Disconnect();
            client.Dispose();
            return;
        }

        client.Connect();
        _client = client;
        var seat = client.CreateSeat("default");
        seat.ConfigureCapabilities(SeatCapabilities());
        seat.Add();
        _seat = seat;
        Log.Info($"eis client {client.Name ?? "<unnamed>"} connected");
    }

    private EiDeviceCapability SeatCapabilities()
    {
        var capabilities = EiDeviceCapability.None;
        if ((_granted & InputDeviceCapability.Pointer) != 0)
        {
            capabilities |= EiDeviceCapability.Pointer | EiDeviceCapability.PointerAbsolute |
                EiDeviceCapability.Button | EiDeviceCapability.Scroll;
        }

        if ((_granted & InputDeviceCapability.Keyboard) != 0)
        {
            capabilities |= EiDeviceCapability.Keyboard;
        }

        if ((_granted & InputDeviceCapability.Touch) != 0)
        {
            capabilities |= EiDeviceCapability.Touch;
        }

        return capabilities;
    }

    private void OnClientDisconnect(EisEvent @event)
    {
        using var client = @event.GetClient();
        if (client is null || _client is not { } ours || client.NativeHandle != ours.NativeHandle)
        {
            return;
        }

        IsEmulating = false;
        ReleaseAll();
        ClearDevices();
        _seat?.Dispose();
        _seat = null;
        ours.Disconnect();
        ours.Dispose();
        _client = null;
        Log.Info($"eis client disconnected");
    }

    private void OnSeatBind(EisSeatBindEvent bind)
    {
        var pointer = (_granted & InputDeviceCapability.Pointer) != 0;
        if (pointer && bind.HasCapability(EiDeviceCapability.Pointer))
        {
            EnsurePointer();
        }
        else
        {
            ClearDevice(ref _pointer);
        }

        if (pointer && bind.HasCapability(EiDeviceCapability.PointerAbsolute))
        {
            EnsureAbsolute();
        }
        else
        {
            ClearDevice(ref _absolute);
        }

        if ((_granted & InputDeviceCapability.Keyboard) != 0 && bind.HasCapability(EiDeviceCapability.Keyboard))
        {
            EnsureKeyboard();
        }
        else
        {
            ClearDevice(ref _keyboardDevice);
        }

        if ((_granted & InputDeviceCapability.Touch) != 0 && bind.HasCapability(EiDeviceCapability.Touch))
        {
            EnsureTouch();
        }
        else
        {
            ClearDevice(ref _touch);
        }
    }

    private void OnDeviceClosed(EisEvent @event)
    {
        using var device = @event.GetDevice();
        if (device is null)
        {
            return;
        }

        if (_pointer is { } pointer && pointer.NativeHandle == device.NativeHandle)
        {
            ClearDevice(ref _pointer);
        }
        else if (_absolute is { } absolute && absolute.NativeHandle == device.NativeHandle)
        {
            ClearDevice(ref _absolute);
        }
        else if (_keyboardDevice is { } keyboard && keyboard.NativeHandle == device.NativeHandle)
        {
            ClearDevice(ref _keyboardDevice);
        }
        else if (_touch is { } touch && touch.NativeHandle == device.NativeHandle)
        {
            ClearDevice(ref _touch);
        }
    }

    private void EnsurePointer()
    {
        if (_pointer is not null || _seat is not { } seat)
        {
            return;
        }

        var pointer = seat.CreateDevice();
        pointer.ConfigureName("remote pointer");
        pointer.ConfigureType(EiDeviceType.Virtual);
        pointer.ConfigureCapabilities(EiDeviceCapability.Pointer | EiDeviceCapability.Button | EiDeviceCapability.Scroll);
        pointer.Add();
        pointer.Resume();
        _pointer = pointer;
    }

    private void EnsureAbsolute()
    {
        if (_absolute is not null || _seat is not { } seat)
        {
            return;
        }

        var absolute = seat.CreateDevice();
        absolute.ConfigureName("remote absolute pointer");
        absolute.ConfigureType(EiDeviceType.Virtual);
        absolute.ConfigureCapabilities(EiDeviceCapability.PointerAbsolute | EiDeviceCapability.Button | EiDeviceCapability.Scroll);
        AddRegions(absolute);
        absolute.Add();
        absolute.Resume();
        _absolute = absolute;
    }

    private void EnsureTouch()
    {
        if (_touch is not null || _seat is not { } seat)
        {
            return;
        }

        var touch = seat.CreateDevice();
        touch.ConfigureName("remote touchscreen");
        touch.ConfigureType(EiDeviceType.Virtual);
        touch.ConfigureCapabilities(EiDeviceCapability.Touch);
        AddRegions(touch);
        touch.Add();
        touch.Resume();
        _touch = touch;
    }

    private void AddRegions(EisDevice device)
    {
        foreach (var (mappingId, x, y, width, height, scale) in _regions)
        {
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            using var region = device.CreateRegion();
            region.SetOffset(unchecked((uint)x), unchecked((uint)y));
            region.SetSize((uint)width, (uint)height);
            region.SetPhysicalScale(scale);
            region.MappingId = mappingId;
            region.Add();
        }
    }

    private void EnsureKeyboard()
    {
        if (_keyboardDevice is not null || _seat is not { } seat)
        {
            return;
        }

        var keyboard = seat.CreateDevice();
        keyboard.ConfigureName("remote keyboard");
        keyboard.ConfigureType(EiDeviceType.Virtual);
        keyboard.ConfigureCapabilities(EiDeviceCapability.Keyboard);
        if (_keymap?.KeymapBuffer is { } buffer)
        {
            try
            {
                using var keymap = keyboard.CreateKeymap(EiKeymapType.Xkb, buffer.Fd, buffer.Size);
                keymap.Add();
            }
            catch (LibeiException e)
            {
                Log.Warn($"eis keymap rejected: {e.Message}");
            }
        }

        keyboard.Add();
        keyboard.Resume();
        _keyboardDevice = keyboard;
    }

    private void OnKeymapChanged()
    {
        if (_keyboardDevice is null)
        {
            return;
        }

        ClearDevice(ref _keyboardDevice);
        EnsureKeyboard();
    }

    private void ClearDevices()
    {
        ClearDevice(ref _pointer);
        ClearDevice(ref _absolute);
        ClearDevice(ref _keyboardDevice);
        ClearDevice(ref _touch);
    }

    private static void ClearDevice(ref EisDevice? slot)
    {
        if (slot is not { } device)
        {
            return;
        }

        slot = null;
        device.Remove();
        device.Dispose();
    }
}
