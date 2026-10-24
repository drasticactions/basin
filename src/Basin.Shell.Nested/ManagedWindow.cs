using Basin.Capabilities;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Shell.Nested;

/// <summary>
/// One window that <see cref="NestedShell"/> manages: its content, its frame, its scene trees and its shell state.
/// Positions are in logical layout coordinates. Change the state through the <see cref="NestedShell"/> methods, which also update visibility, focus and the panel.
/// </summary>
public sealed class ManagedWindow
{
    private readonly NestedShell _shell;
    private Frame? _frame;
    private ResizeAnchor? _resizeAnchor;
    private bool _resizing;
    private double _frameScale = 1.0;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManagedWindow"/> class. The shell creates windows. A consumer adopts content through <see cref="NestedShell.Adopt"/>.
    /// </summary>
    /// <param name="shell">The shell that manages the window.</param>
    /// <param name="content">The window content.</param>
    /// <param name="id">The shell-local window id.</param>
    /// <param name="client">The Wayland client of the content, or <see langword="null"/> for adopted content.</param>
    /// <exception cref="ArgumentNullException"><paramref name="shell"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    public ManagedWindow(NestedShell shell, IWindowContent content, long id, Wayland.Server.WlClient? client)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(content);
        _shell = shell;
        Content = content;
        Id = id;
        Client = client;
        content.TitleChanged += RefreshFrame;
        content.AppIdChanged += RefreshFrame;
        content.ParentChanged += RefreshFrame;
        content.DecorationsChanged += OnDecorationsChanged;
    }

    /// <summary>
    /// Gets the content of the window.
    /// </summary>
    public IWindowContent Content { get; }

    internal Action? AdoptedCommitted { get; set; }

    /// <summary>
    /// Gets the shell-local window id. It is unique for the life of the shell.
    /// </summary>
    public long Id { get; }

    /// <summary>
    /// Gets the Wayland client of the content, or <see langword="null"/> for adopted content.
    /// </summary>
    public Wayland.Server.WlClient? Client { get; }

    /// <summary>
    /// Gets or sets the text that <see cref="DecoratedTitle"/> appends to the title, or <see langword="null"/> for none.
    /// </summary>
    public string? Suffix { get; set; }

    /// <summary>
    /// Gets the window tree, which holds the frame and the content node. It is <see langword="null"/> while the window is not mapped.
    /// </summary>
    public SceneTree? Tree { get; private set; }

    /// <summary>
    /// Gets the content node from <see cref="IWindowContent.Attach"/>, or <see langword="null"/> while the window is not mapped.
    /// </summary>
    public SceneNode? ContentNode { get; private set; }

    /// <summary>
    /// Gets the tree in <see cref="ShellLayers.Menu"/> that holds the popups of the window, or <see langword="null"/> while the window is not mapped.
    /// </summary>
    public SceneTree? PopupTree { get; private set; }

    /// <summary>
    /// Gets the id of the window in <see cref="IToplevelModel"/>. It is zero until <see cref="NestedShell.ToplevelIdOf"/> finds one.
    /// </summary>
    public ulong ToplevelId { get; internal set; }

    /// <summary>
    /// Gets the frame, or <see langword="null"/> while the window is not mapped or not decorated.
    /// </summary>
    public Frame? Frame => _frame;

    /// <summary>
    /// Gets the horizontal position of the window tree origin. The content box starts at this value plus the geometry offset.
    /// </summary>
    public int X { get; private set; }

    /// <summary>
    /// Gets the vertical position of the window tree origin. The content box starts at this value plus the geometry offset.
    /// </summary>
    public int Y { get; private set; }

    /// <summary>
    /// Gets the zero-based workspace index. <see cref="NestedShell.MoveToWorkspace"/> changes it.
    /// </summary>
    public int Workspace { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the window shows on every workspace. <see cref="NestedShell.SetSticky"/> changes it.
    /// </summary>
    public bool Sticky { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the window stays in <see cref="ShellLayers.Above"/>. <see cref="NestedShell.SetAbove"/> changes it.
    /// </summary>
    public bool Above { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the window is rolled up to its title bar. <see cref="NestedShell.SetShaded"/> changes it.
    /// </summary>
    public bool Shaded { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the window is minimized. <see cref="NestedShell.SetMinimized"/> changes it.
    /// </summary>
    public bool Minimized { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the window has focus, as the last <see cref="SetActive"/> call set it.
    /// </summary>
    public bool Active { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the window mapped without focus. Activation clears it.
    /// </summary>
    public bool DemandsAttention { get; internal set; }

    /// <summary>
    /// Gets the time of the last focus of the window, in <see cref="Environment.TickCount64"/> milliseconds, or zero when unknown.
    /// The focus-stealing rule compares it.
    /// </summary>
    public long UserTime { get; internal set; }

    /// <summary>
    /// Gets the edge that the window is tiled against. <see cref="NestedShell.SetTile"/> changes it.
    /// </summary>
    public TileEdge Tile { get; internal set; }

    /// <summary>
    /// Gets the frame box that the window goes back to when it leaves the maximized, tiled or fullscreen state, or <see langword="null"/>.
    /// </summary>
    public Box? Restore { get; internal set; }

    /// <summary>
    /// Gets or sets the icon that the frame and the panel show: the answer of <see cref="NestedShell.ResolveIcon"/>, or <see cref="ClientIcon"/> when no resolver is set.
    /// </summary>
    public string? IconName { get; set; }

    /// <summary>
    /// Gets or sets the icon name that the client gave, or <see langword="null"/>.
    /// </summary>
    public string? ClientIcon { get; set; }

    /// <summary>
    /// Gets the counter that the shell increments for each icon request. The shell drops an answer to an older request.
    /// </summary>
    public int IconGeneration { get; internal set; }

    /// <summary>
    /// Gets the frame box that the last <see cref="NestedShell.SnapshotWindows"/> reported.
    /// </summary>
    public Box PublishedFrame { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the window is mapped.
    /// </summary>
    public bool IsMapped => Tree is not null;

    /// <summary>
    /// Gets a value indicating whether the shell draws a frame for the window.
    /// </summary>
    public bool Decorated { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the content is maximized.
    /// </summary>
    public bool Maximized => Content.Maximized;

    /// <summary>
    /// Gets a value indicating whether the content is fullscreen.
    /// </summary>
    public bool Fullscreen => Content.Fullscreen;

    /// <summary>
    /// Gets a value indicating whether the content has a parent.
    /// </summary>
    public bool IsDialog => Content.Parent is not null;

    /// <summary>
    /// Gets the content title, or the app id when the title is empty.
    /// </summary>
    public string Title => Content.Title is { Length: > 0 } title ? title : Content.AppId;

    /// <summary>
    /// Gets the title that the frame shows: <see cref="Title"/>, then a dash and <see cref="Suffix"/> when a suffix is set.
    /// </summary>
    public string DecoratedTitle =>
        Suffix is { } suffix ? $"{Title} — {suffix}" : Title;

    /// <summary>
    /// Gets the content geometry relative to the window tree origin.
    /// </summary>
    public Box Geometry => Content.Geometry;

    /// <summary>
    /// Gets the content box in logical layout coordinates. Its width and height are at least 1.
    /// </summary>
    public Box ClientBox
    {
        get
        {
            var g = Geometry;
            return new Box(X + g.X, Y + g.Y, Math.Max(g.Width, 1), Math.Max(g.Height, 1));
        }
    }

    /// <summary>
    /// Gets the space that the frame adds on each side of the content. It is zero without a frame or in fullscreen.
    /// </summary>
    public FrameInsets Insets => _frame is null || Fullscreen ? default : _frame.Measure(BuildState(), _frameScale);

    /// <summary>
    /// Gets the frame box in logical layout coordinates: <see cref="ClientBox"/> grown by <see cref="Insets"/>.
    /// </summary>
    public Box FrameBox
    {
        get
        {
            var client = ClientBox;
            var insets = Insets;
            return new Box(
                client.X - insets.Left,
                client.Y - insets.Top,
                client.Width + insets.Left + insets.Right,
                client.Height + insets.Top + insets.Bottom);
        }
    }

    /// <summary>
    /// Gets the height of the frame above the content, title bar and top border together.
    /// </summary>
    public int TitleHeight => Insets.Top;

    /// <summary>
    /// Creates the window tree in a layer, attaches the content and creates the frame when the window is decorated.
    /// </summary>
    /// <param name="layer">The layer that holds the window tree.</param>
    /// <param name="scale">The output scale for the frame.</param>
    /// <param name="decorated"><see langword="true"/> when the shell draws a frame.</param>
    public void Map(SceneTree layer, double scale, bool decorated)
    {
        Tree = new SceneTree(layer);
        Tree.SetPosition(X, Y);
        ContentNode = Content.Attach(Tree);
        _frameScale = scale;
        Decorated = decorated;
        CreateFrame();
    }

    /// <summary>
    /// Adds or removes the frame and keeps the content box where it is. It does nothing when the value does not change.
    /// </summary>
    /// <param name="decorated"><see langword="true"/> to draw a frame.</param>
    public void SetDecorated(bool decorated)
    {
        if (Decorated == decorated)
        {
            return;
        }

        var client = ClientBox;
        Decorated = decorated;
        if (!decorated)
        {
            _frame?.Dispose();
            _frame = null;
            ReportGeometry();
        }
        else
        {
            CreateFrame();
        }

        MoveClientTo(client.X, client.Y);
    }

    private void OnDecorationsChanged()
    {
        if (!IsMapped || Decorated == Content.ServerDecorated)
        {
            return;
        }

        SetDecorated(Content.ServerDecorated);
        _shell.FitStateGeometry(this);
        _shell.Publish();
    }

    /// <summary>
    /// Disposes the frame, detaches the content and destroys the window tree and the popup tree.
    /// </summary>
    public void Unmap()
    {
        _frame?.Dispose();
        _frame = null;
        Content.Detach();
        ContentNode = null;
        Tree?.Destroy();
        Tree = null;
        PopupTree?.Destroy();
        PopupTree = null;
    }

    internal void CreatePopupTree(SceneTree layer) => PopupTree ??= new SceneTree(layer);

    /// <summary>
    /// Creates the frame from the current shell theme. It does nothing while the window is not mapped, not decorated, or already has a frame.
    /// </summary>
    public void CreateFrame()
    {
        if (Tree is null || _frame is not null || !Decorated)
        {
            return;
        }

        _frame = new Frame(_shell.UIHost, _shell.Frames.CreateRenderer(), Tree)
        {
            MenuLayer = _shell.Layers.Menu,
            TouchSlop = 8,
        };
        _frame.Requested += action => _shell.OnFrameAction(this, action);
        _frame.Faulted += error => _shell.OnFrameFaulted(this, error);
        ContentNode?.RaiseToTop();
        LayoutDecorations();
        ReportGeometry();
    }

    /// <summary>
    /// Replaces the frame with one from the current shell theme. It does nothing when the window has no frame.
    /// </summary>
    public void RebuildFrame()
    {
        if (_frame is null)
        {
            return;
        }

        _frame.Dispose();
        _frame = null;
        CreateFrame();
    }

    /// <summary>
    /// Moves the window tree origin and tells the content its new position.
    /// </summary>
    /// <param name="x">The horizontal position of the tree origin.</param>
    /// <param name="y">The vertical position of the tree origin.</param>
    public void MoveTo(int x, int y)
    {
        X = x;
        Y = y;
        Tree?.SetPosition(x, y);
        var g = Geometry;
        Content.SetPosition(x + g.X, y + g.Y);
        ReportGeometry();
        if (_frame is not null && _shell.Scale != _frameScale)
        {
            LayoutDecorations();
        }
    }

    /// <summary>
    /// Moves the window so that the content box starts at a position.
    /// </summary>
    /// <param name="x">The left edge of the content box.</param>
    /// <param name="y">The top edge of the content box.</param>
    public void MoveClientTo(int x, int y)
    {
        var g = Geometry;
        MoveTo(x - g.X, y - g.Y);
    }

    /// <summary>
    /// Moves the window so that the frame box starts at a position.
    /// </summary>
    /// <param name="x">The left edge of the frame box.</param>
    /// <param name="y">The top edge of the frame box.</param>
    public void MoveFrameTo(int x, int y)
    {
        var insets = Insets;
        MoveClientTo(x + insets.Left, y + insets.Top);
    }

    /// <summary>
    /// Asks the content for a new size. When <paramref name="edges"/> includes the left or top edge, the opposite edges stay in place as the content commits.
    /// Otherwise the window moves to the box position at once.
    /// </summary>
    /// <param name="client">The content box to reach.</param>
    /// <param name="edges">The edges that the resize moves.</param>
    public void ResizeClientTo(in Box client, ResizeEdges edges)
    {
        _resizeAnchor = ResizeAnchor.For(edges, client.X, client.Y, client.Width, client.Height);
        if (_resizeAnchor is null)
        {
            MoveClientTo(client.X, client.Y);
        }

        Content.SetSize(client.Width, client.Height);
        ReportGeometry();
    }

    /// <summary>
    /// Starts or ends an interactive resize. While it is in progress, each commit keeps the anchored edges in place.
    /// </summary>
    /// <param name="resizing"><see langword="true"/> while the resize is in progress.</param>
    public void SetResizing(bool resizing)
    {
        _resizing = resizing;
        Content.SetResizing(resizing);
    }

    /// <summary>
    /// Applies a content commit: it keeps the resize anchor, lays out the frame and reports the geometry.
    /// </summary>
    public void OnCommitted()
    {
        ApplyResizeAnchor();
        LayoutDecorations();
        ReportGeometry();
    }

    private void ApplyResizeAnchor()
    {
        if (_resizeAnchor is not { } anchor)
        {
            return;
        }

        var client = ClientBox;
        var (x, y) = anchor.PositionFor(client.Width, client.Height, client.X, client.Y);
        if (x != client.X || y != client.Y)
        {
            MoveClientTo(x, y);
        }

        _resizeAnchor = ResizeAnchor.AfterCommit(_resizeAnchor, _resizing);
    }

    /// <summary>
    /// Sets the focus state, tells the content and redraws the frame. Activation clears <see cref="DemandsAttention"/>.
    /// </summary>
    /// <param name="active"><see langword="true"/> when the window has focus.</param>
    public void SetActive(bool active)
    {
        if (Active == active)
        {
            return;
        }

        Active = active;
        Content.SetActivated(active);
        if (active)
        {
            DemandsAttention = false;
        }

        RefreshFrame();
    }

    /// <summary>
    /// Lays out and commits the frame again from the current state. It does nothing without a frame.
    /// </summary>
    public void RefreshFrame()
    {
        if (_frame is not null && Tree is not null)
        {
            LayoutDecorations();
        }
    }

    /// <summary>
    /// Hides or shows the content node to match <see cref="Shaded"/>, then lays out the frame.
    /// </summary>
    public void ApplyShade()
    {
        if (ContentNode is { } node)
        {
            node.Enabled = !Shaded;
        }

        LayoutDecorations();
    }

    /// <summary>
    /// Configures and commits the frame for the current geometry, scale and state. The frame hides when the content has no size or is fullscreen.
    /// </summary>
    public void LayoutDecorations()
    {
        if (_frame is null)
        {
            return;
        }

        _frame.Window = Content.Surface;

        var geometry = Geometry;
        var visible = geometry.Width > 0 && geometry.Height > 0 && !Fullscreen;
        _frame.Visible = visible;
        if (!visible)
        {
            return;
        }

        var scale = _shell.Scale;
        _frameScale = scale;
        if (!_frame.HasPendingFor(geometry, scale))
        {
            _frame.Configure(geometry, scale, BuildState());
        }

        _frame.Commit();
    }

    /// <summary>
    /// Sends the frame and content boxes to the content. It does nothing while the window is not mapped.
    /// </summary>
    public void ReportGeometry()
    {
        if (Tree is null)
        {
            return;
        }

        Content.ReportGeometry(FrameBox, ClientBox);
    }

    /// <summary>
    /// Builds the frame state from the window state.
    /// </summary>
    /// <returns>The state that the frame draws.</returns>
    public FrameState BuildState() => new()
    {
        Title = DecoratedTitle,
        AppId = Content.AppId,
        Icon = new FrameIcon(IconName, null),
        Active = Active,
        Maximized = Maximized,
        Fullscreen = Fullscreen,
        Resizing = Content.Resizing,
        Capabilities = Content.Capabilities,
        Kind = Content.Parent is null ? FrameKind.Normal : FrameKind.Dialog,
        Tiled = Tile switch
        {
            TileEdge.Left => FrameTiling.Left,
            TileEdge.Right => FrameTiling.Right,
            _ => FrameTiling.None,
        },
        Shaded = Shaded,
        Above = Above,
        Sticky = Sticky,
    };

    /// <summary>
    /// Finds out whether a surface belongs to the content of the window.
    /// </summary>
    /// <param name="surface">The surface to test.</param>
    /// <returns><see langword="true"/> when the content owns the surface.</returns>
    public bool Owns(Surface surface) => Content.Owns(surface);

    /// <summary>
    /// Finds out whether a scene node is part of the frame.
    /// </summary>
    /// <param name="node">The node to test.</param>
    /// <returns><see langword="true"/> when the frame owns the node.</returns>
    public bool OwnsNode(SceneNode node) => _frame?.OwnsNode(node) == true;
}
