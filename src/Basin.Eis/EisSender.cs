using Basin.Capabilities;
using Basin.Diagnostics;
using Libei;
using Libei.Server;
using static Basin.Eis.EisLog;

namespace Basin.Eis;

/// <summary>
/// Serves one libeis receiver client and sends captured pointer and keyboard input to it. It refuses sender clients and a second client.
/// </summary>
public sealed unsafe class EisSender : IDisposable
{
    private readonly EisContext _context;
    private readonly OutputLayout _layout;
    private readonly IActiveKeymap? _keymap;
    private readonly IEventSource _source;
    private readonly List<(string MappingId, int X, int Y, int Width, int Height, double Scale)> _extraRegions = [];
    private EisClient? _client;
    private EisSeat? _seat;
    private EisDevice? _pointer;
    private EisDevice? _keyboardDevice;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="EisSender"/> class. It creates an EIS context and watches its fd on <paramref name="loop"/>.
    /// </summary>
    /// <param name="loop">The compositor loop that dispatches the context.</param>
    /// <param name="layout">The output layout that gives the pointer device's regions.</param>
    /// <param name="keymap">The keymap that the keyboard device sends, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="loop"/> or <paramref name="layout"/> is <see langword="null"/>.</exception>
    public EisSender(ICompositorEventLoop loop, OutputLayout layout, IActiveKeymap? keymap)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        _keymap = keymap;
        _context = new EisContext();
        _context.UseFdBackend();
        _source = loop.AddFd(_context.Fd, FdReadiness.Readable, (_, _) => Pump());
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets a value indicating whether a receiver client is connected.
    /// </summary>
    public bool HasClient => _client is not null;

    /// <summary>
    /// Gets a value indicating whether the client has bound a pointer device.
    /// </summary>
    public bool HasPointer => _pointer is not null;

    /// <summary>
    /// Gets a value indicating whether the client has bound a keyboard device.
    /// </summary>
    public bool HasKeyboard => _keyboardDevice is not null;

    /// <summary>
    /// Creates a socket pair in the EIS context and returns the client end.
    /// </summary>
    /// <returns>The client's fd. The caller owns it and gives it to the client.</returns>
    public int AddClientFd() => _context.AddClient();

    /// <summary>
    /// Adds a region to the pointer device beside the output regions, and recreates the device when it exists.
    /// </summary>
    /// <param name="mappingId">The region's mapping id.</param>
    /// <param name="x">The layout x of the region.</param>
    /// <param name="y">The layout y of the region.</param>
    /// <param name="width">The region width.</param>
    /// <param name="height">The region height.</param>
    /// <param name="scale">The physical scale of the region.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mappingId"/> is <see langword="null"/>.</exception>
    public void AddAbsoluteRegion(string mappingId, int x, int y, int width, int height, double scale)
    {
        ArgumentNullException.ThrowIfNull(mappingId);
        _extraRegions.Add((mappingId, x, y, width, height, scale));
        ResetPointer();
    }

    /// <summary>
    /// Removes the regions that <see cref="AddAbsoluteRegion"/> added, and recreates the pointer device when one was removed.
    /// </summary>
    public void ClearAbsoluteRegions()
    {
        if (_extraRegions.Count == 0)
        {
            return;
        }

        _extraRegions.Clear();
        ResetPointer();
    }

    /// <summary>
    /// Dispatches the EIS context and handles its events. An exception is logged, not raised. The loop calls it when the fd is readable.
    /// </summary>
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
            Log.Warn(e, $"dispatch failed");
        }
    }

    /// <summary>
    /// Starts emulation on the pointer and keyboard devices that exist.
    /// </summary>
    /// <param name="sequence">The emulation sequence number, which is the activation id.</param>
    public void StartEmulating(uint sequence)
    {
        _pointer?.StartEmulating(sequence);
        _keyboardDevice?.StartEmulating(sequence);
    }

    /// <summary>
    /// Stops emulation on the pointer and keyboard devices that exist.
    /// </summary>
    public void StopEmulating()
    {
        _pointer?.StopEmulating();
        _keyboardDevice?.StopEmulating();
    }

    /// <summary>
    /// Sends a relative motion and a frame. It does nothing without a pointer device.
    /// </summary>
    /// <param name="dx">The relative x motion.</param>
    /// <param name="dy">The relative y motion.</param>
    public void SendMotion(double dx, double dy)
    {
        if (_pointer is not { } pointer)
        {
            return;
        }

        pointer.PointerMotion(dx, dy);
        pointer.Frame(_context.Now);
    }

    /// <summary>
    /// Sends a button change and a frame. It does nothing without a pointer device.
    /// </summary>
    /// <param name="button">The evdev button code.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    public void SendButton(uint button, bool pressed)
    {
        if (_pointer is not { } pointer)
        {
            return;
        }

        pointer.Button(button, pressed);
        pointer.Frame(_context.Now);
    }

    /// <summary>
    /// Sends a scroll stop, a discrete scroll or a smooth scroll, then a frame. It does nothing without a pointer device.
    /// </summary>
    /// <param name="axis">The scroll axis. A nonzero value120 sends a discrete scroll.</param>
    public void SendScroll(in PointerAxis axis)
    {
        if (_pointer is not { } pointer)
        {
            return;
        }

        var horizontal = axis.Axis == Wayland.WlPointer.Axis.HorizontalScroll;
        if (axis.IsStop)
        {
            pointer.ScrollStop(horizontal, !horizontal);
        }
        else if (axis.Value120 != 0)
        {
            pointer.ScrollDiscrete(horizontal ? axis.Value120 : 0, horizontal ? 0 : axis.Value120);
        }
        else
        {
            pointer.ScrollDelta(horizontal ? axis.Value : 0, horizontal ? 0 : axis.Value);
        }

        pointer.Frame(_context.Now);
    }

    /// <summary>
    /// Sends a key change and a frame. It does nothing without a keyboard device.
    /// </summary>
    /// <param name="key">The evdev key code.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    public void SendKey(uint key, bool pressed)
    {
        if (_keyboardDevice is not { } keyboard)
        {
            return;
        }

        keyboard.KeyboardKey(key, pressed);
        keyboard.Frame(_context.Now);
    }

    /// <summary>
    /// Sends the xkb modifier state and a frame. It does nothing without a keyboard device.
    /// </summary>
    /// <param name="depressed">The depressed modifier mask.</param>
    /// <param name="latched">The latched modifier mask.</param>
    /// <param name="locked">The locked modifier mask.</param>
    /// <param name="group">The effective layout group.</param>
    public void SendModifiers(uint depressed, uint latched, uint locked, uint group)
    {
        if (_keyboardDevice is not { } keyboard)
        {
            return;
        }

        keyboard.SendXkbModifiers(depressed, latched, locked, group);
        keyboard.Frame(_context.Now);
    }

    /// <summary>
    /// Removes and recreates the keyboard device so that it carries the current keymap. It does nothing without a keyboard device.
    /// </summary>
    public void ResetKeyboard()
    {
        if (_keyboardDevice is null)
        {
            return;
        }

        ClearKeyboard();
        EnsureKeyboard();
    }

    /// <summary>
    /// Removes and recreates the pointer device so that it carries the current regions. It does nothing without a pointer device.
    /// </summary>
    public void ResetPointer()
    {
        if (_pointer is null)
        {
            return;
        }

        ClearPointer();
        EnsurePointer();
    }

    /// <summary>
    /// Removes the devices and the seat, disconnects the client and closes the context.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ClearPointer();
        ClearKeyboard();
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

        _source.Remove();
        _context.Dispose();
        BasinCounters.Untrack();
    }

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
        }
    }

    private void OnClientConnect(EisEvent @event)
    {
        var client = @event.GetClient();
        if (client is null)
        {
            return;
        }

        if (client.IsSender)
        {
            Log.Warn($"sender client {client.Name ?? "<unnamed>"} refused, a capture session only receives");
            client.Disconnect();
            client.Dispose();
            return;
        }

        if (_client is not null)
        {
            Log.Warn($"second client {client.Name ?? "<unnamed>"} refused");
            client.Disconnect();
            client.Dispose();
            return;
        }

        client.Connect();
        _client = client;
        var seat = client.CreateSeat("default");
        seat.ConfigureCapabilities(
            EiDeviceCapability.Pointer | EiDeviceCapability.Button | EiDeviceCapability.Scroll |
            EiDeviceCapability.Keyboard);
        seat.Add();
        _seat = seat;
        Log.Info($"client {client.Name ?? "<unnamed>"} connected");
    }

    private void OnClientDisconnect(EisEvent @event)
    {
        using var client = @event.GetClient();
        if (client is null || _client is not { } ours || client.NativeHandle != ours.NativeHandle)
        {
            return;
        }

        ClearPointer();
        ClearKeyboard();
        _seat?.Dispose();
        _seat = null;
        ours.Disconnect();
        ours.Dispose();
        _client = null;
        Log.Info($"client disconnected");
    }

    private void OnSeatBind(EisSeatBindEvent bind)
    {
        if (bind.HasCapability(EiDeviceCapability.Pointer) &&
            bind.HasCapability(EiDeviceCapability.Button) &&
            bind.HasCapability(EiDeviceCapability.Scroll))
        {
            EnsurePointer();
        }
        else
        {
            ClearPointer();
        }

        if (bind.HasCapability(EiDeviceCapability.Keyboard))
        {
            EnsureKeyboard();
        }
        else
        {
            ClearKeyboard();
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
            ClearPointer();
        }
        else if (_keyboardDevice is { } keyboard && keyboard.NativeHandle == device.NativeHandle)
        {
            ClearKeyboard();
        }
    }

    private void EnsurePointer()
    {
        if (_pointer is not null || _seat is not { } seat)
        {
            return;
        }

        var pointer = seat.CreateDevice();
        pointer.ConfigureName("captured relative pointer");
        pointer.ConfigureCapabilities(EiDeviceCapability.Pointer | EiDeviceCapability.Button | EiDeviceCapability.Scroll);
        foreach (var (output, _) in _layout.Outputs)
        {
            var box = _layout.BoxOf(output);
            AddRegion(pointer, output.Name, box.X, box.Y, box.Width, box.Height, output.Scale);
        }

        foreach (var (mappingId, x, y, width, height, scale) in _extraRegions)
        {
            AddRegion(pointer, mappingId, x, y, width, height, scale);
        }

        pointer.Add();
        pointer.Resume();
        _pointer = pointer;
    }

    private static void AddRegion(EisDevice device, string mappingId, int x, int y, int width, int height, double scale)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        using var region = device.CreateRegion();
        region.SetOffset(unchecked((uint)x), unchecked((uint)y));
        region.SetSize((uint)width, (uint)height);
        region.SetPhysicalScale(scale);
        region.MappingId = mappingId;
        region.Add();
    }

    private void EnsureKeyboard()
    {
        if (_keyboardDevice is not null || _seat is not { } seat)
        {
            return;
        }

        var keyboard = seat.CreateDevice();
        keyboard.ConfigureName("captured keyboard");
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
                Log.Warn(e, $"keymap refused");
            }
        }

        keyboard.Add();
        keyboard.Resume();
        _keyboardDevice = keyboard;
    }

    private void ClearPointer()
    {
        if (_pointer is not { } pointer)
        {
            return;
        }

        _pointer = null;
        pointer.Remove();
        pointer.Dispose();
    }

    private void ClearKeyboard()
    {
        if (_keyboardDevice is not { } keyboard)
        {
            return;
        }

        _keyboardDevice = null;
        keyboard.Remove();
        keyboard.Dispose();
    }
}
