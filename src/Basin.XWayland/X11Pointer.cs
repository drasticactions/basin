using System.Text;
using Basin.Diagnostics;
using Xcb.Native;

namespace Basin.XWayland;

public sealed unsafe class X11Pointer : IDisposable
{
    private readonly xcb_connection_t* _conn;
    private readonly uint _root;
    private bool _disposed;

    private X11Pointer(xcb_connection_t* conn, int screen)
    {
        _conn = conn;
        var screens = Libxcb.xcb_setup_roots_iterator(Libxcb.xcb_get_setup(conn));
        for (var i = 0; i < screen && screens.rem > 1; i++)
        {
            Libxcb.xcb_screen_next(&screens);
        }

        _root = screens.data->root;
        BasinCounters.Track();
    }

    public static X11Pointer? TryConnect(string? display = null)
    {
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
            BasinLog.Warn($"no X server answers on '{display ?? Environment.GetEnvironmentVariable("DISPLAY")}'");
            Libxcb.xcb_disconnect(conn);
            return null;
        }

        return new X11Pointer(conn, screen);
    }

    public (int X, int Y)? TryGetPosition()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Libxcb.xcb_connection_has_error(_conn) != 0)
        {
            return null;
        }

        var cookie = Libxcb.xcb_query_pointer(_conn, _root);
        xcb_generic_error_t* error = null;
        var reply = Libxcb.xcb_query_pointer_reply(_conn, cookie, &error);
        if (reply is null)
        {
            if (error is not null)
            {
                Libc.Free(error);
            }

            return null;
        }

        var position = (reply->root_x, reply->root_y);
        Libc.Free(reply);
        return position;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Libxcb.xcb_disconnect(_conn);
        BasinCounters.Untrack();
    }
}
