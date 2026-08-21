using System.Runtime.InteropServices;
using System.Text;
using Basin.Diagnostics;
using Wayland.Server;
using Xcb.Native;

namespace Basin.XWayland;

public sealed unsafe class XWaylandWm : IDisposable
{
    private const byte EventFocusIn = 9;
    private const byte EventCreateNotify = 16;
    private const byte EventDestroyNotify = 17;
    private const byte EventUnmapNotify = 18;
    private const byte EventMapNotify = 19;
    private const byte EventMapRequest = 20;
    private const byte EventConfigureNotify = 22;
    private const byte EventConfigureRequest = 23;
    private const byte EventPropertyNotify = 28;
    private const byte EventClientMessage = 33;

    private readonly xcb_connection_t* _conn;
    private readonly ICompositorEventLoop _loop;
    private readonly XwaylandShellGlobal _shell;
    private readonly IEventSource _source;
    private readonly uint _root;
    private readonly uint _wmWindow;
    private readonly Dictionary<uint, XWaylandWindow> _windows = [];
    private readonly Dictionary<ulong, XWaylandWindow> _awaitingSurface = [];
    private readonly Dictionary<string, uint> _atoms = [];

    private readonly XWaylandClipboard? _clipboard;

    public XWaylandWm(int wmFd, ICompositorEventLoop loop, XwaylandShellGlobal shell, Seat.Seat? seat = null)
    {
        _loop = loop;
        _shell = shell;
        _conn = Libxcb.xcb_connect_to_fd(wmFd, null);
        if (Libxcb.xcb_connection_has_error(_conn) != 0)
        {
            throw new InvalidOperationException("xcb connection to Xwayland failed");
        }

        var setup = Libxcb.xcb_get_setup(_conn);
        var screens = Libxcb.xcb_setup_roots_iterator(setup);
        _root = screens.data->root;

        var mask = (uint)(0x00080000 | 0x00100000 | 0x00400000 );
        _ = Libxcb.xcb_change_window_attributes(_conn, _root, 0x800 , &mask);
        _ = LibxcbComposite.xcb_composite_redirect_subwindows(_conn, _root, 1 );

        InternAtoms();

        _wmWindow = Libxcb.xcb_generate_id(_conn);
        _ = Libxcb.xcb_create_window(
            _conn, 0, _wmWindow, _root, 0, 0, 1, 1, 0,
            (ushort)xcb_window_class_t.XCB_WINDOW_CLASS_INPUT_OUTPUT,
            screens.data->root_visual, 0, null);
        _ = Libxcb.xcb_set_selection_owner(_conn, _wmWindow, Atom("WM_S0"), 0);
        SetProperty(_wmWindow, Atom("_NET_WM_NAME"), Atom("UTF8_STRING"), "basin");
        var check = _wmWindow;
        SetProperty32(_root, Atom("_NET_SUPPORTING_WM_CHECK"), 33 , &check, 1);
        SetProperty32(_wmWindow, Atom("_NET_SUPPORTING_WM_CHECK"), 33, &check, 1);
        var supported = stackalloc uint[8]
        {
            Atom("_NET_WM_NAME"), Atom("_NET_WM_STATE"), Atom("_NET_WM_STATE_MODAL"),
            Atom("_NET_WM_STATE_FULLSCREEN"), Atom("_NET_ACTIVE_WINDOW"),
            Atom("_NET_WM_WINDOW_TYPE"), Atom("_NET_SUPPORTING_WM_CHECK"), Atom("_NET_CLIENT_LIST"),
        };
        SetProperty32(_root, Atom("_NET_SUPPORTED"), 4 , supported, 8);
        _ = Libxcb.xcb_flush(_conn);

        if (seat is not null)
        {
            _clipboard = new XWaylandClipboard(_conn, seat, loop, _root, screens.data->root_visual, Atom);
        }

        shell.SerialCommitted += OnSerialCommitted;
        var fd = Libxcb.xcb_get_file_descriptor(_conn);
        _source = loop.AddFd(fd, FdReadiness.Readable, (_, _) => Pump());
    }

    public event Action<XWaylandWindow>? WindowMapped;

    public event Action<XWaylandWindow>? OverrideRedirectMapped;

    public IReadOnlyDictionary<uint, XWaylandWindow> Windows => _windows;

    public void Dispose()
    {
        _shell.SerialCommitted -= OnSerialCommitted;
        _clipboard?.Dispose();
        _source.Remove();
        Libxcb.xcb_disconnect(_conn);
    }

    private void InternAtoms()
    {
        string[] names =
        [
            "WM_S0", "WM_PROTOCOLS", "WM_DELETE_WINDOW", "WM_TAKE_FOCUS", "WM_STATE", "WM_CHANGE_STATE",
            "UTF8_STRING", "_NET_WM_NAME", "_NET_SUPPORTED", "_NET_SUPPORTING_WM_CHECK", "_NET_CLIENT_LIST",
            "_NET_ACTIVE_WINDOW", "_NET_WM_STATE", "_NET_WM_STATE_MODAL", "_NET_WM_STATE_FULLSCREEN",
            "_NET_WM_WINDOW_TYPE", "_NET_WM_PING", "WL_SURFACE_SERIAL", "_MOTIF_WM_HINTS", "_NET_WM_ICON",
            "CLIPBOARD", "PRIMARY", "TARGETS", "TEXT", "_BASIN_SELECTION",
            "_NET_WM_STATE_MAXIMIZED_VERT", "_NET_WM_STATE_MAXIMIZED_HORZ",
            "_NET_WM_WINDOW_TYPE_COMBO", "_NET_WM_WINDOW_TYPE_DND", "_NET_WM_WINDOW_TYPE_DROPDOWN_MENU",
            "_NET_WM_WINDOW_TYPE_MENU", "_NET_WM_WINDOW_TYPE_NOTIFICATION", "_NET_WM_WINDOW_TYPE_POPUP_MENU",
            "_NET_WM_WINDOW_TYPE_SPLASH", "_NET_WM_WINDOW_TYPE_DESKTOP", "_NET_WM_WINDOW_TYPE_TOOLTIP",
            "_NET_WM_WINDOW_TYPE_UTILITY",
        ];
        var cookies = new xcb_intern_atom_cookie_t[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            var bytes = Encoding.ASCII.GetBytes(names[i]);
            fixed (byte* ptr = bytes)
            {
                cookies[i] = Libxcb.xcb_intern_atom(_conn, 0, (ushort)bytes.Length, (sbyte*)ptr);
            }
        }

        for (var i = 0; i < names.Length; i++)
        {
            var reply = Libxcb.xcb_intern_atom_reply(_conn, cookies[i], null);
            if (reply != null)
            {
                _atoms[names[i]] = reply->atom;
                Libc.Free(reply);
            }
        }
    }

    private uint Atom(string name) => _atoms.GetValueOrDefault(name);

    private void SetProperty(uint window, uint property, uint type, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        fixed (byte* ptr = bytes)
        {
            _ = Libxcb.xcb_change_property(_conn, 0, window, property, type, 8, (uint)bytes.Length, ptr);
        }
    }

    private void SetProperty32(uint window, uint property, uint type, uint* values, uint count) =>
        _ = Libxcb.xcb_change_property(_conn, 0, window, property, type, 32, count, values);

    private string GetStringProperty(uint window, uint property, uint type)
    {
        var cookie = Libxcb.xcb_get_property(_conn, 0, window, property, type, 0, 1024);
        var reply = Libxcb.xcb_get_property_reply(_conn, cookie, null);
        if (reply == null)
        {
            return string.Empty;
        }

        var length = Libxcb.xcb_get_property_value_length(reply);
        var value = (byte*)Libxcb.xcb_get_property_value(reply);
        var result = length > 0 ? Encoding.UTF8.GetString(value, length) : string.Empty;
        Libc.Free(reply);
        return result;
    }

    private uint[] Get32Property(uint window, uint property, uint type, uint longLength = 64)
    {
        var cookie = Libxcb.xcb_get_property(_conn, 0, window, property, type, 0, longLength);
        var reply = Libxcb.xcb_get_property_reply(_conn, cookie, null);
        if (reply == null)
        {
            return [];
        }

        var count = Libxcb.xcb_get_property_value_length(reply) / 4;
        var value = (uint*)Libxcb.xcb_get_property_value(reply);
        var result = new uint[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = value[i];
        }

        Libc.Free(reply);
        return result;
    }

    private void Pump()
    {
        while (true)
        {
            var ev = Libxcb.xcb_poll_for_event(_conn);
            if (ev == null)
            {
                break;
            }

            try
            {
                if (_clipboard is null || !_clipboard.Handle(ev))
                {
                    Dispatch(ev);
                }
            }
            catch (Exception ex)
            {
                BasinLog.Warn($"xwayland wm event failed: {ex.Message}");
            }

            Libc.Free(ev);
        }

        _ = Libxcb.xcb_flush(_conn);
    }

    private void Dispatch(xcb_generic_event_t* ev)
    {
        switch (ev->response_type & 0x7F)
        {
            case EventCreateNotify:
            {
                var e = (xcb_create_notify_event_t*)ev;
                if (e->window == _wmWindow)
                {
                    break;
                }

                _windows[e->window] = new XWaylandWindow(
                    this, e->window, e->override_redirect != 0, e->x, e->y, e->width, e->height);
                break;
            }

            case EventDestroyNotify:
            {
                var e = (xcb_destroy_notify_event_t*)ev;
                if (_windows.Remove(e->window, out var window))
                {
                    if (window.AssociationSerial != 0)
                    {
                        _awaitingSurface.Remove(window.AssociationSerial);
                        _shell.Forget(window.AssociationSerial);
                    }

                    window.RaiseDestroyed();
                }

                break;
            }

            case EventMapRequest:
            {
                var e = (xcb_map_request_event_t*)ev;
                if (_windows.TryGetValue(e->window, out var window))
                {
                    RefreshProperties(window);
                    var state = stackalloc uint[2] { 1 , 0 };
                    SetProperty32(e->window, Atom("WM_STATE"), Atom("WM_STATE"), state, 2);
                }

                _ = Libxcb.xcb_map_window(_conn, e->window);
                break;
            }

            case EventMapNotify:
            {
                var e = (xcb_map_notify_event_t*)ev;
                if (_windows.TryGetValue(e->window, out var window))
                {
                    window.IsMappedInX = true;
                    AnnounceIfReady(window);
                }

                break;
            }

            case EventUnmapNotify:
            {
                var e = (xcb_unmap_notify_event_t*)ev;
                if (_windows.TryGetValue(e->window, out var window))
                {
                    window.IsMappedInX = false;
                    if (window.AnnouncedMapped)
                    {
                        window.AnnouncedMapped = false;
                        window.RaiseUnmapped();
                    }
                }

                break;
            }

            case EventConfigureRequest:
            {
                var e = (xcb_configure_request_event_t*)ev;
                var values = stackalloc uint[4] { (uint)e->x, (uint)e->y, e->width, e->height };
                _ = Libxcb.xcb_configure_window(
                    _conn, e->window,
                    0x1 | 0x2 | 0x4 | 0x8 ,
                    values);
                if (_windows.TryGetValue(e->window, out var window))
                {
                    (window.X, window.Y, window.Width, window.Height) = (e->x, e->y, e->width, e->height);
                    window.RaiseGeometryChanged();
                }

                break;
            }

            case EventConfigureNotify:
            {
                var e = (xcb_configure_notify_event_t*)ev;
                if (_windows.TryGetValue(e->window, out var window))
                {
                    (window.X, window.Y, window.Width, window.Height) = (e->x, e->y, e->width, e->height);
                    window.RaiseGeometryChanged();
                }

                break;
            }

            case EventPropertyNotify:
            {
                var e = (xcb_property_notify_event_t*)ev;
                if (_windows.TryGetValue(e->window, out var window))
                {
                    if (e->atom == Atom("_NET_WM_NAME") || e->atom == 39 )
                    {
                        RefreshTitle(window);
                    }
                    else if (e->atom == Atom("WM_PROTOCOLS"))
                    {
                        RefreshProtocols(window);
                    }
                    else if (e->atom == Atom("_NET_WM_ICON"))
                    {
                        RefreshIcon(window);
                    }
                    else if (e->atom == Atom("_MOTIF_WM_HINTS"))
                    {
                        RefreshDecorationHints(window);
                    }
                    else if (e->atom == Atom("_NET_WM_WINDOW_TYPE"))
                    {
                        RefreshWindowType(window);
                    }
                }

                break;
            }

            case EventClientMessage:
            {
                var e = (xcb_client_message_event_t*)ev;
                var data = (uint*)&e->data;
                if (e->type == Atom("WL_SURFACE_SERIAL"))
                {
                    if (_windows.TryGetValue(e->window, out var window))
                    {
                        var serial = ((ulong)data[1] << 32) | data[0];
                        window.AssociationSerial = serial;
                        if (_shell.SurfaceFor(serial) is { } surface)
                        {
                            Associate(window, surface);
                        }
                        else
                        {
                            _awaitingSurface[serial] = window;
                        }
                    }
                }
                else if (e->type == Atom("_NET_ACTIVE_WINDOW"))
                {
                    if (_windows.TryGetValue(e->window, out var window))
                    {
                        ActivationRequested?.Invoke(window);
                    }
                }

                break;
            }
        }
    }

    public event Action<XWaylandWindow>? ActivationRequested;

    private void OnSerialCommitted(ulong serial, Surface surface)
    {
        if (_awaitingSurface.Remove(serial, out var window))
        {
            Associate(window, surface);
        }
    }

    private void Associate(XWaylandWindow window, Surface surface)
    {
        window.Surface = surface;
        surface.Destroyed += () =>
        {
            if (window.Surface == surface)
            {
                window.Surface = null;
            }
        };
        AnnounceIfReady(window);
    }

    private void AnnounceIfReady(XWaylandWindow window)
    {
        if (window.AnnouncedMapped || !window.IsMappedInX || window.Surface is null)
        {
            return;
        }

        window.AnnouncedMapped = true;
        if (window.OverrideRedirect)
        {
            OverrideRedirectMapped?.Invoke(window);
        }
        else
        {
            WindowMapped?.Invoke(window);
        }

        window.RaiseMapped();
    }

    private void RefreshProperties(XWaylandWindow window)
    {
        RefreshTitle(window);
        var wmClass = GetStringProperty(window.WindowId, 67 , 31 );
        var parts = wmClass.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        window.Instance = parts.Length > 0 ? parts[0] : string.Empty;
        window.Class = parts.Length > 1 ? parts[1] : string.Empty;
        RefreshProtocols(window);

        var transient = Get32Property(window.WindowId, 68 , 33 );
        window.TransientFor = transient.Length > 0 ? _windows.GetValueOrDefault(transient[0]) : null;

        var state = Get32Property(window.WindowId, Atom("_NET_WM_STATE"), 4 );
        window.Modal = Array.IndexOf(state, Atom("_NET_WM_STATE_MODAL")) >= 0;
        RefreshDecorationHints(window);
        RefreshIcon(window);
        RefreshWindowType(window);
    }

    private static readonly string[] FocuslessWindowTypes =
    [
        "_NET_WM_WINDOW_TYPE_COMBO", "_NET_WM_WINDOW_TYPE_DND", "_NET_WM_WINDOW_TYPE_DROPDOWN_MENU",
        "_NET_WM_WINDOW_TYPE_MENU", "_NET_WM_WINDOW_TYPE_NOTIFICATION", "_NET_WM_WINDOW_TYPE_POPUP_MENU",
        "_NET_WM_WINDOW_TYPE_SPLASH", "_NET_WM_WINDOW_TYPE_DESKTOP", "_NET_WM_WINDOW_TYPE_TOOLTIP",
        "_NET_WM_WINDOW_TYPE_UTILITY",
    ];

    private void RefreshWindowType(XWaylandWindow window)
    {
        var types = Get32Property(window.WindowId, Atom("_NET_WM_WINDOW_TYPE"), 4 );
        var wants = true;
        foreach (var name in FocuslessWindowTypes)
        {
            if (Array.IndexOf(types, Atom(name)) >= 0)
            {
                wants = false;
                break;
            }
        }

        window.WantsFocus = wants;
    }

    internal void SetWindowMaximized(XWaylandWindow window, bool maximized)
    {
        var atoms = stackalloc uint[3];
        uint count = 0;
        if (window.Modal)
        {
            atoms[count++] = Atom("_NET_WM_STATE_MODAL");
        }

        if (maximized)
        {
            atoms[count++] = Atom("_NET_WM_STATE_MAXIMIZED_VERT");
            atoms[count++] = Atom("_NET_WM_STATE_MAXIMIZED_HORZ");
        }

        SetProperty32(window.WindowId, Atom("_NET_WM_STATE"), 4 , atoms, count);
        _ = Libxcb.xcb_flush(_conn);
    }

    private void RefreshIcon(XWaylandWindow window)
    {
        var data = Get32Property(window.WindowId, Atom("_NET_WM_ICON"), 6 , 128 * 128 + 2);
        var best = -1;
        var bestSize = 0;
        for (var i = 0; i + 2 <= data.Length;)
        {
            var width = (int)data[i];
            var height = (int)data[i + 1];
            if (width <= 0 || height <= 0 || width > 512 || height > 512 || i + 2 + width * height > data.Length)
            {
                break;
            }

            if (best < 0 || Math.Abs(width - 64) < Math.Abs(bestSize - 64))
            {
                best = i;
                bestSize = width;
            }

            i += 2 + width * height;
        }

        if (best < 0)
        {
            if (window.Icon is not null)
            {
                window.Icon = null;
                window.RaiseIconChanged();
            }

            return;
        }

        var w = (int)data[best];
        var h = (int)data[best + 1];
        var pixels = new uint[w * h];
        Array.Copy(data, best + 2, pixels, 0, w * h);
        window.Icon = new XWaylandIcon(w, h, pixels);
        window.RaiseIconChanged();
    }

    private void RefreshDecorationHints(XWaylandWindow window)
    {
        var hints = Get32Property(window.WindowId, Atom("_MOTIF_WM_HINTS"), Atom("_MOTIF_WM_HINTS"));
        var wants = hints.Length < 3 || (hints[0] & 0x2 ) == 0 || hints[2] != 0;
        if (wants != window.WantsDecorations)
        {
            window.WantsDecorations = wants;
            window.RaiseDecorationsChanged();
        }
    }

    private void RefreshTitle(XWaylandWindow window)
    {
        var title = GetStringProperty(window.WindowId, Atom("_NET_WM_NAME"), Atom("UTF8_STRING"));
        if (title.Length == 0)
        {
            title = GetStringProperty(window.WindowId, 39 , 31 );
        }

        if (title != window.Title)
        {
            window.Title = title;
            window.RaiseTitleChanged();
        }
    }

    private void RefreshProtocols(XWaylandWindow window)
    {
        var protocols = Get32Property(window.WindowId, Atom("WM_PROTOCOLS"), 4 );
        window.SupportsDeleteWindow = Array.IndexOf(protocols, Atom("WM_DELETE_WINDOW")) >= 0;
        window.SupportsTakeFocus = Array.IndexOf(protocols, Atom("WM_TAKE_FOCUS")) >= 0;
    }

    internal void ConfigureWindow(XWaylandWindow window, int x, int y, int width, int height)
    {
        var values = stackalloc uint[4] { (uint)x, (uint)y, (uint)width, (uint)height };
        _ = Libxcb.xcb_configure_window(_conn, window.WindowId, 0x1 | 0x2 | 0x4 | 0x8, values);
        (window.X, window.Y, window.Width, window.Height) = (x, y, width, height);

        var notify = new xcb_configure_notify_event_t
        {
            response_type = EventConfigureNotify,
            @event = window.WindowId,
            window = window.WindowId,
            x = (short)x,
            y = (short)y,
            width = (ushort)width,
            height = (ushort)height,
        };
        _ = Libxcb.xcb_send_event(_conn, 0, window.WindowId, 0x00020000 , (sbyte*)&notify);
        _ = Libxcb.xcb_flush(_conn);
    }

    internal void ActivateWindow(XWaylandWindow window)
    {
        if (window.SupportsTakeFocus)
        {
            var message = new xcb_client_message_event_t
            {
                response_type = EventClientMessage,
                format = 32,
                window = window.WindowId,
                type = Atom("WM_PROTOCOLS"),
            };
            var data = (uint*)&message.data;
            data[0] = Atom("WM_TAKE_FOCUS");
            data[1] = 0;
            _ = Libxcb.xcb_send_event(_conn, 0, window.WindowId, 0, (sbyte*)&message);
        }

        _ = Libxcb.xcb_set_input_focus(_conn, 1 , window.WindowId, 0);
        var active = window.WindowId;
        SetProperty32(_root, Atom("_NET_ACTIVE_WINDOW"), 33, &active, 1);
        _ = Libxcb.xcb_flush(_conn);
    }

    internal void CloseWindow(XWaylandWindow window)
    {
        if (window.SupportsDeleteWindow)
        {
            var message = new xcb_client_message_event_t
            {
                response_type = EventClientMessage,
                format = 32,
                window = window.WindowId,
                type = Atom("WM_PROTOCOLS"),
            };
            var data = (uint*)&message.data;
            data[0] = Atom("WM_DELETE_WINDOW");
            data[1] = 0;
            _ = Libxcb.xcb_send_event(_conn, 0, window.WindowId, 0, (sbyte*)&message);
        }
        else
        {
            _ = Libxcb.xcb_kill_client(_conn, window.WindowId);
        }

        _ = Libxcb.xcb_flush(_conn);
    }

    internal void RaiseWindow(XWaylandWindow window)
    {
        var values = stackalloc uint[1] { 0 };
        _ = Libxcb.xcb_configure_window(_conn, window.WindowId, 0x40 , values);
        _ = Libxcb.xcb_flush(_conn);
    }
}
