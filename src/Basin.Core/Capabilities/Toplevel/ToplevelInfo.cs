namespace Basin.Capabilities;

/// <summary>
/// A snapshot of one toplevel window.
/// </summary>
/// <param name="Id">The toplevel id. It is never 0.</param>
/// <param name="Title">The window title, or an empty string.</param>
/// <param name="AppId">The application id: the xdg-shell app_id, or the X11 WM_CLASS class.</param>
/// <param name="State">The state flags.</param>
/// <param name="Surface">The root surface, or <see langword="null"/> when the toplevel has none.</param>
/// <param name="Geometry">The frame box in layout coordinates, decorations included. It is empty until the scene reports geometry.</param>
/// <param name="ClientGeometry">The box of the client area in layout coordinates, without decorations.</param>
/// <param name="ResourceName">The X11 WM_CLASS instance name, or an empty string for other shells.</param>
/// <param name="Pid">The process id of the client, or 0 when it is unknown.</param>
/// <param name="ParentId">The id of the toplevel that this one is transient for, or 0.</param>
/// <param name="AppMenuService">The D-Bus service name of the application menu, or an empty string.</param>
/// <param name="AppMenuObjectPath">The D-Bus object path of the application menu, or an empty string.</param>
/// <param name="MinimizedGeometry">The box that a taskbar set as the minimize target, or an empty box.</param>
/// <param name="X11Display">The X11 display name of an XWayland toplevel, for example ":1". It is <see langword="null"/> for other shells.</param>
/// <param name="X11Density">The density of the XWayland server that owns the toplevel. It is <see langword="null"/> for other shells.</param>
public readonly record struct ToplevelInfo(
    ulong Id,
    string Title,
    string AppId,
    ToplevelState State,
    Surface? Surface,
    Box Geometry,
    Box ClientGeometry = default,
    string ResourceName = "",
    uint Pid = 0,
    ulong ParentId = 0,
    string AppMenuService = "",
    string AppMenuObjectPath = "",
    Box MinimizedGeometry = default,
    string? X11Display = null,
    X11Density? X11Density = null);
