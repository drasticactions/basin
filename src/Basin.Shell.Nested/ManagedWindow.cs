using Basin.Capabilities;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Shell.Nested;

public sealed class ManagedWindow
{
    private readonly NestedShell _shell;
    private Frame? _frame;
    private ResizeAnchor? _resizeAnchor;
    private bool _resizing;
    private double _frameScale = 1.0;

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

    public IWindowContent Content { get; }

    public long Id { get; }

    public Wayland.Server.WlClient? Client { get; }

    public string? Suffix { get; set; }

    public SceneTree? Tree { get; private set; }

    public SceneNode? ContentNode { get; private set; }

    public Frame? Frame => _frame;

    public int X { get; private set; }

    public int Y { get; private set; }

    public int Workspace { get; set; }

    public bool Sticky { get; set; }

    public bool Above { get; set; }

    public bool Shaded { get; set; }

    public bool Minimized { get; set; }

    public bool Active { get; private set; }

    public bool DemandsAttention { get; set; }

    public long UserTime { get; set; }

    public TileEdge Tile { get; set; }

    public Box? Restore { get; set; }

    public string? IconName { get; set; }

    public string? ClientIcon { get; set; }

    public int IconGeneration { get; set; }

    public Box PublishedFrame { get; set; }

    public bool IsMapped => Tree is not null;

    public bool Decorated { get; private set; } = true;

    public bool Maximized => Content.Maximized;

    public bool Fullscreen => Content.Fullscreen;

    public bool IsDialog => Content.Parent is not null;

    public string Title => Content.Title is { Length: > 0 } title ? title : Content.AppId;

    public string DecoratedTitle =>
        Suffix is { } suffix ? $"{Title} — {suffix}" : Title;

    public Box Geometry => Content.Geometry;

    public Box ClientBox
    {
        get
        {
            var g = Geometry;
            return new Box(X + g.X, Y + g.Y, Math.Max(g.Width, 1), Math.Max(g.Height, 1));
        }
    }

    public FrameInsets Insets => _frame is null || Fullscreen ? default : _frame.Measure(BuildState(), _frameScale);

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

    public int TitleHeight => Insets.Top;

    public void Map(SceneTree layer, double scale, bool decorated)
    {
        Tree = new SceneTree(layer);
        Tree.SetPosition(X, Y);
        ContentNode = Content.Attach(Tree);
        _frameScale = scale;
        Decorated = decorated;
        CreateFrame();
    }

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
        _shell.Publish();
    }

    public void Unmap()
    {
        _frame?.Dispose();
        _frame = null;
        Content.Detach();
        ContentNode = null;
        Tree?.Destroy();
        Tree = null;
    }

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

    public void MoveClientTo(int x, int y)
    {
        var g = Geometry;
        MoveTo(x - g.X, y - g.Y);
    }

    public void MoveFrameTo(int x, int y)
    {
        var insets = Insets;
        MoveClientTo(x + insets.Left, y + insets.Top);
    }

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

    public void SetResizing(bool resizing)
    {
        _resizing = resizing;
        Content.SetResizing(resizing);
    }

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

    public void RefreshFrame()
    {
        if (_frame is not null && Tree is not null)
        {
            LayoutDecorations();
        }
    }

    public void ApplyShade()
    {
        if (ContentNode is { } node)
        {
            node.Enabled = !Shaded;
        }

        LayoutDecorations();
    }

    public void LayoutDecorations()
    {
        if (_frame is null)
        {
            return;
        }

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

    public void ReportGeometry()
    {
        if (Tree is null)
        {
            return;
        }

        Content.ReportGeometry(FrameBox, ClientBox);
    }

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

    public bool Owns(Surface surface) => Content.Owns(surface);

    public bool OwnsNode(SceneNode node) => _frame?.OwnsNode(node) == true;
}
