using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// Who draws the frame of a toplevel. The values are the wire values of <c>zxdg_toplevel_decoration_v1.mode</c>.
/// </summary>
public enum DecorationMode
{
    /// <summary>
    /// The client draws its own frame.
    /// </summary>
    ClientSide = 1,
    /// <summary>
    /// The compositor draws the frame.
    /// </summary>
    ServerSide = 2,
}
