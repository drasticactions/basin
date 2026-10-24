using Basin;
using Basin.Capabilities;
using Basin.Desktop;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.XWayland;

namespace TabWm;

internal sealed class TabWindow
{
    public TabWindow(ulong id, XdgToplevelWindow xdg, SceneTree tree, SceneSurface scene, SceneFit fit)
    {
        Id = id;
        Xdg = xdg;
        Tree = tree;
        Scene = scene;
        Fit = fit;
    }

    public TabWindow(ulong id, XWaylandWindow x11, SceneTree tree)
    {
        Id = id;
        X11 = x11;
        Tree = tree;
    }

    public ulong Id { get; }

    public XdgToplevelWindow? Xdg { get; }

    public XWaylandWindow? X11 { get; }

    public IToplevelHandle Handle => (IToplevelHandle?)Xdg ?? X11!;

    public SceneTree Tree { get; }

    public SceneSurface? Scene { get; }

    public SceneFit? Fit { get; }

    public Tab? Tab { get; set; }

    public TabFrame? Frame { get; set; }

    public SceneRect? Curtain { get; set; }

    public int X { get; private set; }

    public int Y { get; private set; }

    public RestoreGeometry Restore { get; set; }

    public bool Maximized { get; set; }

    public bool Fullscreen { get; set; }

    public bool Minimized { get; set; }

    public bool Mapped { get; set; }

    public bool HadInput { get; set; }

    public PlacementVia Via { get; set; }

    public double Scale { get; set; } = 1.0;

    public ResizeAnchor? ResizeAnchor { get; set; }

    public bool Resizing { get; set; }

    public Surface? Surface => Xdg is { } xdg ? xdg.Surface : X11?.Surface;

    public string Title => Xdg?.Title ?? X11?.Title ?? string.Empty;

    public string AppId => Handle.AppId;

    public int Pid
    {
        get
        {
            if (X11 is { } x11)
            {
                return x11.Pid;
            }

            return Xdg is { } xdg && !xdg.Surface.IsDestroyed && xdg.Surface.Resource.Client.TryGetCredentials(out var credentials)
                ? credentials.Pid
                : 0;
        }
    }

    public (int Width, int Height) Size
    {
        get
        {
            if (Xdg is { } xdg)
            {
                var geometry = xdg.Xdg.PresentedGeometry;
                return (geometry.Width, geometry.Height);
            }

            return (X11!.Width, X11.Height);
        }
    }

    public Box Geometry
    {
        get
        {
            var (width, height) = Size;
            return new Box(X, Y, width, height);
        }
    }

    public IToplevelHandle? ParentHandle => Handle.Parent;

    public void MoveTo(int x, int y)
    {
        X = x;
        Y = y;
        Tree.SetPosition(x, y);
        if (X11 is { } x11 && (x11.X != x || x11.Y != y))
        {
            x11.Configure(x, y, Math.Max(1, x11.Width), Math.Max(1, x11.Height));
        }

        UpdateFrame();
    }

    public void Follow(int x, int y)
    {
        X = x;
        Y = y;
        Tree.SetPosition(x, y);
        UpdateFrame();
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (Xdg is { } xdg)
        {
            xdg.SetSize(width, height);
            xdg.RequestConfigure();
            return;
        }

        X11!.Configure(X, Y, width, height);
    }

    public void Place(Box box)
    {
        if (X11 is { } x11)
        {
            X = box.X;
            Y = box.Y;
            Tree.SetPosition(box.X, box.Y);
            x11.Configure(box.X, box.Y, Math.Max(1, box.Width), Math.Max(1, box.Height));
            UpdateFrame();
            return;
        }

        MoveTo(box.X, box.Y);
        Resize(box.Width, box.Height);
    }

    public void SetActivated(bool activated)
    {
        if (Xdg is { } xdg)
        {
            if (!xdg.Surface.IsDestroyed)
            {
                xdg.SetActivated(activated);
            }
        }
        else if (activated)
        {
            X11!.Activate();
        }

        Frame?.SetActive(activated);
    }

    public void SetMaximizedState(bool maximized)
    {
        if (Xdg is { } xdg)
        {
            xdg.SetMaximized(maximized);
        }
        else
        {
            X11!.SetMaximized(maximized);
        }
    }

    public void SetFullscreenState(bool fullscreen)
    {
        if (Xdg is { } xdg)
        {
            xdg.SetFullscreen(fullscreen);
        }
        else
        {
            X11!.SetFullscreen(fullscreen);
        }
    }

    public void Close() => Handle.Close();

    public void UpdateFrame()
    {
        if (Frame is { } frame)
        {
            var (width, height) = Size;
            frame.Update(X, Y, width, height, Scale);
        }
    }
}
