using Basin.Capabilities;
using Basin.Shell.Xdg.Protocol;
using Wayland;

namespace Basin.Shell.Xdg;

public static class XdgToplevels
{
    public static XdgToplevelWindow? Resolve(XdgToplevelResource? resource) =>
        resource is null ? null : XdgToplevelRegistry.Resolve(resource);
}
