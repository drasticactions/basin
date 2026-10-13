using Xcb.Native;

namespace Basin.XWayland;

internal static unsafe class XWaylandRootCursor
{
    private const byte PictTypeDirect = 1;
    private const byte ImageFormatZPixmap = 2;
    private const uint CwCursor = 0x4000;

    public static bool Set(xcb_connection_t* conn, uint root, string theme, int size)
    {
        var loaded = XcursorTheme.Load(theme.Length > 0 ? theme : null, size);
        if ((loaded?.Get("left_ptr") ?? loaded?.Get("default")) is not { Frames.Count: > 0 } cursor)
        {
            return false;
        }

        var image = cursor.Frame(0);
        if (image.Width <= 0 || image.Height <= 0 || image.Pixels.Length < image.Width * image.Height * 4)
        {
            return false;
        }

        var format = FindArgb32(conn);
        if (format == 0)
        {
            return false;
        }

        var pixmap = Libxcb.xcb_generate_id(conn);
        _ = Libxcb.xcb_create_pixmap(conn, 32, pixmap, root, (ushort)image.Width, (ushort)image.Height);
        var gc = Libxcb.xcb_generate_id(conn);
        _ = Libxcb.xcb_create_gc(conn, gc, pixmap, 0, null);
        fixed (byte* pixels = image.Pixels)
        {
            _ = Libxcb.xcb_put_image(
                conn, ImageFormatZPixmap, pixmap, gc, (ushort)image.Width, (ushort)image.Height,
                0, 0, 0, 32, (uint)(image.Width * image.Height * 4), pixels);
        }

        _ = Libxcb.xcb_free_gc(conn, gc);
        var picture = Libxcb.xcb_generate_id(conn);
        _ = LibxcbRender.xcb_render_create_picture(conn, picture, pixmap, format, 0, null);
        var id = Libxcb.xcb_generate_id(conn);
        _ = LibxcbRender.xcb_render_create_cursor(conn, id, picture, (ushort)image.HotspotX, (ushort)image.HotspotY);
        _ = LibxcbRender.xcb_render_free_picture(conn, picture);
        _ = Libxcb.xcb_free_pixmap(conn, pixmap);
        var value = id;
        _ = Libxcb.xcb_change_window_attributes(conn, root, CwCursor, &value);
        _ = Libxcb.xcb_free_cursor(conn, id);
        return true;
    }

    private static uint FindArgb32(xcb_connection_t* conn)
    {
        var reply = LibxcbRender.xcb_render_query_pict_formats_reply(conn, LibxcbRender.xcb_render_query_pict_formats(conn), null);
        if (reply == null)
        {
            return 0;
        }

        try
        {
            var formats = LibxcbRender.xcb_render_query_pict_formats_formats(reply);
            var count = LibxcbRender.xcb_render_query_pict_formats_formats_length(reply);
            for (var i = 0; i < count; i++)
            {
                var info = formats[i];
                var direct = info.direct;
                if (info.type == PictTypeDirect && info.depth == 32 &&
                    direct.alpha_shift == 24 && direct.alpha_mask == 0xff &&
                    direct.red_shift == 16 && direct.red_mask == 0xff &&
                    direct.green_shift == 8 && direct.green_mask == 0xff &&
                    direct.blue_shift == 0 && direct.blue_mask == 0xff)
                {
                    return info.id;
                }
            }

            return 0;
        }
        finally
        {
            Libc.Free(reply);
        }
    }
}
