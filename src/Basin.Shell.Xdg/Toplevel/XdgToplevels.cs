using Basin.Capabilities;
using Basin.Shell.Xdg.Protocol;
using Wayland;

namespace Basin.Shell.Xdg;

/// <summary>
/// Finds the <see cref="XdgToplevelWindow"/> of an <c>xdg_toplevel</c> resource for other protocol modules.
/// </summary>
public static class XdgToplevels
{
    /// <summary>
    /// Finds the toplevel window of a resource.
    /// </summary>
    /// <param name="resource">The <c>xdg_toplevel</c> resource from a request, or <see langword="null"/>.</param>
    /// <returns>The window, or <see langword="null"/> when <paramref name="resource"/> is null or its role object is destroyed.</returns>
    public static XdgToplevelWindow? Resolve(XdgToplevelResource? resource) =>
        resource is null ? null : XdgToplevelRegistry.Resolve(resource);
}
