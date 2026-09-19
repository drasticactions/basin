using Basin.Shell.Xdg.Protocol;
using Wayland;

namespace Basin.Tests.Nested;

internal sealed class HarnessToplevel(WlSurface surface, XdgSurface xdgSurface, XdgToplevel toplevel)
{
    public WlSurface Surface { get; } = surface;

    public XdgSurface XdgSurface { get; } = xdgSurface;

    public XdgToplevel Toplevel { get; } = toplevel;

    public ClientShmBuffer? Buffer { get; set; }

    public ZxdgToplevelDecorationV1? Decoration { get; set; }

    public bool Configured { get; set; }

    public bool CloseReceived { get; set; }

    public int ConfiguredWidth { get; set; }

    public int ConfiguredHeight { get; set; }

    public void Destroy()
    {
        Decoration?.Dispose();
        Toplevel.Dispose();
        XdgSurface.Dispose();
        Surface.Dispose();
    }
}
