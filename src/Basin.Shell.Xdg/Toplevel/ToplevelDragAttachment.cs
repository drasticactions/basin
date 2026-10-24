using Basin.Capabilities;
using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// A toplevel that rides a drag in flight, with the offset that the client asked for. Moving the window with the cursor is the consumer's policy.
/// </summary>
/// <param name="Toplevel">The attached toplevel.</param>
/// <param name="OffsetX">The horizontal offset of the cursor hotspot from the window-geometry origin, in logical pixels.</param>
/// <param name="OffsetY">The vertical offset of the cursor hotspot from the window-geometry origin, in logical pixels.</param>
public readonly record struct ToplevelDragAttachment(XdgToplevelWindow Toplevel, int OffsetX, int OffsetY);
