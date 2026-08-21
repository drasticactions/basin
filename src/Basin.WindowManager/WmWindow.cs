using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

public sealed class WmWindow : IRenderWindow
{
    private readonly RiverWindowManager _wm;
    private readonly RiverWindowV1 _proxy;
    private WmNode? _node;

    private string? _pendingAppId;
    private bool _appIdChanged;
    private string? _pendingTitle;
    private bool _titleChanged;
    private RiverWindowV1? _pendingParent;
    private bool _parentChanged;
    private DimensionsHint _pendingSizeHint;
    private bool _sizeHintChanged;
    private DecorationHint _pendingDecorationHint;
    private bool _decorationHintChanged;
    private Size _pendingDimensions;
    private bool _dimensionsChanged;
    private PresentationMode _pendingPresentationHint;
    private bool _presentationHintChanged;
    private uint _pendingCaptureSessions;
    private bool _captureSessionsChanged;

    private Notifications _notifications;
    private RiverOutputV1? _fullscreenOutput;
    private Point _windowMenuAt;
    private RiverSeatV1? _moveSeat;
    private RiverSeatV1? _resizeSeat;
    private Edges _resizeEdges;

    internal WmWindow(RiverWindowManager wm, RiverWindowV1 proxy)
    {
        _wm = wm;
        _proxy = proxy;

        proxy.AppId += (_, e) => (_pendingAppId, _appIdChanged) = (e.AppId, true);
        proxy.Title += (_, e) => (_pendingTitle, _titleChanged) = (e.Title, true);
        proxy.Parent += (_, e) => (_pendingParent, _parentChanged) = (e.Parent, true);
        proxy.DimensionsHint += (_, e) =>
        {
            _pendingSizeHint = new DimensionsHint(
                new Size(e.MinWidth, e.MinHeight),
                new Size(e.MaxWidth, e.MaxHeight));
            _sizeHintChanged = true;
        };
        proxy.DecorationHintEvent += (_, e) =>
        {
            _pendingDecorationHint = (DecorationHint)(uint)e.Hint;
            _decorationHintChanged = true;
        };
        proxy.Dimensions += (_, e) =>
        {
            _pendingDimensions = new Size(e.Width, e.Height);
            _dimensionsChanged = true;
        };
        proxy.UnreliablePid += (_, e) => UnreliablePid = e.UnreliablePid;
        proxy.Identifier += (_, e) => Identifier = e.Identifier;
        proxy.PresentationHint += (_, e) =>
        {
            _pendingPresentationHint = (PresentationMode)e.Hint;
            _presentationHintChanged = true;
        };
        proxy.CaptureSessions += (_, e) =>
        {
            _pendingCaptureSessions = e.Count;
            _captureSessionsChanged = true;
        };

        proxy.MaximizeRequested += (_, _) => _notifications |= Notifications.Maximize;
        proxy.UnmaximizeRequested += (_, _) => _notifications |= Notifications.Unmaximize;
        proxy.MinimizeRequested += (_, _) => _notifications |= Notifications.Minimize;
        proxy.ExitFullscreenRequested += (_, _) => _notifications |= Notifications.ExitFullscreen;
        proxy.FullscreenRequested += (_, e) =>
        {
            _fullscreenOutput = e.Output;
            _notifications |= Notifications.Fullscreen;
        };
        proxy.ShowWindowMenuRequested += (_, e) =>
        {
            _windowMenuAt = new Point(e.X, e.Y);
            _notifications |= Notifications.WindowMenu;
        };
        proxy.PointerMoveRequested += (_, e) =>
        {
            _moveSeat = e.Seat;
            _notifications |= Notifications.PointerMove;
        };
        proxy.PointerResizeRequested += (_, e) =>
        {
            _resizeSeat = e.Seat;
            _resizeEdges = (Edges)e.Edges;
            _notifications |= Notifications.PointerResize;
        };
        proxy.Closed += (_, _) =>
        {
            IsClosed = true;
            _notifications |= Notifications.Closed;
            _wm.OnWindowClosed(this);
        };
    }

    public string? AppId { get; private set; }

    public string? Title { get; private set; }

    public WmWindow? Parent { get; private set; }

    public string? Identifier { get; private set; }

    public int UnreliablePid { get; private set; }

    public DimensionsHint SizeHint { get; private set; }

    public DecorationHint DecorationHint { get; private set; } = DecorationHint.NoPreference;

    public Size Dimensions { get; private set; }

    public PresentationMode PresentationHint { get; private set; }

    public int CaptureSessions { get; private set; }

    public WmNode Node => _node ??= new WmNode(_wm, _proxy.GetNode());

    public bool IsClosed { get; private set; }

    public event Action? MaximizeRequested;

    public event Action? UnmaximizeRequested;

    public event Action<WmOutput?>? FullscreenRequested;

    public event Action? ExitFullscreenRequested;

    public event Action? MinimizeRequested;

    public event Action<Point>? ShowWindowMenuRequested;

    public event Action<WmSeat>? PointerMoveRequested;

    public event Action<WmSeat, Edges>? PointerResizeRequested;

    public event Action? Closed;

    public void ProposeDimensions(int width, int height)
    {
        _wm.EnsureManage(nameof(ProposeDimensions));
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        _proxy.ProposeDimensions(width, height);
    }

    public void ProposeDimensions(Size size) => ProposeDimensions(size.Width, size.Height);

    public void SetDimensionBounds(int maxWidth, int maxHeight)
    {
        _wm.EnsureManage(nameof(SetDimensionBounds));
        _wm.RequireVersion(4, "set_dimension_bounds");
        ArgumentOutOfRangeException.ThrowIfNegative(maxWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(maxHeight);
        _proxy.SetDimensionBounds(maxWidth, maxHeight);
    }

    public void Close()
    {
        _wm.EnsureManage(nameof(Close));
        _proxy.Close();
    }

    public void UseClientSideDecorations()
    {
        _wm.EnsureManage(nameof(UseClientSideDecorations));
        _proxy.UseCsd();
    }

    public void UseServerSideDecorations()
    {
        _wm.EnsureManage(nameof(UseServerSideDecorations));
        _proxy.UseSsd();
    }

    public void SetTiled(Edges edges)
    {
        _wm.EnsureManage(nameof(SetTiled));
        _proxy.SetTiled((RiverWindowV1.Edges)edges);
    }

    public void SetCapabilities(WindowCapabilities capabilities)
    {
        _wm.EnsureManage(nameof(SetCapabilities));
        _proxy.SetCapabilities((RiverWindowV1.Capabilities)capabilities);
    }

    public void InformMaximized(bool maximized)
    {
        _wm.EnsureManage(nameof(InformMaximized));
        if (maximized)
        {
            _proxy.InformMaximized();
        }
        else
        {
            _proxy.InformUnmaximized();
        }
    }

    public void InformFullscreen(bool fullscreen)
    {
        _wm.EnsureManage(nameof(InformFullscreen));
        if (fullscreen)
        {
            _proxy.InformFullscreen();
        }
        else
        {
            _proxy.InformNotFullscreen();
        }
    }

    public void InformResizing(bool resizing)
    {
        _wm.EnsureManage(nameof(InformResizing));
        if (resizing)
        {
            _proxy.InformResizeStart();
        }
        else
        {
            _proxy.InformResizeEnd();
        }
    }

    public void Fullscreen(WmOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _wm.EnsureManage(nameof(Fullscreen));
        _proxy.Fullscreen(output.Proxy);
    }

    public void ExitFullscreen()
    {
        _wm.EnsureManage(nameof(ExitFullscreen));
        _proxy.ExitFullscreen();
    }

    public void Show()
    {
        _wm.EnsureRender(nameof(Show));
        _proxy.Show();
    }

    public void Hide()
    {
        _wm.EnsureRender(nameof(Hide));
        _proxy.Hide();
    }

    public void SetBorders(Edges edges, int width, WmColor color)
    {
        _wm.EnsureRender(nameof(SetBorders));
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        _proxy.SetBorders((RiverWindowV1.Edges)edges, width, color.R, color.G, color.B, color.A);
    }

    public void ClearBorders() => SetBorders(Edges.None, 0, WmColor.Transparent);

    public void SetClipBox(Rect box)
    {
        _wm.EnsureRender(nameof(SetClipBox));
        _wm.RequireVersion(2, "set_clip_box");
        ArgumentOutOfRangeException.ThrowIfNegative(box.Width);
        ArgumentOutOfRangeException.ThrowIfNegative(box.Height);
        _proxy.SetClipBox(box.X, box.Y, box.Width, box.Height);
    }

    public void SetContentClipBox(Rect box)
    {
        _wm.EnsureRender(nameof(SetContentClipBox));
        _wm.RequireVersion(3, "set_content_clip_box");
        ArgumentOutOfRangeException.ThrowIfNegative(box.Width);
        ArgumentOutOfRangeException.ThrowIfNegative(box.Height);
        _proxy.SetContentClipBox(box.X, box.Y, box.Width, box.Height);
    }

    public WmDecoration CreateDecorationAbove(Wayland.WlSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        WmThreadAffinity.Assert();
        return new WmDecoration(_wm, _proxy.GetDecorationAbove(surface));
    }

    public WmDecoration CreateDecorationBelow(Wayland.WlSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        WmThreadAffinity.Assert();
        return new WmDecoration(_wm, _proxy.GetDecorationBelow(surface));
    }

    public override string ToString() =>
        $"window '{AppId ?? "?"}' \"{Title ?? string.Empty}\" {Dimensions.Width}x{Dimensions.Height}";

    internal RiverWindowV1 Proxy => _proxy;

    internal RiverWindowV1 ProxyForRemoval => _proxy;

    internal void ApplyPending()
    {
        if (_appIdChanged)
        {
            (AppId, _appIdChanged) = (_pendingAppId, false);
        }

        if (_titleChanged)
        {
            (Title, _titleChanged) = (_pendingTitle, false);
        }

        if (_parentChanged)
        {
            Parent = _wm.Resolve(_pendingParent);
            _parentChanged = false;
        }

        if (_sizeHintChanged)
        {
            (SizeHint, _sizeHintChanged) = (_pendingSizeHint, false);
        }

        if (_decorationHintChanged)
        {
            (DecorationHint, _decorationHintChanged) = (_pendingDecorationHint, false);
        }

        if (_dimensionsChanged)
        {
            (Dimensions, _dimensionsChanged) = (_pendingDimensions, false);
        }

        if (_presentationHintChanged)
        {
            (PresentationHint, _presentationHintChanged) = (_pendingPresentationHint, false);
        }

        if (_captureSessionsChanged)
        {
            CaptureSessions = (int)_pendingCaptureSessions;
            _captureSessionsChanged = false;
        }
    }

    internal bool HasPendingNotifications => _notifications != Notifications.None;

    internal void FirePending()
    {
        if (_notifications == Notifications.None)
        {
            return;
        }

        var pending = _notifications;
        _notifications = Notifications.None;

        if ((pending & Notifications.Maximize) != 0)
        {
            MaximizeRequested?.Invoke();
        }

        if ((pending & Notifications.Unmaximize) != 0)
        {
            UnmaximizeRequested?.Invoke();
        }

        if ((pending & Notifications.Minimize) != 0)
        {
            MinimizeRequested?.Invoke();
        }

        if ((pending & Notifications.Fullscreen) != 0)
        {
            FullscreenRequested?.Invoke(_wm.Resolve(_fullscreenOutput));
            _fullscreenOutput = null;
        }

        if ((pending & Notifications.ExitFullscreen) != 0)
        {
            ExitFullscreenRequested?.Invoke();
        }

        if ((pending & Notifications.WindowMenu) != 0)
        {
            ShowWindowMenuRequested?.Invoke(_windowMenuAt);
        }

        if ((pending & Notifications.PointerMove) != 0 && _wm.Resolve(_moveSeat) is { } moveSeat)
        {
            PointerMoveRequested?.Invoke(moveSeat);
            _moveSeat = null;
        }

        if ((pending & Notifications.PointerResize) != 0 && _wm.Resolve(_resizeSeat) is { } resizeSeat)
        {
            PointerResizeRequested?.Invoke(resizeSeat, _resizeEdges);
            _resizeSeat = null;
        }

        if ((pending & Notifications.Closed) != 0)
        {
            Closed?.Invoke();
        }
    }

    internal void DestroyProxy()
    {
        _node?.DestroyProxy();
        _node = null;
        if (!_proxy.IsDestroyed)
        {
            _proxy.Destroy();
        }
    }

    [Flags]
    private enum Notifications
    {
        None = 0,
        Maximize = 1,
        Unmaximize = 2,
        Minimize = 4,
        Fullscreen = 8,
        ExitFullscreen = 16,
        WindowMenu = 32,
        PointerMove = 64,
        PointerResize = 128,
        Closed = 256,
    }
}
