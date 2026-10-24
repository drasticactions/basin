using System.Text;
using Basin.Diagnostics;
using Xcb.Native;
using static Basin.XWayland.XWaylandLog;

namespace Basin.XWayland;

/// <summary>
/// Reads the pointer position from an X server over its own xcb connection.
/// </summary>
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

    /// <summary>
    /// Connects to an X server.
    /// </summary>
    /// <param name="display">The X display name. <see langword="null"/> uses <c>DISPLAY</c>.</param>
    /// <returns>The reader, or <see langword="null"/> when no X server answers. The caller owns the result.</returns>
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
            Log.Warn($"no X server answers on '{display ?? Environment.GetEnvironmentVariable("DISPLAY")}'");
            Libxcb.xcb_disconnect(conn);
            return null;
        }

        return new X11Pointer(conn, screen);
    }

    /// <summary>
    /// Queries the pointer position on the root window. The call blocks until the X server answers.
    /// </summary>
    /// <returns>The position in root window pixels, or <see langword="null"/> when the connection is lost or the query fails.</returns>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
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

    /// <summary>
    /// Closes the X connection. A second call does nothing.
    /// </summary>
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
