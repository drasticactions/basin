using Basin.Seat;
using Basin.Shell.Xdg.Protocol;
using Wayland;

namespace Basin.Shell.Xdg;

internal static class XdgPopupRegistry
{
    private static readonly Dictionary<XdgPopupResource, XdgPopupWindow> Popups = [];

    public static void Register(XdgPopupResource resource, XdgPopupWindow popup) => Popups[resource] = popup;

    public static void Remove(XdgPopupResource resource) => Popups.Remove(resource);

    public static XdgPopupWindow? Resolve(XdgPopupResource resource) =>
        Popups.TryGetValue(resource, out var popup) ? popup : null;
}
