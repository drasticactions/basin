using Basin.Capabilities;
using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

public readonly record struct ToplevelDragAttachment(XdgToplevelWindow Toplevel, int OffsetX, int OffsetY);
