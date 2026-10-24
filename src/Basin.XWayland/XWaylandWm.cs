using System.Runtime.InteropServices;
using System.Text;
using Basin.Diagnostics;
using Wayland.Server;
using Xcb.Native;
using static Basin.XWayland.XWaylandLog;

namespace Basin.XWayland;

/// <summary>
/// The X11 window manager of one Xwayland server. It pairs each X window with its Wayland surface, reads window properties, and publishes the display settings.
/// </summary>
public sealed unsafe class XWaylandWm : IDisposable
{
    private const byte EventFocusIn = 9;
    private const byte EventFocusOut = 10;
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
    private readonly WlClient? _client;
    private readonly IEventSource _source;
    private readonly uint _root;
    private readonly uint _wmWindow;
    private readonly Dictionary<uint, XWaylandWindow> _windows = [];
    private readonly Dictionary<ulong, XWaylandWindow> _awaitingSurface = [];
    private readonly Dictionary<string, uint> _atoms = [];

    private readonly XWaylandClipboard? _clipboard;

    private XWaylandWmOptions _options;

    private double _scale;

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="XWaylandWm"/> class with the default <see cref="XWaylandWmOptions"/>.
    /// </summary>
    /// <param name="wmFd">The window manager socket that Xwayland got with <c>-wm</c>. The window manager owns it and closes it on dispose.</param>
    /// <param name="loop">The loop that reads the X connection.</param>
    /// <param name="shell">The shell global that pairs serials with surfaces.</param>
    /// <param name="seat">The seat for keyboard focus and the clipboard bridge, or <see langword="null"/> for neither.</param>
    /// <exception cref="InvalidOperationException">The X connection on <paramref name="wmFd"/> fails.</exception>
    public XWaylandWm(int wmFd, ICompositorEventLoop loop, XwaylandShellGlobal shell, Seat.Seat? seat = null)
        : this(wmFd, loop, shell, seat, new XWaylandWmOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="XWaylandWm"/> class with no known Xwayland client, so serials match a surface of any client.
    /// </summary>
    /// <param name="wmFd">The window manager socket that Xwayland got with <c>-wm</c>. The window manager owns it and closes it on dispose.</param>
    /// <param name="loop">The loop that reads the X connection.</param>
    /// <param name="shell">The shell global that pairs serials with surfaces.</param>
    /// <param name="seat">The seat for keyboard focus and the clipboard bridge, or <see langword="null"/> for neither.</param>
    /// <param name="options">The display settings to publish.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The X connection on <paramref name="wmFd"/> fails.</exception>
    public XWaylandWm(int wmFd, ICompositorEventLoop loop, XwaylandShellGlobal shell, Seat.Seat? seat, XWaylandWmOptions options)
        : this(wmFd, loop, shell, seat, options, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="XWaylandWm"/> class. It takes the <c>WM_S0</c> selection, and the XSETTINGS selection when <paramref name="options"/> asks for it.
    /// </summary>
    /// <param name="wmFd">The window manager socket that Xwayland got with <c>-wm</c>. The window manager owns it and closes it on dispose.</param>
    /// <param name="loop">The loop that reads the X connection.</param>
    /// <param name="shell">The shell global that pairs serials with surfaces.</param>
    /// <param name="seat">The seat for keyboard focus and the clipboard bridge, or <see langword="null"/> for neither.</param>
    /// <param name="options">The display settings to publish.</param>
    /// <param name="client">The Wayland client of the Xwayland process, which limits the serial lookup to its surfaces, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The X connection on <paramref name="wmFd"/> fails.</exception>
    public XWaylandWm(
        int wmFd, ICompositorEventLoop loop, XwaylandShellGlobal shell, Seat.Seat? seat, XWaylandWmOptions options, WlClient? client)
    {
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _loop = loop;
        _options = options;
        _scale = options.Scale;
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
        QueryShapeExtension();

        WriteResourceManager();
        SetRootCursor();
        if (options.PublishXSettings)
        {
            XSettings = new XSettingsManager(_conn, loop, _root, screens.data->root_visual, Atom, RequestDrain);
            PublishDisplaySettings();
            XSettings.WriteNow();
        }

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
        var supported = stackalloc uint[14]
        {
            Atom("_NET_WM_NAME"), Atom("_NET_WM_STATE"), Atom("_NET_WM_STATE_MODAL"),
            Atom("_NET_WM_STATE_FULLSCREEN"), Atom("_NET_ACTIVE_WINDOW"),
            Atom("_NET_WM_WINDOW_TYPE"), Atom("_NET_SUPPORTING_WM_CHECK"), Atom("_NET_CLIENT_LIST"),
            Atom("_NET_WM_STATE_HIDDEN"), Atom("_NET_WM_ALLOWED_ACTIONS"), Atom("_NET_WM_ACTION_MINIMIZE"),
            Atom("_NET_WM_STATE_MAXIMIZED_VERT"), Atom("_NET_WM_STATE_MAXIMIZED_HORZ"), Atom("_NET_WM_MOVERESIZE"),
        };
        SetProperty32(_root, Atom("_NET_SUPPORTED"), 4 , supported, 14);
        PublishClientList();
        Flush();

        if (seat is not null)
        {
            _clipboard = new XWaylandClipboard(_conn, seat, loop, _root, screens.data->root_visual, Atom, RequestDrain);
            _keyboard = seat.Keyboard;
            _keyboard.FocusChanged += OnKeyboardFocusChanged;
        }

        shell.SerialCommitted += OnSerialCommitted;
        var fd = Libxcb.xcb_get_file_descriptor(_conn);
        _source = loop.AddFd(fd, FdReadiness.Readable, (_, _) => Pump());
    }

    /// <summary>
    /// Occurs when a managed window is mapped in X and has its surface. Each map raises it once.
    /// </summary>
    public event Action<XWaylandWindow>? WindowMapped;

    /// <summary>
    /// Occurs at the end of <see cref="Dispose"/>. The argument is this window manager.
    /// </summary>
    public event Action<XWaylandWm>? Disposed;

    /// <summary>
    /// Gets the Wayland client of the Xwayland process, or <see langword="null"/> when the constructor got none.
    /// </summary>
    public WlClient? Client => _client;

    /// <summary>
    /// Occurs when an override-redirect window is mapped in X and has its surface. Each map raises it once.
    /// </summary>
    public event Action<XWaylandWindow>? OverrideRedirectMapped;

    /// <summary>
    /// Gets every X window that exists now, mapped or not, by X11 window id.
    /// </summary>
    public IReadOnlyDictionary<uint, XWaylandWindow> Windows => _windows;

    /// <summary>
    /// Gets the current X11 scale. X11 coordinates are logical coordinates times this value.
    /// </summary>
    public double Scale => _scale;

    /// <summary>
    /// Gets the options of the last <see cref="UpdateOptions"/> call, or of the constructor. <see cref="SetScale"/> does not change them.
    /// </summary>
    public XWaylandWmOptions Options => _options;

    /// <summary>
    /// Gets the XSETTINGS publisher, or <see langword="null"/> when the options turned XSETTINGS off.
    /// </summary>
    public XSettingsManager? XSettings { get; }

    /// <summary>
    /// Gets the DPI that the window manager publishes for the current scale and DPI buckets.
    /// </summary>
    public int Dpi => XWaylandDpi.For(_scale, _options.DpiBuckets);

    /// <summary>
    /// Changes the X11 scale. It configures each mapped window again at its logical geometry and publishes the new DPI, cursor size and XSETTINGS.
    /// </summary>
    /// <param name="scale">The new scale. It must be positive and finite.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not positive or not finite.</exception>
    public void SetScale(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "an X11 scale must be positive and finite");
        }

        if (scale == _scale)
        {
            return;
        }

        _scale = scale;
        foreach (var window in _windows.Values)
        {
            if (window.OverrideRedirect)
            {
                var bounds = window.X11Bounds;
                SetGeometryFromX11(window, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                window.RaiseGeometryChanged();
            }
            else if (window.IsMappedInX)
            {
                RefreshSizeHints(window);
                ConfigureWindow(window, window.X, window.Y, window.Width, window.Height);
            }
        }

        PublishDisplaySettings();
        WriteResourceManager();
        SetRootCursor();
        Flush();
    }

    /// <summary>
    /// Applies new options. A new scale goes through <see cref="SetScale"/>. Otherwise it publishes the DPI, the resource database and the root cursor again.
    /// </summary>
    /// <param name="options">The new options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public void UpdateOptions(XWaylandWmOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        if (options.Scale != _scale)
        {
            SetScale(options.Scale);
            return;
        }

        PublishDisplaySettings();
        WriteResourceManager();
        SetRootCursor();
        Flush();
    }

    private void PublishDisplaySettings()
    {
        if (XSettings is not { } settings)
        {
            return;
        }

        var dpi = Dpi;
        var factor = XWaylandDpi.WindowScalingFactor(_scale);
        settings.Set("Xft/DPI", dpi * 1024);
        settings.Set("Gdk/WindowScalingFactor", factor);
        settings.Set("Gdk/UnscaledDPI", dpi * 1024 / factor);
        settings.Set("Gtk/CursorThemeSize", (int)Math.Round(_options.CursorSize * _scale / factor));
        if (_options.CursorTheme.Length > 0)
        {
            settings.Set("Gtk/CursorThemeName", _options.CursorTheme);
        }
        else
        {
            settings.Remove("Gtk/CursorThemeName");
        }
    }

    private (string Theme, int Size) _rootCursor = (string.Empty, 0);

    private void SetRootCursor()
    {
        var theme = _options.CursorTheme;
        var size = (int)Math.Round(_options.CursorSize * _scale);
        if ((theme, size) == _rootCursor)
        {
            return;
        }

        _rootCursor = (theme, size);
        if (!XWaylandRootCursor.Set(_conn, _root, theme, size))
        {
            Log.Warn($"cursor theme '{theme}' has no left_ptr cursor at {size}px. X11 windows without their own cursor will show the X default cursor.");
        }
    }

    private const uint AtomString = 31;

    private void WriteResourceManager()
    {
        var existing = GetStringProperty(_root, Atom("RESOURCE_MANAGER"), AtomString);
        var values = new List<(string, string)>
        {
            ("Xft.dpi", Dpi.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Xcursor.size", ((int)Math.Round(_options.CursorSize * _scale)).ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
        if (_options.CursorTheme.Length > 0)
        {
            values.Add(("Xcursor.theme", _options.CursorTheme));
        }

        SetProperty(_root, Atom("RESOURCE_MANAGER"), AtomString, XResourceDatabase.Merge(existing, values));
    }

    private int X11Position(int value) =>
        _scale == 1.0 ? value : (int)Math.Round(value * _scale, MidpointRounding.AwayFromZero);

    private int X11Size(XWaylandWindow? window, int value) =>
        _scale == 1.0
            ? value
            : Math.Max(1, (int)Math.Round(value * _scale, MidpointRounding.AwayFromZero));

    private int LogicalPosition(int value) =>
        _scale == 1.0 ? value : (int)Math.Round(value / _scale, MidpointRounding.AwayFromZero);

    private int LogicalSize(XWaylandWindow? window, int value) =>
        _scale == 1.0
            ? value
            : Math.Max(1, (int)Math.Round(value / _scale, MidpointRounding.AwayFromZero));

    private void SetGeometryFromX11(XWaylandWindow window, int x, int y, int width, int height)
    {
        if (window.EmulatedMode is { } emulated && emulated == new Box(x, y, width, height))
        {
            window.X11Bounds = emulated;
            var logical = window.EmulatedLogical;
            (window.X, window.Y, window.Width, window.Height) = (logical.X, logical.Y, logical.Width, logical.Height);
            return;
        }

        window.X11Bounds = new Box(x, y, width, height);
        window.CoversScreen = window.OverrideRedirect && window.EmulatedModes.Count > 0 && x == 0 && y == 0
            && (width, height) == RootSize();
        window.IsOffscreenStub = window.OverrideRedirect && width <= 1 && height <= 1 && IsOutsideScreen(x, y, width, height);
        (window.X, window.Y, window.Width, window.Height) =
            (LogicalPosition(x), LogicalPosition(y), LogicalSize(window, width), LogicalSize(window, height));
    }

    private bool IsOutsideScreen(int x, int y, int width, int height)
    {
        if (x + width <= 0 || y + height <= 0)
        {
            return true;
        }

        var (rootWidth, rootHeight) = RootSize();
        return rootWidth > 0 && rootHeight > 0 && (x >= rootWidth || y >= rootHeight);
    }

    private (int Width, int Height) RootSize()
    {
        var cookie = Libxcb.xcb_get_geometry(_conn, _root);
        var reply = Libxcb.xcb_get_geometry_reply(_conn, cookie, null);
        if (reply == null)
        {
            return (0, 0);
        }

        var size = ((int)reply->width, (int)reply->height);
        Libc.Free(reply);
        return size;
    }

    /// <summary>
    /// Stops the clipboard bridge, closes the X connection and raises <see cref="Disposed"/>. The windows get no destroyed event. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_keyboard is not null)
        {
            _keyboard.FocusChanged -= OnKeyboardFocusChanged;
        }

        _shell.SerialCommitted -= OnSerialCommitted;
        _clipboard?.Dispose();
        _source.Remove();
        Libxcb.xcb_disconnect(_conn);
        Disposed?.Invoke(this);
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
            "_NET_WM_STATE_MAXIMIZED_VERT", "_NET_WM_STATE_MAXIMIZED_HORZ", "_NET_WM_STATE_HIDDEN",
            "_NET_WM_ALLOWED_ACTIONS", "_NET_WM_ACTION_MINIMIZE",
            "_NET_WM_WINDOW_TYPE_COMBO", "_NET_WM_WINDOW_TYPE_DND", "_NET_WM_WINDOW_TYPE_DROPDOWN_MENU",
            "_NET_WM_WINDOW_TYPE_MENU", "_NET_WM_WINDOW_TYPE_NOTIFICATION", "_NET_WM_WINDOW_TYPE_POPUP_MENU",
            "_NET_WM_WINDOW_TYPE_SPLASH", "_NET_WM_WINDOW_TYPE_DESKTOP", "_NET_WM_WINDOW_TYPE_TOOLTIP",
            "_NET_WM_WINDOW_TYPE_UTILITY", "_KDE_NET_WM_APPMENU_SERVICE_NAME", "_KDE_NET_WM_APPMENU_OBJECT_PATH",
            "_NET_WM_WINDOW_TYPE_NORMAL", "_NET_WM_WINDOW_TYPE_DIALOG", "_NET_WM_WINDOW_TYPE_TOOLBAR",
            "_NET_WM_WINDOW_TYPE_DOCK", "_NET_WM_PID", "STEAM_GAME",
            "_XSETTINGS_S0", "_XSETTINGS_SETTINGS", "MANAGER", "RESOURCE_MANAGER",
            "_XWAYLAND_RANDR_EMU_MONITOR_RECTS", "_GTK_THEME_VARIANT",
            "_NET_WM_STATE_SKIP_TASKBAR", "_NET_WM_STATE_SKIP_PAGER", "_NET_WM_STATE_ABOVE", "_NET_WM_STATE_BELOW",
            "_NET_WM_STATE_STICKY", "_NET_WM_STATE_DEMANDS_ATTENTION",
            "_NET_WM_MOVERESIZE", "_WINE_HWND_STYLE", "_WINE_HWND_EXSTYLE",
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

    private int Read32Property(uint window, uint property, uint type, Span<uint> into)
    {
        var cookie = Libxcb.xcb_get_property(_conn, 0, window, property, type, 0, (uint)into.Length);
        var reply = Libxcb.xcb_get_property_reply(_conn, cookie, null);
        if (reply == null)
        {
            return 0;
        }

        var count = Math.Min(Libxcb.xcb_get_property_value_length(reply) / 4, into.Length);
        var value = (uint*)Libxcb.xcb_get_property_value(reply);
        for (var i = 0; i < count; i++)
        {
            into[i] = value[i];
        }

        Libc.Free(reply);
        return count;
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

    private bool _inPump;
    private bool _drainQueued;
    private Action? _drain;

    internal void RequestDrain()
    {
        if (_inPump || _drainQueued || _disposed)
        {
            return;
        }

        _drainQueued = true;
        _loop.AddIdle(_drain ??= () =>
        {
            _drainQueued = false;
            if (!_disposed)
            {
                Pump();
            }
        });
    }

    private void Flush()
    {
        _ = Libxcb.xcb_flush(_conn);
        RequestDrain();
    }

    private void Pump()
    {
        _inPump = true;
        try
        {
            PumpEvents();
        }
        finally
        {
            _inPump = false;
        }
    }

    private void PumpEvents()
    {
        while (true)
        {
            var ev = Libxcb.xcb_poll_for_event(_conn);
            if (ev == null)
            {
                _ = Libxcb.xcb_flush(_conn);
                ev = Libxcb.xcb_poll_for_queued_event(_conn);
                if (ev == null)
                {
                    break;
                }
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
                Log.Warn($"xwayland wm event failed: {ex.Message}");
            }

            Libc.Free(ev);
        }
    }

    private void Dispatch(xcb_generic_event_t* ev)
    {
        if (_shapeFirstEvent != 0 && (ev->response_type & 0x7F) == _shapeFirstEvent)
        {
            var shape = (xcb_shape_notify_event_t*)ev;
            if (_windows.TryGetValue(shape->affected_window, out var shaped))
            {
                RefreshShape(shaped);
            }

            return;
        }

        switch (ev->response_type & 0x7F)
        {
            case EventFocusIn:
            case EventFocusOut:
            {
                var e = (xcb_focus_in_event_t*)ev;
                OnFocusEvent((ev->response_type & 0x7F) == EventFocusIn, e->@event, e->detail, e->mode);
                break;
            }

            case EventCreateNotify:
            {
                var e = (xcb_create_notify_event_t*)ev;
                if (e->window == _wmWindow)
                {
                    break;
                }

                var created = new XWaylandWindow(
                    this, e->window, e->override_redirect != 0, e->x, e->y, e->width, e->height);
                SetGeometryFromX11(created, e->x, e->y, e->width, e->height);
                _windows[e->window] = created;
                var events = 0x00400000u;
                _ = Libxcb.xcb_change_window_attributes(_conn, e->window, 0x800, &events);
                if (_shapeFirstEvent != 0)
                {
                    _ = LibxcbShape.xcb_shape_select_input(_conn, e->window, 1);
                }
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
                        _shell.Forget(_client, window.AssociationSerial);
                    }

                    window.RaiseDestroyed();
                    if (_clientList.Remove(e->window))
                    {
                        PublishClientList();
                    }
                }

                break;
            }

            case EventMapRequest:
            {
                var e = (xcb_map_request_event_t*)ev;
                if (_windows.TryGetValue(e->window, out var window))
                {
                    window.OverrideRedirect = false;
                    var managedEvents = 0x00400000u | 0x00200000u;
                    _ = Libxcb.xcb_change_window_attributes(_conn, e->window, 0x800, &managedEvents);
                    RefreshProperties(window);
                    RefreshProcess(window);
                    RefreshShape(window);
                    var state = stackalloc uint[2] { NormalState, 0 };
                    SetProperty32(e->window, Atom("WM_STATE"), Atom("WM_STATE"), state, 2);
                    var actions = stackalloc uint[1] { Atom("_NET_WM_ACTION_MINIMIZE") };
                    SetProperty32(e->window, Atom("_NET_WM_ALLOWED_ACTIONS"), 4 , actions, 1);
                }

                _ = Libxcb.xcb_map_window(_conn, e->window);
                break;
            }

            case EventMapNotify:
            {
                var e = (xcb_map_notify_event_t*)ev;
                if (_windows.TryGetValue(e->window, out var window))
                {
                    var overrideRedirect = e->override_redirect != 0;
                    if (overrideRedirect != window.OverrideRedirect)
                    {
                        var announced = window.AnnouncedMapped;
                        if (announced)
                        {
                            window.AnnouncedMapped = false;
                            window.RaiseUnmapped();
                        }

                        window.OverrideRedirect = overrideRedirect;
                        if (!overrideRedirect)
                        {
                            RefreshProperties(window);
                            RefreshProcess(window);
                        }
                    }

                    window.IsMappedInX = true;
                    if (window.OverrideRedirect)
                    {
                        RefreshShape(window);
                    }

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

                    if (_clientList.Remove(e->window))
                    {
                        PublishClientList();
                    }
                }

                break;
            }

            case EventConfigureRequest:
            {
                var e = (xcb_configure_request_event_t*)ev;
                _windows.TryGetValue(e->window, out var window);
                if (window is { AnnouncedMapped: true, OverrideRedirect: false } && ConfigureRequest is { } filter
                    && !filter(window, new Box(
                        LogicalPosition(e->x), LogicalPosition(e->y), LogicalSize(window, e->width), LogicalSize(window, e->height))))
                {
                    if (window.AllowClientMove && (e->value_mask & 0x3) != 0)
                    {
                        var x = (e->value_mask & 0x1) != 0 ? LogicalPosition(e->x) : window.X;
                        var y = (e->value_mask & 0x2) != 0 ? LogicalPosition(e->y) : window.Y;
                        ConfigureWindow(window, x, y, window.Width, window.Height);
                        window.RaiseGeometryChanged();
                    }

                    break;
                }

                var values = stackalloc uint[5] { (uint)e->x, (uint)e->y, e->width, e->height, 0 };
                _ = Libxcb.xcb_configure_window(
                    _conn, e->window,
                    0x1 | 0x2 | 0x4 | 0x8 | 0x10,
                    values);
                if (window is not null)
                {
                    SetGeometryFromX11(window, e->x, e->y, e->width, e->height);
                    window.RaiseGeometryChanged();
                }

                break;
            }

            case EventConfigureNotify:
            {
                var e = (xcb_configure_notify_event_t*)ev;
                if (_windows.TryGetValue(e->window, out var window))
                {
                    var resized = window.X11Bounds.Width != e->width || window.X11Bounds.Height != e->height;
                    SetGeometryFromX11(window, e->x, e->y, e->width, e->height);
                    if (resized && (window.BoundingShape is not null || window.InputShape is not null))
                    {
                        RefreshShape(window);
                    }

                    window.RaiseGeometryChanged();
                }

                break;
            }

            case EventPropertyNotify:
            {
                var e = (xcb_property_notify_event_t*)ev;
                _lastTimestamp = e->time;
                if (_windows.TryGetValue(e->window, out var window))
                {
                    if (e->atom == AtomWmHints || e->atom == Atom("_NET_WM_STATE") ||
                        e->atom == Atom("_WINE_HWND_STYLE") || e->atom == Atom("_WINE_HWND_EXSTYLE"))
                    {
                        if (RefreshFocusFacts(window))
                        {
                            window.RaisePropertiesChanged();
                        }
                    }
                    else if (e->atom == Atom("_NET_WM_NAME") || e->atom == 39 )
                    {
                        RefreshTitle(window);
                    }
                    else if (e->atom == Atom("WM_PROTOCOLS"))
                    {
                        var takesFocus = window.SupportsTakeFocus;
                        RefreshProtocols(window);
                        if (takesFocus != window.SupportsTakeFocus)
                        {
                            window.RaisePropertiesChanged();
                        }
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
                        if (RefreshWindowType(window))
                        {
                            window.RaisePropertiesChanged();
                        }
                    }
                    else if (e->atom == Atom("_XWAYLAND_RANDR_EMU_MONITOR_RECTS"))
                    {
                        if (RefreshEmulatedModes(window))
                        {
                            if (window.FullscreenState && window.IsMappedInX && !window.OverrideRedirect)
                            {
                                ConfigureWindow(window, window.X, window.Y, window.Width, window.Height);
                            }
                            else if (window.OverrideRedirect)
                            {
                                var bounds = window.X11Bounds;
                                SetGeometryFromX11(window, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                                window.RaiseGeometryChanged();
                            }

                            window.RaisePropertiesChanged();
                        }
                    }
                    else if (e->atom == Atom("_GTK_THEME_VARIANT"))
                    {
                        if (RefreshThemeVariant(window))
                        {
                            window.RaisePropertiesChanged();
                        }
                    }
                    else if (e->atom == Atom("STEAM_GAME"))
                    {
                        if (RefreshSteamGame(window))
                        {
                            window.RaisePropertiesChanged();
                        }
                    }
                    else if (e->atom == AtomWmNormalHints)
                    {
                        RefreshSizeHints(window);
                    }
                    else if (e->atom == Atom("_KDE_NET_WM_APPMENU_SERVICE_NAME") || e->atom == Atom("_KDE_NET_WM_APPMENU_OBJECT_PATH"))
                    {
                        RefreshAppMenu(window);
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
                        if (_shell.SurfaceFor(_client, serial) is { } surface)
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
                else if (e->type == Atom("WM_CHANGE_STATE"))
                {
                    if (data[0] == IconicState && _windows.TryGetValue(e->window, out var window))
                    {
                        window.RaiseMinimizeRequested(true);
                    }
                }
                else if (e->type == Atom("_NET_WM_STATE"))
                {
                    if (_windows.TryGetValue(e->window, out var window))
                    {
                        UpdateClientState(window, data[0], data[1], data[2]);
                        HandleStateMessage(window, data[0], data[1], data[2]);
                    }
                }
                else if (e->type == Atom("_NET_WM_MOVERESIZE"))
                {
                    if (_windows.TryGetValue(e->window, out var window) && !window.OverrideRedirect)
                    {
                        window.MoveResizeOrigin = new Point(LogicalPosition((int)data[0]), LogicalPosition((int)data[1]));
                        HandleMoveResize(window, data[2]);
                    }
                }

                break;
            }
        }
    }

    /// <summary>
    /// Occurs when a client sends <c>_NET_ACTIVE_WINDOW</c>, or when a toplevel request asks to activate the window. With a handler, a toplevel request does not activate the window by itself.
    /// </summary>
    public event Action<XWaylandWindow>? ActivationRequested;

    /// <summary>
    /// Gets or sets the policy filter for a configure request of a mapped managed window. It gets the requested logical box and returns <see langword="false"/> to refuse it.
    /// </summary>
    public Func<XWaylandWindow, Box, bool>? ConfigureRequest { get; set; }

    private void HandleStateMessage(XWaylandWindow window, uint action, uint first, uint second)
    {
        if (Mentions("_NET_WM_STATE_HIDDEN"))
        {
            window.RaiseMinimizeRequested(Wanted(window.Minimized));
        }

        if (Mentions("_NET_WM_STATE_FULLSCREEN"))
        {
            window.RaiseFullscreenRequested(Wanted(window.FullscreenState));
        }

        if ((Mentions("_NET_WM_STATE_MAXIMIZED_VERT") || Mentions("_NET_WM_STATE_MAXIMIZED_HORZ"))
            && !(window.ForceUnmaximize && !window.AnnouncedMapped))
        {
            window.RaiseMaximizeRequested(Wanted(window.MaximizedState));
        }

        bool Mentions(string name) => first == Atom(name) || second == Atom(name);

        bool Wanted(bool current) => action switch
        {
            NetWmStateRemove => false,
            NetWmStateAdd => true,
            _ => !current,
        };
    }

    private void OnSerialCommitted(WlClient client, ulong serial, Surface surface)
    {
        if (_client is not null && !ReferenceEquals(client, _client))
        {
            return;
        }

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
            if (!_clientList.Contains(window.WindowId))
            {
                _clientList.Add(window.WindowId);
                PublishClientList();
            }

            WindowMapped?.Invoke(window);
        }

        window.RaiseMapped();
    }

    private readonly List<uint> _clientList = [];

    private void PublishClientList()
    {
        var count = _clientList.Count;
        var ids = stackalloc uint[Math.Max(count, 1)];
        for (var i = 0; i < count; i++)
        {
            ids[i] = _clientList[i];
        }

        SetProperty32(_root, Atom("_NET_CLIENT_LIST"), 33, ids, (uint)count);
        Flush();
    }

    private void RefreshProperties(XWaylandWindow window)
    {
        RefreshTitle(window);
        var wmClass = GetStringProperty(window.WindowId, 67 , 31 );
        var parts = wmClass.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var instance = parts.Length > 0 ? parts[0] : string.Empty;
        var wmClassName = parts.Length > 1 ? parts[1] : string.Empty;
        var identityChanged = instance != window.Instance || wmClassName != window.Class;
        window.Instance = instance;
        window.Class = wmClassName;
        RefreshProtocols(window);

        var transient = Get32Property(window.WindowId, 68 , 33 );
        var transientFor = transient.Length > 0 ? _windows.GetValueOrDefault(transient[0]) : null;
        identityChanged |= !ReferenceEquals(transientFor, window.TransientFor);
        window.TransientFor = transientFor;
        identityChanged |= RefreshWindowType(window);
        identityChanged |= RefreshFocusFacts(window);
        identityChanged |= RefreshSteamGame(window);
        identityChanged |= RefreshEmulatedModes(window);
        identityChanged |= RefreshThemeVariant(window);
        if (identityChanged)
        {
            window.RaisePropertiesChanged();
        }

        var state = Get32Property(window.WindowId, Atom("_NET_WM_STATE"), 4 );
        window.Modal = Array.IndexOf(state, Atom("_NET_WM_STATE_MODAL")) >= 0;
        window.WantsFullscreen = Array.IndexOf(state, Atom("_NET_WM_STATE_FULLSCREEN")) >= 0;
        window.WantsMaximized = Array.IndexOf(state, Atom("_NET_WM_STATE_MAXIMIZED_VERT")) >= 0
            || Array.IndexOf(state, Atom("_NET_WM_STATE_MAXIMIZED_HORZ")) >= 0;
        RefreshDecorationHints(window);
        RefreshIcon(window);
        RefreshSizeHints(window);
        RefreshAppMenu(window);
    }

    private const uint AtomWmNormalHints = 40;

    private const uint AtomWmSizeHints = 41;

    private const uint SizeHintMinSize = 1 << 4;

    private const uint SizeHintMaxSize = 1 << 5;

    private const uint SizeHintBaseSize = 1 << 8;

    private const uint SizeHintUserPosition = 1 << 0;

    private const uint SizeHintProgramPosition = 1 << 2;

    private const uint SizeHintWinGravity = 1 << 9;

    private const uint AtomWmHints = 35;

    private const uint AtomCardinal = 6;

    private bool RefreshFocusFacts(XWaylandWindow window)
    {
        Span<uint> hints = stackalloc uint[9];
        var hintCount = Read32Property(window.WindowId, AtomWmHints, AtomWmHints, hints);
        var acceptsInput = hintCount < 2 || (hints[0] & 1) == 0 || hints[1] != 0;

        Span<uint> state = stackalloc uint[MaxStateAtoms];
        var stateCount = Read32Property(window.WindowId, Atom("_NET_WM_STATE"), 4, state);
        var skipTaskbar = state[..stateCount].Contains(Atom("_NET_WM_STATE_SKIP_TASKBAR"));
        var skipPager = state[..stateCount].Contains(Atom("_NET_WM_STATE_SKIP_PAGER"));

        Span<uint> word = stackalloc uint[1];
        uint? style = Read32Property(window.WindowId, Atom("_WINE_HWND_STYLE"), AtomCardinal, word) == 1 ? word[0] : null;
        uint? styleEx = Read32Property(window.WindowId, Atom("_WINE_HWND_EXSTYLE"), AtomCardinal, word) == 1 ? word[0] : null;

        var changed = acceptsInput != window.AcceptsInput || skipTaskbar != window.SkipTaskbar ||
            skipPager != window.SkipPager || style != window.HwndStyle || styleEx != window.HwndStyleEx;
        window.AcceptsInput = acceptsInput;
        window.SkipTaskbar = skipTaskbar;
        window.SkipPager = skipPager;
        window.HwndStyle = style;
        window.HwndStyleEx = styleEx;
        return changed;
    }

    private void HandleMoveResize(XWaylandWindow window, uint direction)
    {
        switch (direction)
        {
            case <= 7:
                window.RaiseResizeRequested(MoveResizeEdges[direction], keyboard: false);
                break;
            case 8:
                window.RaiseMoveRequested(keyboard: false);
                break;
            case 9:
                window.RaiseResizeRequested(X11ResizeEdges.BottomRight, keyboard: true);
                break;
            case 10:
                window.RaiseMoveRequested(keyboard: true);
                break;
            case 11:
                window.RaiseMoveResizeCancelled();
                break;
        }
    }

    private static readonly X11ResizeEdges[] MoveResizeEdges =
    [
        X11ResizeEdges.TopLeft, X11ResizeEdges.Top, X11ResizeEdges.TopRight, X11ResizeEdges.Right,
        X11ResizeEdges.BottomRight, X11ResizeEdges.Bottom, X11ResizeEdges.BottomLeft, X11ResizeEdges.Left,
    ];

    private uint _lastTimestamp;

    private XWaylandWindow? _focused;

    private const byte NotifyGrab = 1;

    private const byte NotifyUngrab = 2;

    private const byte NotifyInferior = 2;

    private const byte NotifyPointerRoot = 6;

    private const byte NotifyDetailNone = 7;

    private void OnFocusEvent(bool focusIn, uint eventWindow, byte detail, byte mode)
    {
        if (mode is NotifyGrab or NotifyUngrab || _focused is not { } focused || _disposed)
        {
            return;
        }

        if (!_windows.ContainsKey(focused.WindowId))
        {
            _focused = null;
            return;
        }

        if (eventWindow == focused.WindowId)
        {
            if (!focusIn && detail == NotifyInferior)
            {
                focused.FocusChild = CurrentFocus();
            }
            else if (!focusIn && detail is NotifyPointerRoot or NotifyDetailNone)
            {
                RestoreFocus(focused);
            }

            return;
        }

        if (focusIn && _windows.ContainsKey(eventWindow))
        {
            RestoreFocus(focused);
        }
    }

    private uint? CurrentFocus()
    {
        var cookie = Libxcb.xcb_get_input_focus(_conn);
        var reply = Libxcb.xcb_get_input_focus_reply(_conn, cookie, null);
        if (reply == null)
        {
            return null;
        }

        var focus = reply->focus;
        Libc.Free(reply);
        return focus;
    }

    private void RestoreFocus(XWaylandWindow focused)
    {
        var target = focused.FocusChild is { } child && child != 0 && child != 1 ? child : focused.WindowId;
        Log.Debug($"X focus left 0x{focused.WindowId:x}; giving it back to 0x{target:x}");
        _ = Libxcb.xcb_set_input_focus(_conn, 1, target, 0);
        Flush();
    }

    private void RefreshSizeHints(XWaylandWindow window)
    {
        var hints = Get32Property(window.WindowId, AtomWmNormalHints, AtomWmSizeHints, 18);
        var minWidth = 0;
        var minHeight = 0;
        var maxWidth = 0;
        var maxHeight = 0;
        var userPosition = false;
        var programPosition = false;
        uint? gravity = null;
        if (hints.Length >= 1)
        {
            userPosition = (hints[0] & SizeHintUserPosition) != 0;
            programPosition = (hints[0] & SizeHintProgramPosition) != 0;
            if ((hints[0] & SizeHintWinGravity) != 0 && hints.Length >= 18)
            {
                gravity = hints[17];
            }
        }

        var positionChanged = userPosition != window.UserPosition || programPosition != window.ProgramPosition ||
            gravity != window.Gravity;
        window.UserPosition = userPosition;
        window.ProgramPosition = programPosition;
        window.Gravity = gravity;
        if (hints.Length >= 9)
        {
            var flags = hints[0];
            if ((flags & SizeHintMinSize) != 0)
            {
                minWidth = (int)hints[5];
                minHeight = (int)hints[6];
            }
            else if ((flags & SizeHintBaseSize) != 0 && hints.Length >= 17)
            {
                minWidth = (int)hints[15];
                minHeight = (int)hints[16];
            }

            if ((flags & SizeHintMaxSize) != 0)
            {
                maxWidth = (int)hints[7];
                maxHeight = (int)hints[8];
            }
        }

        minWidth = LogicalMinimum(window, Math.Max(0, minWidth));
        minHeight = LogicalMinimum(window, Math.Max(0, minHeight));
        maxWidth = LogicalMaximum(window, Math.Max(0, maxWidth));
        maxHeight = LogicalMaximum(window, Math.Max(0, maxHeight));
        if (!positionChanged && minWidth == window.MinWidth && minHeight == window.MinHeight
            && maxWidth == window.MaxWidth && maxHeight == window.MaxHeight)
        {
            return;
        }

        window.MinWidth = minWidth;
        window.MinHeight = minHeight;
        window.MaxWidth = maxWidth;
        window.MaxHeight = maxHeight;
        window.RaiseSizeHintsChanged();
    }

    private int LogicalMinimum(XWaylandWindow window, int value) =>
        value == 0 || _scale == 1.0
            ? value
            : (int)Math.Ceiling(value / _scale);

    private int LogicalMaximum(XWaylandWindow window, int value) =>
        value == 0 || _scale == 1.0
            ? value
            : Math.Max(1, (int)Math.Floor(value / _scale));

    private static readonly string[] FocuslessWindowTypes =
    [
        "_NET_WM_WINDOW_TYPE_COMBO", "_NET_WM_WINDOW_TYPE_DND", "_NET_WM_WINDOW_TYPE_DROPDOWN_MENU",
        "_NET_WM_WINDOW_TYPE_MENU", "_NET_WM_WINDOW_TYPE_NOTIFICATION", "_NET_WM_WINDOW_TYPE_POPUP_MENU",
        "_NET_WM_WINDOW_TYPE_SPLASH", "_NET_WM_WINDOW_TYPE_DESKTOP", "_NET_WM_WINDOW_TYPE_TOOLTIP",
        "_NET_WM_WINDOW_TYPE_UTILITY",
    ];

    private static readonly string[] NamedWindowTypes =
    [
        "_NET_WM_WINDOW_TYPE_NORMAL", "_NET_WM_WINDOW_TYPE_DIALOG", "_NET_WM_WINDOW_TYPE_TOOLBAR",
        "_NET_WM_WINDOW_TYPE_DOCK", .. FocuslessWindowTypes,
    ];

    private bool RefreshWindowType(XWaylandWindow window)
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

        var changed = window.TypeWantsFocus != wants;
        window.TypeWantsFocus = wants;

        string? type = null;
        foreach (var atom in types)
        {
            foreach (var name in NamedWindowTypes)
            {
                if (atom != 0 && atom == Atom(name))
                {
                    type = name["_NET_WM_WINDOW_TYPE_".Length..].ToLowerInvariant();
                    break;
                }
            }

            if (type is not null)
            {
                break;
            }
        }

        type ??= window.TransientFor is not null ? "dialog" : "normal";
        if (type == window.WindowType)
        {
            return changed;
        }

        window.WindowType = type;
        return true;
    }

    private bool RefreshEmulatedModes(XWaylandWindow window)
    {
        var values = Get32Property(window.WindowId, Atom("_XWAYLAND_RANDR_EMU_MONITOR_RECTS"), 6 , 64);
        var count = values.Length % 4 == 0 ? values.Length / 4 : 0;
        var modes = new Box[count];
        for (var i = 0; i < count; i++)
        {
            modes[i] = new Box((int)values[i * 4], (int)values[(i * 4) + 1], (int)values[(i * 4) + 2], (int)values[(i * 4) + 3]);
        }

        if (modes.AsSpan().SequenceEqual(window.EmulatedModes is Box[] known ? known : [.. window.EmulatedModes]))
        {
            return false;
        }

        window.EmulatedModes = modes;
        return true;
    }

    private Box? EmulatedModeAt(XWaylandWindow window, int x11X, int x11Y)
    {
        if (!window.HonorEmulatedModes)
        {
            return null;
        }

        foreach (var mode in window.EmulatedModes)
        {
            if (mode.X == x11X && mode.Y == x11Y && mode.Width > 0 && mode.Height > 0)
            {
                return mode;
            }
        }

        return null;
    }

    private bool RefreshThemeVariant(XWaylandWindow window)
    {
        var variant = GetStringProperty(window.WindowId, Atom("_GTK_THEME_VARIANT"), 0).TrimEnd('\0');
        if (variant == window.ThemeVariant)
        {
            return false;
        }

        window.ThemeVariant = variant;
        return true;
    }

    private bool RefreshSteamGame(XWaylandWindow window)
    {
        var values = Get32Property(window.WindowId, Atom("STEAM_GAME"), 6 , 1);
        uint? game = values.Length > 0 && values[0] != 0 ? values[0] : null;
        if (game == window.SteamGame)
        {
            return false;
        }

        window.SteamGame = game;
        return true;
    }

    private void RefreshProcess(XWaylandWindow window)
    {
        if (window.Pid != 0)
        {
            return;
        }

        var verified = QueryClientPid(window.WindowId);
        var pid = verified;
        if (pid <= 0)
        {
            var claimed = Get32Property(window.WindowId, Atom("_NET_WM_PID"), 6 , 1);
            pid = claimed.Length > 0 ? (int)claimed[0] : 0;
        }

        if (pid <= 0)
        {
            return;
        }

        window.Process = ProcessIdentity.Read(pid);
        window.PidVerified = verified > 0;
        window.RaisePropertiesChanged();
    }

    private byte _shapeFirstEvent;

    private void QueryShapeExtension()
    {
        var name = "SHAPE"u8;
        fixed (byte* ptr = name)
        {
            var cookie = Libxcb.xcb_query_extension(_conn, (ushort)name.Length, (sbyte*)ptr);
            var reply = Libxcb.xcb_query_extension_reply(_conn, cookie, null);
            if (reply == null)
            {
                return;
            }

            _shapeFirstEvent = reply->present != 0 ? reply->first_event : (byte)0;
            Libc.Free(reply);
        }
    }

    private const byte ShapeBounding = 0;

    private const byte ShapeInput = 2;

    private void RefreshShape(XWaylandWindow window)
    {
        if (_shapeFirstEvent == 0)
        {
            return;
        }

        var extents = LibxcbShape.xcb_shape_query_extents_reply(_conn, LibxcbShape.xcb_shape_query_extents(_conn, window.WindowId), null);
        var boundingShaped = extents != null && extents->bounding_shaped != 0;
        if (extents != null)
        {
            Libc.Free(extents);
        }

        var bounds = window.X11Bounds;
        var bounding = boundingShaped ? ShapeRegion(window.WindowId, ShapeBounding, bounds) : null;
        var input = ShapeRegion(window.WindowId, ShapeInput, bounds);
        if (input is not null && input.RectangleCount == 1
            && input.Extents is { X1: <= 0, Y1: <= 0 } whole && whole.X2 >= bounds.Width && whole.Y2 >= bounds.Height)
        {
            input.Dispose();
            input = null;
        }

        window.SetShapes(bounding, input);
    }

    private Pixman.PixmanRegion32? ShapeRegion(uint windowId, byte kind, Box bounds)
    {
        var reply = LibxcbShape.xcb_shape_get_rectangles_reply(_conn, LibxcbShape.xcb_shape_get_rectangles(_conn, windowId, kind), null);
        if (reply == null)
        {
            return null;
        }

        var region = new Pixman.PixmanRegion32();
        var rectangles = LibxcbShape.xcb_shape_get_rectangles_rectangles(reply);
        var count = LibxcbShape.xcb_shape_get_rectangles_rectangles_length(reply);
        for (var i = 0; i < count; i++)
        {
            var r = rectangles[i];
            if (r.width > 0 && r.height > 0)
            {
                region.UnionRect(region, r.x, r.y, r.width, r.height);
            }
        }

        Libc.Free(reply);
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            region.IntersectRect(region, 0, 0, (uint)bounds.Width, (uint)bounds.Height);
        }

        return region;
    }

    private bool _xresMissing;

    private int QueryClientPid(uint windowId)
    {
        if (_xresMissing)
        {
            return 0;
        }

        try
        {
            var spec = new xcb_res_client_id_spec_t { client = windowId, mask = (uint)xcb_res_client_id_mask_t.XCB_RES_CLIENT_ID_MASK_LOCAL_CLIENT_PID };
            var cookie = LibxcbRes.xcb_res_query_client_ids(_conn, 1, &spec);
            xcb_generic_error_t* error = null;
            var reply = LibxcbRes.xcb_res_query_client_ids_reply(_conn, cookie, &error);
            if (error != null)
            {
                Libc.Free(error);
            }

            if (reply == null)
            {
                return 0;
            }

            var pid = 0;
            var iterator = LibxcbRes.xcb_res_query_client_ids_ids_iterator(reply);
            while (iterator.rem > 0)
            {
                var value = iterator.data;
                if ((value->spec.mask & (uint)xcb_res_client_id_mask_t.XCB_RES_CLIENT_ID_MASK_LOCAL_CLIENT_PID) != 0
                    && LibxcbRes.xcb_res_client_id_value_value_length(value) >= 1)
                {
                    pid = (int)LibxcbRes.xcb_res_client_id_value_value(value)[0];
                    break;
                }

                LibxcbRes.xcb_res_client_id_value_next(&iterator);
            }

            Libc.Free(reply);
            return pid;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            _xresMissing = true;
            Log.Debug($"X-Resource is unavailable, so X11 pids are unverified: {error.Message}");
            return 0;
        }
    }

    internal void SetWindowMaximized(XWaylandWindow window, bool maximized)
    {
        var changed = window.MaximizedState != maximized;
        window.MaximizedState = maximized;
        WriteWindowState(window);
        if (changed)
        {
            window.RaiseStateChanged();
        }
    }

    internal void SetWindowFullscreen(XWaylandWindow window, bool fullscreen)
    {
        var changed = window.FullscreenState != fullscreen;
        window.FullscreenState = fullscreen;
        WriteWindowState(window);
        if (changed)
        {
            window.RaiseStateChanged();
        }
    }

    internal void SetWindowMinimized(XWaylandWindow window, bool minimized)
    {
        if (window.Minimized == minimized)
        {
            return;
        }

        window.Minimized = minimized;
        WriteWindowState(window);
        var state = stackalloc uint[2] { minimized ? IconicState : NormalState, 0 };
        SetProperty32(window.WindowId, Atom("WM_STATE"), Atom("WM_STATE"), state, 2);
        Flush();
        window.RaisePropertiesChanged();
    }

    private const uint NetWmStateRemove = 0;

    private const uint NetWmStateAdd = 1;

    private const uint NormalState = 1;

    private const uint IconicState = 3;

    private void WriteWindowState(XWaylandWindow window)
    {
        Span<uint> current = stackalloc uint[MaxStateAtoms];
        var kept = Read32Property(window.WindowId, Atom("_NET_WM_STATE"), 4, current);
        var atoms = stackalloc uint[MaxStateAtoms + 5];
        uint count = 0;
        for (var i = 0; i < kept; i++)
        {
            if (!IsManagedState(current[i]) && !Contains(atoms, count, current[i]))
            {
                atoms[count++] = current[i];
            }
        }

        if (window.Modal)
        {
            atoms[count++] = Atom("_NET_WM_STATE_MODAL");
        }

        if (window.MaximizedState)
        {
            atoms[count++] = Atom("_NET_WM_STATE_MAXIMIZED_VERT");
            atoms[count++] = Atom("_NET_WM_STATE_MAXIMIZED_HORZ");
        }

        if (window.FullscreenState)
        {
            atoms[count++] = Atom("_NET_WM_STATE_FULLSCREEN");
        }

        if (window.Minimized)
        {
            atoms[count++] = Atom("_NET_WM_STATE_HIDDEN");
        }

        SetProperty32(window.WindowId, Atom("_NET_WM_STATE"), 4 , atoms, count);
        Flush();
    }

    private const int MaxStateAtoms = 32;

    private static bool Contains(uint* atoms, uint count, uint atom)
    {
        for (var i = 0; i < count; i++)
        {
            if (atoms[i] == atom)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsManagedState(uint atom) =>
        atom == Atom("_NET_WM_STATE_MODAL") || atom == Atom("_NET_WM_STATE_MAXIMIZED_VERT") ||
        atom == Atom("_NET_WM_STATE_MAXIMIZED_HORZ") || atom == Atom("_NET_WM_STATE_FULLSCREEN") ||
        atom == Atom("_NET_WM_STATE_HIDDEN");

    private bool IsClientState(uint atom) =>
        atom == Atom("_NET_WM_STATE_SKIP_TASKBAR") || atom == Atom("_NET_WM_STATE_SKIP_PAGER") ||
        atom == Atom("_NET_WM_STATE_ABOVE") || atom == Atom("_NET_WM_STATE_BELOW") ||
        atom == Atom("_NET_WM_STATE_STICKY") || atom == Atom("_NET_WM_STATE_DEMANDS_ATTENTION");

    private void UpdateClientState(XWaylandWindow window, uint action, uint first, uint second)
    {
        if (!(first != 0 && IsClientState(first)) && !(second != 0 && IsClientState(second)))
        {
            return;
        }

        Span<uint> current = stackalloc uint[MaxStateAtoms];
        var length = Read32Property(window.WindowId, Atom("_NET_WM_STATE"), 4, current);
        var atoms = stackalloc uint[MaxStateAtoms + 2];
        uint count = 0;
        for (var i = 0; i < length; i++)
        {
            atoms[count++] = current[i];
        }

        Apply(first);
        Apply(second);
        SetProperty32(window.WindowId, Atom("_NET_WM_STATE"), 4, atoms, count);
        Flush();

        void Apply(uint atom)
        {
            if (atom == 0 || !IsClientState(atom))
            {
                return;
            }

            var index = -1;
            for (var i = 0; i < count; i++)
            {
                if (atoms[i] == atom)
                {
                    index = i;
                    break;
                }
            }

            var want = action switch
            {
                NetWmStateRemove => false,
                NetWmStateAdd => true,
                _ => index < 0,
            };
            if (want && index < 0)
            {
                atoms[count++] = atom;
            }
            else if (!want && index >= 0)
            {
                atoms[index] = atoms[--count];
            }
        }
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

    private void RefreshAppMenu(XWaylandWindow window)
    {
        var service = GetStringProperty(window.WindowId, Atom("_KDE_NET_WM_APPMENU_SERVICE_NAME"), 0).TrimEnd('\0');
        var path = GetStringProperty(window.WindowId, Atom("_KDE_NET_WM_APPMENU_OBJECT_PATH"), 0).TrimEnd('\0');
        if (service != window.AppMenuServiceName || path != window.AppMenuObjectPath)
        {
            window.AppMenuServiceName = service;
            window.AppMenuObjectPath = path;
            window.RaiseAppMenuChanged();
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
        var bounds = window.X11Bounds;
        var x11X = LogicalPosition(bounds.X) == x ? bounds.X : X11Position(x);
        var x11Y = LogicalPosition(bounds.Y) == y ? bounds.Y : X11Position(y);
        var x11Width = bounds.Width > 0 && LogicalSize(window, bounds.Width) == width ? bounds.Width : X11Size(window, width);
        var x11Height = bounds.Height > 0 && LogicalSize(window, bounds.Height) == height ? bounds.Height : X11Size(window, height);
        window.EmulatedMode = window.FullscreenState ? EmulatedModeAt(window, x11X, x11Y) : null;
        if (window.EmulatedMode is { } emulated)
        {
            (x11Width, x11Height) = (emulated.Width, emulated.Height);
            window.EmulatedLogical = new Box(x, y, width, height);
        }

        var values = stackalloc uint[5] { (uint)x11X, (uint)x11Y, (uint)x11Width, (uint)x11Height, 0 };
        _ = Libxcb.xcb_configure_window(_conn, window.WindowId, 0x1 | 0x2 | 0x4 | 0x8 | 0x10, values);
        (window.X, window.Y, window.Width, window.Height) = (x, y, width, height);
        window.X11Bounds = new Box(x11X, x11Y, x11Width, x11Height);

        var notify = new xcb_configure_notify_event_t
        {
            response_type = EventConfigureNotify,
            @event = window.WindowId,
            window = window.WindowId,
            x = (short)x11X,
            y = (short)x11Y,
            width = (ushort)x11Width,
            height = (ushort)x11Height,
        };
        _ = Libxcb.xcb_send_event(_conn, 0, window.WindowId, 0x00020000 , (sbyte*)&notify);
        Flush();
    }

    private Seat.SeatKeyboard? _keyboard;
    private Surface? _keyboardFocus;

    internal Surface? KeyboardFocus => _keyboardFocus;

    internal event Action<Surface?, Surface?>? KeyboardFocusChanged;

    private void OnKeyboardFocusChanged(Surface? focus)
    {
        var previous = _keyboardFocus;
        _keyboardFocus = focus;
        if (_focused is { } focused && (focus is null || !ReferenceEquals(focused.Surface, focus)))
        {
            _focused = null;
        }

        if (previous != focus)
        {
            KeyboardFocusChanged?.Invoke(previous, focus);
        }
    }

    internal void RequestActivation(XWaylandWindow window)
    {
        if (ActivationRequested is { } activation)
        {
            activation.Invoke(window);
            return;
        }

        ActivateWindow(window);
    }

    internal void ActivateWindow(XWaylandWindow window)
    {
        if (!window.WantsFocus)
        {
            Log.Debug($"0x{window.WindowId:x} does not take focus; activation leaves the X focus alone");
            return;
        }

        if (!ReferenceEquals(_focused, window))
        {
            window.FocusChild = null;
        }

        _focused = window;
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
            data[1] = _lastTimestamp;
            _ = Libxcb.xcb_send_event(_conn, 0, window.WindowId, 0, (sbyte*)&message);
        }

        _ = Libxcb.xcb_set_input_focus(_conn, 1 , window.WindowId, 0);
        var active = window.WindowId;
        SetProperty32(_root, Atom("_NET_ACTIVE_WINDOW"), 33, &active, 1);
        Flush();
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

        Flush();
    }

    internal void RaiseWindow(XWaylandWindow window) => Restack(window, StackAbove);

    internal void LowerWindow(XWaylandWindow window) => Restack(window, StackBelow);

    private const uint StackAbove = 0;

    private const uint StackBelow = 1;

    private void Restack(XWaylandWindow window, uint mode)
    {
        var values = stackalloc uint[1] { mode };
        _ = Libxcb.xcb_configure_window(_conn, window.WindowId, 0x40 , values);
        Flush();
    }
}
