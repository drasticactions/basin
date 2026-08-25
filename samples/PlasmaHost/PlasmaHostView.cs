using Basin;
using Basin.Capabilities;
using Basin.Effects;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace PlasmaHost;

internal sealed class PlasmaHostView
{
    public PlasmaHostView(XdgToplevelWindow xdg, SceneTree tree, SceneSurface scene)
    {
        Handle = xdg;
        Xdg = xdg;
        Tree = tree;
        Scene = scene;
    }

    public IToplevelHandle Handle { get; }

    public XdgToplevelWindow Xdg { get; }

    public SceneTree Tree { get; }

    public SceneSurface Scene { get; }

    public Surface Surface => Handle.Surface!;

    public bool Maximized { get; set; }

    public bool Minimized { get; set; }

    public bool Active { get; set; }

    public bool Resizing { get; set; }

    public PlasmaFrame? Frame { get; set; }

    public PlasmaShadowPair? Shadow { get; set; }

    public string? IconName { get; set; }

    public ResizeAnchor? ResizeAnchor { get; set; }

    public RestoreGeometry Restore { get; set; }

    public Box? StretchFrom { get; set; }

    public bool StretchFullscreen { get; set; }

    public (int Width, int Height) GeometrySize() => Handle.NaturalSize;

    public bool IsTransientFor(PlasmaHostView parent) => Handle.IsTransientFor(parent.Handle);
}
