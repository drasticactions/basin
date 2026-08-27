using System.Text;
using Basin.Diagnostics;
using Xcb.Native;
using static Basin.XWayland.XWaylandLog;

namespace Basin.XWayland;

public sealed unsafe class X11KeyboardGrab : IDisposable
{
    private const byte EventKeyPress = 2;
    private const byte EventKeyRelease = 3;
    private const byte GrabModeAsync = 1;
    private const byte KeycodeBase = 8;

    private readonly xcb_connection_t* _conn;
    private readonly IEventSource _source;
    private readonly Action<uint, bool> _key;
    private bool _dead;
    private bool _disposed;

    private X11KeyboardGrab(ICompositorEventLoop loop, xcb_connection_t* conn, Action<uint, bool> key)
    {
        _conn = conn;
        _key = key;
        _source = loop.AddFd(Libxcb.xcb_get_file_descriptor(conn), FdReadiness.Readable, (_, _) => Pump());
        BasinCounters.Track();
    }

    public static X11KeyboardGrab? TryGrab(ICompositorEventLoop loop, string? display, Action<uint, bool> key)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(key);
        xcb_connection_t* conn;
        int screen;
        if (display is null)
        {
            conn = Libxcb.xcb_connect(null, &screen);
        }
        else
        {
            var bytes = Encoding.ASCII.GetBytes(display + "\0");
            fixed (byte* ptr = bytes)
            {
                conn = Libxcb.xcb_connect((sbyte*)ptr, &screen);
            }
        }

        if (Libxcb.xcb_connection_has_error(conn) != 0)
        {
            Log.Warn($"no X server answers on '{display ?? Environment.GetEnvironmentVariable("DISPLAY")}'");
            Libxcb.xcb_disconnect(conn);
            return null;
        }

        var screens = Libxcb.xcb_setup_roots_iterator(Libxcb.xcb_get_setup(conn));
        for (var i = 0; i < screen && screens.rem > 1; i++)
        {
            Libxcb.xcb_screen_next(&screens);
        }

        var root = screens.data->root;
        var cookie = Libxcb.xcb_grab_keyboard(conn, 0, root, 0, GrabModeAsync, GrabModeAsync);
        var reply = Libxcb.xcb_grab_keyboard_reply(conn, cookie, null);
        if (reply == null || reply->status != 0)
        {
            var status = reply == null ? -1 : reply->status;
            if (reply != null)
            {
                Libc.Free(reply);
            }

            Log.Warn($"the X server refused the keyboard grab (status {status})");
            Libxcb.xcb_disconnect(conn);
            return null;
        }

        Libc.Free(reply);
        _ = Libxcb.xcb_flush(conn);
        return new X11KeyboardGrab(loop, conn, key);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_dead)
        {
            _ = Libxcb.xcb_ungrab_keyboard(_conn, 0);
            _ = Libxcb.xcb_flush(_conn);
            _source.Remove();
        }

        Libxcb.xcb_disconnect(_conn);
        BasinCounters.Untrack();
    }

    private void Pump()
    {
        if (_dead)
        {
            return;
        }

        while (true)
        {
            var ev = Libxcb.xcb_poll_for_event(_conn);
            if (ev == null)
            {
                break;
            }

            try
            {
                var kind = ev->response_type & 0x7F;
                if (kind is EventKeyPress or EventKeyRelease)
                {
                    var e = (xcb_key_press_event_t*)ev;
                    if (e->detail >= KeycodeBase)
                    {
                        _key((uint)(e->detail - KeycodeBase), kind == EventKeyPress);
                    }
                }
            }
            catch (Exception error)
            {
                Log.Warn($"a grabbed key could not be delivered: {error.Message}");
            }

            Libc.Free(ev);
        }

        if (Libxcb.xcb_connection_has_error(_conn) != 0)
        {
            _dead = true;
            _source.Remove();
            Log.Warn($"the X server connection was lost, the keyboard grab is off");
            return;
        }

        _ = Libxcb.xcb_flush(_conn);
    }
}
