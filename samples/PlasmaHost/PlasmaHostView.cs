using Basin;
using Basin.Effects;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace PlasmaHost;

internal sealed class PlasmaHostView
{
    public PlasmaHostView(XdgToplevelWindow xdg, SceneTree tree, SceneSurface scene)
    {
        Xdg = xdg;
        Tree = tree;
        Scene = scene;
    }

    public XdgToplevelWindow Xdg { get; }

    public SceneTree Tree { get; }

    public SceneSurface Scene { get; }

    public Surface Surface => Xdg.Surface;

    public bool Maximized { get; set; }

    public bool Minimized { get; set; }

    public bool Active { get; set; }

    public bool Resizing { get; set; }

    public Frame? Frame { get; set; }

    public DropShadowEffect? Shadow { get; set; }

    public string? IconName { get; set; }

    public ResizeAnchor? ResizeAnchor { get; set; }

    public (int Width, int Height) GeometrySize()
    {
        var geometry = Xdg.Xdg.EffectiveGeometry;
        return (geometry.Width, geometry.Height);
    }

    public bool IsTransientFor(PlasmaHostView parent)
    {
        for (var ancestor = Xdg.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, parent.Xdg))
            {
                return true;
            }
        }

        return false;
    }
}
