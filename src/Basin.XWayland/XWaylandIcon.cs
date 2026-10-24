namespace Basin.XWayland;

/// <summary>
/// One image from the <c>_NET_WM_ICON</c> property of an X11 window.
/// </summary>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="Pixels">The pixels in rows from the top, one non-premultiplied ARGB value each, <paramref name="Width"/> times <paramref name="Height"/> values in all.</param>
public sealed record XWaylandIcon(int Width, int Height, uint[] Pixels);
