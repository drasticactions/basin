using Basin;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Westonia;

internal sealed class ShellWindow
{
    public ShellWindow(XdgToplevelWindow window, SceneTree container, SceneSurface scene)
    {
        Window = window;
        Tree = container;
        Scene = scene;
    }

    public XdgToplevelWindow Window { get; }

    public SceneSurface Scene { get; }

    public SceneTree Tree { get; }

    public Surface Surface => Window.Surface;

    public ShellWindowKind Kind { get; set; } = ShellWindowKind.Normal;

    public int X { get; set; }

    public int Y { get; set; }

    public RestoreGeometry Restore { get; set; }

    public int Workspace { get; set; }

    public ResizeEdges Tiled { get; set; }

    public bool Resizing { get; set; }

    public ResizeAnchor? ResizeAnchor { get; set; }

    public bool Maximized { get; set; }

    public bool Fullscreen { get; set; }

    public IOutput? Output { get; set; }

    public ShellFrame? Frame { get; set; }

    public SceneRect? Curtain { get; set; }

    public bool Decorated => Frame is not null;

    public void MoveTo(int x, int y)
    {
        X = x;
        Y = y;
        Tree.SetPosition(x, y);
        Frame?.Update(Scale);
    }

    public double Scale { get; set; } = 1.0;

    public Box Geometry
    {
        get
        {
            var geometry = Window.Xdg.EffectiveGeometry;
            return new Box(X, Y, geometry.Width, geometry.Height);
        }
    }
}
