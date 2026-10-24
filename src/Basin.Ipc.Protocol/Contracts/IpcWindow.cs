using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// One window, as <c>windows/list</c>, <c>windows/get</c>, <c>windows/wait</c> and the window events write it.
/// </summary>
/// <param name="Id">The window id. An xdg or X11 id is above 2 to the power 53. A JavaScript client must not read it as a double.</param>
/// <param name="Title">The window title.</param>
/// <param name="AppId">The application id: the xdg-shell app_id, or the X11 WM_CLASS class.</param>
/// <param name="State">The state flags.</param>
/// <param name="Geometry">The frame box in layout coordinates, decorations included.</param>
/// <param name="ClientGeometry">The box of the client area in layout coordinates. Window-relative pointer input is relative to it.</param>
/// <param name="Pid">The process id of the client, or 0 when it is unknown.</param>
/// <param name="Parent">The id of the window that this one is transient for, or <see langword="null"/> for none.</param>
/// <param name="Output">The name of the output under the center of <paramref name="Geometry"/>, or <see langword="null"/> when no output is there.</param>
/// <param name="Workspace">The id of the workspace that holds the window, or <see langword="null"/> for none.</param>
/// <param name="X11Display">The X11 display of an X11 window, for example <c>:2</c>. The key is absent for a Wayland window.</param>
/// <param name="X11Density"><see cref="NativeDensity"/> or <see cref="LowDensity"/> for an X11 window. The key is absent for a Wayland window.</param>
/// <param name="FrameLimit">The frame limit in frames per second. The key is absent when the window has no limit.</param>
public readonly record struct IpcWindow(
    ulong Id,
    string Title,
    string AppId,
    IpcWindowState State,
    IpcBox Geometry,
    IpcBox ClientGeometry,
    uint Pid,
    ulong? Parent,
    string? Output,
    ulong? Workspace,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? X11Display = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? X11Density = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? FrameLimit = null)
{
    /// <summary>
    /// The density name of an X11 window on the Xwayland server at the output scale.
    /// </summary>
    public const string NativeDensity = "native";

    /// <summary>
    /// The density name of an X11 window on the second Xwayland server, at scale 1.
    /// </summary>
    public const string LowDensity = "low";
}
