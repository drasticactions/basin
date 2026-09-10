using Basin;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace MauiComp;

internal sealed class ShellWindow
{
    public ShellWindow(XdgToplevelWindow window, SceneSurface scene)
    {
        Window = window;
        Scene = scene;
    }

    public XdgToplevelWindow Window { get; }

    public SceneSurface Scene { get; }

    public SceneTree Tree => Scene.Tree;

    public ShellTitlebar? Titlebar { get; set; }

    public Shell.TaskEntry? Task { get; set; }

    public bool Maximized { get; set; }

    public bool Minimized { get; set; }

    public bool Fullscreen { get; set; }

    public SceneRect? Curtain { get; set; }

    public Box Restore { get; set; }

    public bool RestorePending { get; set; }

    public Box Geometry
    {
        get
        {
            var geometry = Window.Xdg.EffectiveGeometry;
            return new Box(Tree.X + geometry.X, Tree.Y + geometry.Y, geometry.Width, geometry.Height);
        }
    }

    public string Label => Window.Title is { Length: > 0 } title
        ? title
        : Window.AppId is { Length: > 0 } appId ? appId : "window";
}
