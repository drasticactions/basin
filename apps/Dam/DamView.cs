using Basin;
using Basin.Capabilities;
using Basin.Host;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Dam;

internal sealed class DamView
{
    public DamView(XdgToplevelWindow xdg, SceneSurface scene)
    {
        Handle = xdg;
        Xdg = xdg;
        Scene = scene;
    }

    public DamView(Basin.XWayland.XWaylandWindow x11, SceneSurface scene)
    {
        Handle = x11;
        X11 = x11;
        Scene = scene;
    }

    public IToplevelHandle Handle { get; }

    public XdgToplevelWindow? Xdg { get; }

    public Basin.XWayland.XWaylandWindow? X11 { get; }

    public SceneSurface Scene { get; }

    public Surface Surface => Handle.Surface!;

    public string Title => Handle.Title;

    public bool IsPrimary => Handle.Parent is null;

    public bool WantsFocus => Handle.WantsFocus;

    public bool WantsFullscreen { get; set; }

    public bool IsTransientFor(DamView parent) => Handle.IsTransientFor(parent.Handle);

    public (int Width, int Height) GeometrySize() => Handle.NaturalSize;

    public void SetActivated(bool activated) => Handle.SetActivated(activated);

    public void Maximize(int x, int y, int width, int height)
    {
        Handle.Configure(x, y, width, height);
        Handle.SetMaximized(true);
    }
}
