using System.Runtime.InteropServices;
using SDL3;

namespace Basin.Sdl;

public sealed unsafe class SdlWaylandMoves : IDisposable
{
    private const string Client = "libwayland-client.so.0";
    private const uint BtnLeft = 0x110;
    private const uint XdgToplevelMove = 5;
    private const uint XdgToplevelResize = 6;

    private readonly nint _display;
    private readonly nint _queue;
    private readonly nint _wrapper;
    private readonly nint _registry;
    private readonly nint _seatInterface;
    private readonly nint _pointerInterface;
    private readonly nint* _registryListener;
    private readonly nint* _seatListener;
    private readonly nint* _pointerListener;
    private GCHandle _self;
    private nint _seat;
    private nint _pointer;
    private uint _pressSerial;
    private bool _pressed;
    private bool _disposed;

    private SdlWaylandMoves(nint display, nint registryInterface, nint seatInterface, nint pointerInterface)
    {
        _display = display;
        _seatInterface = seatInterface;
        _pointerInterface = pointerInterface;
        _self = GCHandle.Alloc(this);
        _registryListener = (nint*)NativeMemory.Alloc(2, (nuint)sizeof(nint));
        _registryListener[0] = (nint)(delegate* unmanaged<nint, nint, uint, byte*, uint, void>)&OnGlobal;
        _registryListener[1] = (nint)(delegate* unmanaged<nint, nint, uint, void>)&OnGlobalRemove;
        _seatListener = (nint*)NativeMemory.Alloc(1, (nuint)sizeof(nint));
        _seatListener[0] = (nint)(delegate* unmanaged<nint, nint, uint, void>)&OnCapabilities;
        _pointerListener = (nint*)NativeMemory.Alloc(5, (nuint)sizeof(nint));
        _pointerListener[0] = (nint)(delegate* unmanaged<nint, nint, uint, nint, int, int, void>)&OnEnter;
        _pointerListener[1] = (nint)(delegate* unmanaged<nint, nint, uint, nint, void>)&OnLeave;
        _pointerListener[2] = (nint)(delegate* unmanaged<nint, nint, uint, int, int, void>)&OnMotion;
        _pointerListener[3] = (nint)(delegate* unmanaged<nint, nint, uint, uint, uint, uint, void>)&OnButton;
        _pointerListener[4] = (nint)(delegate* unmanaged<nint, nint, uint, uint, int, void>)&OnAxis;

        _queue = wl_display_create_queue(display);
        _wrapper = wl_proxy_create_wrapper(display);
        wl_proxy_set_queue(_wrapper, _queue);
        var args = stackalloc ulong[1];
        args[0] = 0;
        _registry = wl_proxy_marshal_array_flags(_wrapper, 1, registryInterface, 1, 0, args);
        _ = wl_proxy_add_listener(_registry, _registryListener, GCHandle.ToIntPtr(_self));
        _ = wl_display_roundtrip_queue(display, _queue);
        _ = wl_display_roundtrip_queue(display, _queue);
    }

    public bool HasPointer => _pointer != 0;

    public bool Pressed => _pressed;

    public static SdlWaylandMoves? TryCreate()
    {
        if (!OperatingSystem.IsLinux() || SDL.GetCurrentVideoDriver() != "wayland")
        {
            return null;
        }

        var display = SDL.GetPointerProperty(SDL.GetGlobalProperties(), SDL.Props.GlobalVideoWaylandWLDisplayPointer, 0);
        if (display == 0 || !NativeLibrary.TryLoad(Client, out var library) ||
            !NativeLibrary.TryGetExport(library, "wl_registry_interface", out var registry) ||
            !NativeLibrary.TryGetExport(library, "wl_seat_interface", out var seat) ||
            !NativeLibrary.TryGetExport(library, "wl_pointer_interface", out var pointer))
        {
            return null;
        }

        return new SdlWaylandMoves(display, registry, seat, pointer);
    }

    public void Dispatch()
    {
        if (!_disposed)
        {
            _ = wl_display_dispatch_queue_pending(_display, _queue);
        }
    }

    public bool TryBegin(nint window, uint edges)
    {
        Dispatch();
        if (_disposed || _seat == 0 || !_pressed)
        {
            return false;
        }

        var toplevel = SDL.GetPointerProperty(SDL.GetWindowProperties(window), SDL.Props.WindowWaylandXDGToplevelPointer, 0);
        if (toplevel == 0)
        {
            return false;
        }

        var args = stackalloc ulong[3];
        args[0] = (ulong)_seat;
        args[1] = _pressSerial;
        args[2] = edges;
        _ = wl_proxy_marshal_array_flags(
            toplevel, edges == 0 ? XdgToplevelMove : XdgToplevelResize, 0, wl_proxy_get_version(toplevel), 0, args);
        _ = wl_display_flush(_display);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_pointer != 0)
        {
            wl_proxy_destroy(_pointer);
        }

        if (_seat != 0)
        {
            wl_proxy_destroy(_seat);
        }

        wl_proxy_destroy(_registry);
        wl_proxy_wrapper_destroy(_wrapper);
        wl_event_queue_destroy(_queue);
        NativeMemory.Free(_registryListener);
        NativeMemory.Free(_seatListener);
        NativeMemory.Free(_pointerListener);
        _self.Free();
    }

    private static SdlWaylandMoves? From(nint data) =>
        GCHandle.FromIntPtr(data).Target as SdlWaylandMoves;

    [UnmanagedCallersOnly]
    private static void OnGlobal(nint data, nint registry, uint name, byte* iface, uint version)
    {
        try
        {
            if (From(data) is not { _seat: 0 } self || Marshal.PtrToStringUTF8((nint)iface) != "wl_seat")
            {
                return;
            }

            var args = stackalloc ulong[4];
            args[0] = name;
            args[1] = (ulong)iface;
            args[2] = 1;
            args[3] = 0;
            self._seat = wl_proxy_marshal_array_flags(registry, 0, self._seatInterface, 1, 0, args);
            _ = wl_proxy_add_listener(self._seat, self._seatListener, data);
        }
        catch (Exception error)
        {
            SdlLog.Log.Error($"wayland seat bind failed: {error.Message}");
        }
    }

    [UnmanagedCallersOnly]
    private static void OnGlobalRemove(nint data, nint registry, uint name)
    {
    }

    [UnmanagedCallersOnly]
    private static void OnCapabilities(nint data, nint seat, uint capabilities)
    {
        try
        {
            if (From(data) is not { } self || self._pointer != 0 || (capabilities & 1) == 0)
            {
                return;
            }

            var args = stackalloc ulong[1];
            args[0] = 0;
            self._pointer = wl_proxy_marshal_array_flags(seat, 0, self._pointerInterface, 1, 0, args);
            _ = wl_proxy_add_listener(self._pointer, self._pointerListener, data);
        }
        catch (Exception error)
        {
            SdlLog.Log.Error($"wayland pointer bind failed: {error.Message}");
        }
    }

    [UnmanagedCallersOnly]
    private static void OnEnter(nint data, nint pointer, uint serial, nint surface, int x, int y)
    {
    }

    [UnmanagedCallersOnly]
    private static void OnLeave(nint data, nint pointer, uint serial, nint surface)
    {
    }

    [UnmanagedCallersOnly]
    private static void OnMotion(nint data, nint pointer, uint time, int x, int y)
    {
    }

    [UnmanagedCallersOnly]
    private static void OnButton(nint data, nint pointer, uint serial, uint time, uint button, uint state)
    {
        try
        {
            if (button != BtnLeft || From(data) is not { } self)
            {
                return;
            }

            self._pressed = state != 0;
            if (self._pressed)
            {
                self._pressSerial = serial;
            }
        }
        catch (Exception error)
        {
            SdlLog.Log.Error($"wayland pointer button failed: {error.Message}");
        }
    }

    [UnmanagedCallersOnly]
    private static void OnAxis(nint data, nint pointer, uint time, uint axis, int value)
    {
    }

    [DllImport(Client)]
    private static extern nint wl_display_create_queue(nint display);

    [DllImport(Client)]
    private static extern void wl_event_queue_destroy(nint queue);

    [DllImport(Client)]
    private static extern nint wl_proxy_create_wrapper(nint proxy);

    [DllImport(Client)]
    private static extern void wl_proxy_wrapper_destroy(nint wrapper);

    [DllImport(Client)]
    private static extern void wl_proxy_set_queue(nint proxy, nint queue);

    [DllImport(Client)]
    private static extern nint wl_proxy_marshal_array_flags(
        nint proxy, uint opcode, nint iface, uint version, uint flags, ulong* args);

    [DllImport(Client)]
    private static extern int wl_proxy_add_listener(nint proxy, nint* listener, nint data);

    [DllImport(Client)]
    private static extern uint wl_proxy_get_version(nint proxy);

    [DllImport(Client)]
    private static extern void wl_proxy_destroy(nint proxy);

    [DllImport(Client)]
    private static extern int wl_display_roundtrip_queue(nint display, nint queue);

    [DllImport(Client)]
    private static extern int wl_display_dispatch_queue_pending(nint display, nint queue);

    [DllImport(Client)]
    private static extern int wl_display_flush(nint display);
}
