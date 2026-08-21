using Basin.Capabilities;
using Basin.Shell.Xdg.Protocol;
using Wayland;

namespace Basin.Shell.Xdg;

internal static class XdgToplevelRegistry
{
    private static readonly Dictionary<XdgToplevelResource, XdgToplevelWindow> Toplevels = [];

    public static void Register(XdgToplevelResource resource, XdgToplevelWindow toplevel) => Toplevels[resource] = toplevel;

    public static void Remove(XdgToplevelResource resource) => Toplevels.Remove(resource);

    public static XdgToplevelWindow? Resolve(XdgToplevelResource resource) =>
        Toplevels.TryGetValue(resource, out var toplevel) ? toplevel : null;
}
