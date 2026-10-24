using Basin.Capabilities;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.Shell.Xdg.Protocol;

namespace Basin.Shell.Nested;

/// <summary>
/// The <see cref="IWindowContent"/> of an xdg toplevel. The shell creates one for each new toplevel.
/// </summary>
public sealed class XdgContent : IWindowContent
{
    private readonly Func<XdgToplevelWindow, IWindowContent?> _resolveParent;
    private readonly Func<XdgToplevelWindow, bool> _serverDecorated;
    private readonly XdgToplevelSource? _toplevels;
    private SceneSurface? _scene;

    /// <summary>
    /// Initializes a new instance of the <see cref="XdgContent"/> class.
    /// </summary>
    /// <param name="toplevel">The toplevel to wrap.</param>
    /// <param name="resolveParent">Finds the content of a parent toplevel, or returns <see langword="null"/>.</param>
    /// <param name="serverDecorated">Decides whether the shell draws a frame for a toplevel.</param>
    /// <param name="toplevels">The toplevel source that gets the minimized state and the geometry, or <see langword="null"/>.</param>
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

    /// <summary>
    /// Gets the toplevel that this content wraps.
    /// </summary>
    public XdgToplevelWindow Toplevel { get; }

    /// <inheritdoc/>
    public string Title => Toplevel.Title;

    /// <inheritdoc/>
    public string AppId => Toplevel.AppId;

    /// <inheritdoc/>
    public Box Geometry => Toplevel.Xdg.EffectiveGeometry;

    /// <inheritdoc/>
    public int MinWidth => Toplevel.MinWidth;

    /// <inheritdoc/>
    public int MinHeight => Toplevel.MinHeight;

    /// <inheritdoc/>
    public int MaxWidth => Toplevel.MaxWidth;

    /// <inheritdoc/>
    public int MaxHeight => Toplevel.MaxHeight;

    /// <inheritdoc/>
    public IWindowContent? Parent => Toplevel.Parent is { } parent ? _resolveParent(parent) : null;

    /// <inheritdoc/>
    public Surface? Surface => Toplevel.Surface;

    /// <summary>
    /// Gets no UI surface. The value is always <see langword="null"/>, so keyboard focus goes to <see cref="Surface"/>.
    /// </summary>
    public IUISurface? UISurface => null;

    /// <inheritdoc/>
    public bool Maximized => Toplevel.HasState(XdgToplevel.State.Maximized);

    /// <inheritdoc/>
    public bool Fullscreen => Toplevel.HasState(XdgToplevel.State.Fullscreen);

    /// <inheritdoc/>
    public bool Resizing => Toplevel.HasState(XdgToplevel.State.Resizing);

    /// <inheritdoc/>
    public bool ServerDecorated => _serverDecorated(Toplevel);

    /// <summary>
    /// Gets a value indicating whether the content maps centered. It is always <see langword="false"/> for an xdg toplevel.
    /// </summary>
    public bool Centered => false;

    /// <summary>
    /// Gets the window menu, shade, above and stick entries, plus maximize, minimize and fullscreen when the toplevel advertises them.
    /// </summary>
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

    /// <inheritdoc/>
    public event Action? TitleChanged;

    /// <inheritdoc/>
    public event Action? AppIdChanged;

    /// <inheritdoc/>
    public event Action? ParentChanged;

    /// <summary>
    /// Never occurs. The shell follows decoration changes of a toplevel through the decoration managers.
    /// </summary>
    public event Action? DecorationsChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <inheritdoc/>
    public event Action? Committed;

    /// <inheritdoc/>
    public SceneNode Attach(SceneTree tree)
    {
        _scene = new SceneSurface(tree, Toplevel.Surface);
        return _scene.Tree;
    }

    /// <inheritdoc/>
    public void Detach()
    {
        _scene?.Destroy();
        _scene = null;
    }

    /// <summary>
    /// Gets the scene surface of the toplevel, or <see langword="null"/> before <see cref="Attach"/> and after <see cref="Detach"/>.
    /// </summary>
    public SceneSurface? Scene => _scene;

    /// <inheritdoc/>
    public void SetActivated(bool activated) => Toplevel.SetActivated(activated);

    /// <inheritdoc/>
    public void SetMaximized(bool maximized) => Toplevel.SetMaximized(maximized);

    /// <inheritdoc/>
    public void SetFullscreen(bool fullscreen) => Toplevel.SetFullscreen(fullscreen);

    /// <inheritdoc/>
    public void SetResizing(bool resizing) => Toplevel.SetResizing(resizing);

    /// <summary>
    /// Records the minimized state in the toplevel source. It does nothing when the content has no source.
    /// </summary>
    /// <param name="minimized"><see langword="true"/> when the window is minimized.</param>
    public void SetMinimized(bool minimized) => _toplevels?.SetMinimized(Toplevel, minimized);

    /// <summary>
    /// Does nothing. The scene order is the only stacking order of an xdg toplevel.
    /// </summary>
    public void Raise()
    {
    }

    /// <summary>
    /// Does nothing. The scene order is the only stacking order of an xdg toplevel.
    /// </summary>
    public void Lower()
    {
    }

    /// <inheritdoc/>
    public void SetTiled(ResizeEdges edges) => Toplevel.SetTiled(edges);

    /// <inheritdoc/>
    public void SetSize(int width, int height) => Toplevel.SetSize(width, height);

    /// <inheritdoc/>
    public void SetBounds(int width, int height) => Toplevel.SetBounds(width, height);

    /// <summary>
    /// Does nothing. An xdg toplevel does not know its position.
    /// </summary>
    /// <param name="x">The left edge of the content box. It is not used.</param>
    /// <param name="y">The top edge of the content box. It is not used.</param>
    public void SetPosition(int x, int y)
    {
    }

    /// <summary>
    /// Does nothing. The client learns the scale from the output and the screen enter events.
    /// </summary>
    /// <param name="scale">The new output scale. It is not used.</param>
    public void OutputScaleChanged(double scale)
    {
    }

    /// <summary>
    /// Records the frame and content boxes in the toplevel source. It does nothing when the content has no source.
    /// </summary>
    /// <param name="frame">The frame box in logical layout coordinates.</param>
    /// <param name="client">The content box in logical layout coordinates.</param>
    public void ReportGeometry(in Box frame, in Box client) => _toplevels?.SetGeometry(Toplevel, frame, client);

    /// <inheritdoc/>
    public void Close() => Toplevel.Close();

    /// <inheritdoc/>
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
