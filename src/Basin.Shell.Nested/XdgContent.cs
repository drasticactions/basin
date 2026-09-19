using Basin.Capabilities;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.Shell.Xdg.Protocol;

namespace Basin.Shell.Nested;

public sealed class XdgContent : IWindowContent
{
    private readonly Func<XdgToplevelWindow, IWindowContent?> _resolveParent;
    private readonly Func<XdgToplevelWindow, bool> _serverDecorated;
    private readonly XdgToplevelSource? _toplevels;
    private SceneSurface? _scene;

    public XdgContent(
        XdgToplevelWindow toplevel,
        Func<XdgToplevelWindow, IWindowContent?> resolveParent,
        Func<XdgToplevelWindow, bool> serverDecorated,
        XdgToplevelSource? toplevels)
    {
        Toplevel = toplevel;
        _resolveParent = resolveParent;
        _serverDecorated = serverDecorated;
        _toplevels = toplevels;
        toplevel.TitleChanged += () => TitleChanged?.Invoke();
        toplevel.AppIdChanged += () => AppIdChanged?.Invoke();
        toplevel.ParentChanged += () => ParentChanged?.Invoke();
        toplevel.Xdg.Committed += () => Committed?.Invoke();
    }

    public XdgToplevelWindow Toplevel { get; }

    public string Title => Toplevel.Title;

    public string AppId => Toplevel.AppId;

    public Box Geometry => Toplevel.Xdg.EffectiveGeometry;

    public int MinWidth => Toplevel.MinWidth;

    public int MinHeight => Toplevel.MinHeight;

    public int MaxWidth => Toplevel.MaxWidth;

    public int MaxHeight => Toplevel.MaxHeight;

    public IWindowContent? Parent => Toplevel.Parent is { } parent ? _resolveParent(parent) : null;

    public Surface? Surface => Toplevel.Surface;

    public IUISurface? UISurface => null;

    public bool Maximized => Toplevel.HasState(XdgToplevel.State.Maximized);

    public bool Fullscreen => Toplevel.HasState(XdgToplevel.State.Fullscreen);

    public bool Resizing => Toplevel.HasState(XdgToplevel.State.Resizing);

    public bool ServerDecorated => _serverDecorated(Toplevel);

    public bool Centered => false;

    public FrameCapabilities Capabilities
    {
        get
        {
            var capabilities = FrameCapabilities.WindowMenu | FrameCapabilities.Shade | FrameCapabilities.Above
                | FrameCapabilities.Stick;
            var wm = Toplevel.WmCapabilities;
            if ((wm & XdgWmCapabilities.Maximize) != 0)
            {
                capabilities |= FrameCapabilities.Maximize;
            }

            if ((wm & XdgWmCapabilities.Minimize) != 0)
            {
                capabilities |= FrameCapabilities.Minimize;
            }

            if ((wm & XdgWmCapabilities.Fullscreen) != 0)
            {
                capabilities |= FrameCapabilities.Fullscreen;
            }

            return capabilities;
        }
    }

    public event Action? TitleChanged;

    public event Action? AppIdChanged;

    public event Action? ParentChanged;

    public event Action? DecorationsChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    public event Action? Committed;

    public SceneNode Attach(SceneTree tree)
    {
        _scene = new SceneSurface(tree, Toplevel.Surface);
        return _scene.Tree;
    }

    public void Detach()
    {
        _scene?.Destroy();
        _scene = null;
    }

    public SceneSurface? Scene => _scene;

    public void SetActivated(bool activated) => Toplevel.SetActivated(activated);

    public void SetMaximized(bool maximized) => Toplevel.SetMaximized(maximized);

    public void SetFullscreen(bool fullscreen) => Toplevel.SetFullscreen(fullscreen);

    public void SetResizing(bool resizing) => Toplevel.SetResizing(resizing);

    public void SetMinimized(bool minimized) => _toplevels?.SetMinimized(Toplevel, minimized);

    public void Raise()
    {
    }

    public void Lower()
    {
    }

    public void SetTiled(ResizeEdges edges) => Toplevel.SetTiled(edges);

    public void SetSize(int width, int height) => Toplevel.SetSize(width, height);

    public void SetBounds(int width, int height) => Toplevel.SetBounds(width, height);

    public void SetPosition(int x, int y)
    {
    }

    public void OutputScaleChanged(double scale)
    {
    }

    public void ReportGeometry(in Box frame, in Box client) => _toplevels?.SetGeometry(Toplevel, frame, client);

    public void Close() => Toplevel.Close();

    public bool Owns(Surface surface)
    {
        for (var candidate = surface; candidate is not null;)
        {
            if (candidate == Toplevel.Surface)
            {
                return true;
            }

            candidate = candidate.SubsurfaceRole?.Parent;
        }

        return false;
    }
}
