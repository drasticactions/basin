using Basin;
using Basin.Host;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Dam;

internal sealed class DamView
{
    public DamView(XdgToplevelWindow xdg, SceneSurface scene)
    {
        Xdg = xdg;
        Scene = scene;
    }

    public DamView(Basin.XWayland.XWaylandWindow x11, SceneSurface scene)
    {
        X11 = x11;
        Scene = scene;
    }

    public XdgToplevelWindow? Xdg { get; }

    public Basin.XWayland.XWaylandWindow? X11 { get; }

    public SceneSurface Scene { get; }

    public Surface Surface => Xdg is { } xdg ? xdg.Surface : X11!.Surface!;

    public string Title => Xdg is { } xdg ? xdg.Title : X11!.Title;

    public bool IsPrimary => Xdg is { } xdg ? xdg.Parent is null : X11!.TransientFor is null;

    public bool WantsFocus => X11 is null || X11.WantsFocus;

    public bool WantsFullscreen { get; set; }

    public bool IsTransientFor(DamView parent)
    {
        for (var ancestor = Xdg?.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, parent.Xdg))
            {
                return true;
            }
        }

        return false;
    }

    public (int Width, int Height) GeometrySize()
    {
        if (Xdg is { } xdg)
        {
            var geometry = xdg.Xdg.EffectiveGeometry;
            return (geometry.Width, geometry.Height);
        }

        var current = X11!.Surface?.Current;
        return (current?.Width ?? 0, current?.Height ?? 0);
    }

    public void SetActivated(bool activated)
    {
        if (Xdg is { } xdg)
        {
            xdg.SetActivated(activated);
        }
        else if (activated)
        {
            X11!.Activate();
        }
    }

    public void Maximize(int x, int y, int width, int height)
    {
        if (Xdg is { } xdg)
        {
            xdg.SetSize(width, height);
            xdg.SetMaximized(true);
        }
        else
        {
            X11!.Configure(x, y, width, height);
            X11.SetMaximized(true);
        }
    }
}
