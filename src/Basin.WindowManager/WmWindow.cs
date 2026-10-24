using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

/// <summary>
/// A client window that the compositor hands to the manager. Its properties hold the state from the start of the current sequence, so a callback never sees a half-updated window.
/// </summary>
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
    private WindowCapabilities? _sentCapabilities;
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

    /// <summary>
    /// Gets the application id that the client set, or <see langword="null"/> when it set none.
    /// </summary>
    public string? AppId { get; private set; }

    /// <summary>
    /// Gets the title that the client set, or <see langword="null"/> when it set none.
    /// </summary>
    public string? Title { get; private set; }

    /// <summary>
    /// Gets the window that this window names as its parent, for example the owner of a dialog. It is <see langword="null"/> for a window with no parent.
    /// </summary>
    public WmWindow? Parent { get; private set; }

    /// <summary>
    /// Gets the compositor's unique identifier for the window, which is never reused. It is <see langword="null"/> before protocol version 4.
    /// </summary>
    public string? Identifier { get; private set; }

    /// <summary>
    /// Gets the process id of the client that created the window, or zero when unknown. A pid can be reused, so do not use it for security decisions.
    /// </summary>
    public int UnreliablePid { get; private set; }

    /// <summary>
    /// Gets the minimum and maximum dimensions that the client prefers. The manager can propose dimensions outside this range.
    /// </summary>
    public DimensionsHint SizeHint { get; private set; }

    /// <summary>
    /// Gets the decoration styles that the client supports and prefers. It is <see cref="DecorationHint.NoPreference"/> until the client sends a hint.
    /// </summary>
    public DecorationHint DecorationHint { get; private set; } = DecorationHint.NoPreference;

    /// <inheritdoc/>
    public Size Dimensions { get; private set; }

    /// <summary>
    /// Gets the presentation mode that the client prefers. It is <see cref="PresentationMode.Vsync"/> until the client sends a hint.
    /// </summary>
    public PresentationMode PresentationHint { get; private set; }

    /// <summary>
    /// Gets the number of screen capture sessions that record this window.
    /// </summary>
    public int CaptureSessions { get; private set; }

    /// <inheritdoc/>
    public WmNode Node => _node ??= new WmNode(_wm, _proxy.GetNode());

    /// <summary>
    /// Gets a value indicating whether the window closed. It becomes true when the event arrives, before <see cref="Closed"/> fires.
    /// </summary>
    public bool IsClosed { get; private set; }

    /// <summary>
    /// Occurs at the start of a sequence when the client asked to be maximized. The manager can ignore the request.
    /// </summary>
    public event Action? MaximizeRequested;

    /// <summary>
    /// Occurs at the start of a sequence when the client asked to leave the maximized state.
    /// </summary>
    public event Action? UnmaximizeRequested;

    /// <summary>
    /// Occurs at the start of a sequence when the client asked to be fullscreen. The argument is the output it named, or <see langword="null"/> when it named none.
    /// </summary>
    public event Action<WmOutput?>? FullscreenRequested;

    /// <summary>
    /// Occurs at the start of a sequence when the client asked to leave fullscreen.
    /// </summary>
    public event Action? ExitFullscreenRequested;

    /// <summary>
    /// Occurs at the start of a sequence when the client asked to be minimized.
    /// </summary>
    public event Action? MinimizeRequested;

    /// <summary>
    /// Occurs at the start of a sequence when the client asked for a window menu. The argument is the menu position relative to the window's top-left corner.
    /// </summary>
    public event Action<Point>? ShowWindowMenuRequested;

    /// <summary>
    /// Occurs at the start of a sequence when the client asked for an interactive move on a seat. It does not fire when the seat is gone.
    /// </summary>
    public event Action<WmSeat>? PointerMoveRequested;

    /// <summary>
    /// Occurs at the start of a sequence when the client asked for an interactive resize on a seat, with the edges to move. It does not fire when the seat is gone.
    /// </summary>
    public event Action<WmSeat, Edges>? PointerResizeRequested;

    /// <summary>
    /// Occurs at the start of the next sequence after the window closed. The library destroys the window when that manage sequence ends.
    /// </summary>
    public event Action? Closed;

    /// <summary>
    /// Proposes a content size. The client can take another size, and the size it takes arrives in <see cref="Dimensions"/> in a later render sequence.
    /// </summary>
    /// <param name="width">The width in logical pixels. Zero lets the client choose.</param>
    /// <param name="height">The height in logical pixels. Zero lets the client choose.</param>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is negative.</exception>
    public void ProposeDimensions(int width, int height)
    {
        _wm.EnsureManage(nameof(ProposeDimensions));
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        _proxy.ProposeDimensions(width, height);
    }

    /// <summary>
    /// Proposes a content size from a <see cref="Size"/>.
    /// </summary>
    /// <param name="size">The size in logical pixels. A zero component lets the client choose it.</param>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A component is negative.</exception>
    public void ProposeDimensions(Size size) => ProposeDimensions(size.Width, size.Height);

    /// <summary>
    /// Recommends a maximum content size to the client. The client can ignore it.
    /// </summary>
    /// <param name="maxWidth">The largest width in logical pixels. Zero removes the bound.</param>
    /// <param name="maxHeight">The largest height in logical pixels. Zero removes the bound.</param>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    /// <exception cref="NotSupportedException">The bound protocol version is lower than 4.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A bound is negative.</exception>
    public void SetDimensionBounds(int maxWidth, int maxHeight)
    {
        _wm.EnsureManage(nameof(SetDimensionBounds));
        _wm.RequireVersion(4, "set_dimension_bounds");
        ArgumentOutOfRangeException.ThrowIfNegative(maxWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(maxHeight);
        _proxy.SetDimensionBounds(maxWidth, maxHeight);
    }

    /// <summary>
    /// Asks the client to close the window. The client can ignore it or wait, and <see cref="Closed"/> fires only when it does close.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public void Close()
    {
        _wm.EnsureManage(nameof(Close));
        _proxy.Close();
    }

    /// <summary>
    /// Tells the client to draw its own titlebar and borders. This is the default.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public void UseClientSideDecorations()
    {
        _wm.EnsureManage(nameof(UseClientSideDecorations));
        _proxy.UseCsd();
    }

    /// <summary>
    /// Tells the client that the manager draws the titlebar and borders.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public void UseServerSideDecorations()
    {
        _wm.EnsureManage(nameof(UseServerSideDecorations));
        _proxy.UseSsd();
    }

    /// <summary>
    /// Tells the client which of its edges touch other elements of a tiled layout, so it can drop shadows there.
    /// </summary>
    /// <param name="edges">The tiled edges. <see cref="Edges.None"/> tells the client it is not tiled.</param>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public void SetTiled(Edges edges)
    {
        _wm.EnsureManage(nameof(SetTiled));
        _proxy.SetTiled((RiverWindowV1.Edges)edges);
    }

    /// <summary>
    /// Tells the client which window actions the manager supports. It sends nothing when the value equals the last one sent, so a manager can call it in every manage sequence.
    /// </summary>
    /// <param name="capabilities">The supported actions. A client that never gets this request assumes all of them.</param>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public void SetCapabilities(WindowCapabilities capabilities)
    {
        _wm.EnsureManage(nameof(SetCapabilities));
        if (_sentCapabilities == capabilities)
        {
            return;
        }

        _sentCapabilities = capabilities;
        _proxy.SetCapabilities((RiverWindowV1.Capabilities)capabilities);
    }

    /// <summary>
    /// Tells the client whether it is maximized. This changes no size or position.
    /// </summary>
    /// <param name="maximized"><see langword="true"/> for maximized, <see langword="false"/> for not maximized.</param>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
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

    /// <summary>
    /// Tells the client whether it is fullscreen. This changes no size and does not make the window the only one shown. <see cref="Fullscreen(WmOutput)"/> does that.
    /// </summary>
    /// <param name="fullscreen"><see langword="true"/> for fullscreen, <see langword="false"/> for not fullscreen.</param>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
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

    /// <summary>
    /// Tells the client that an interactive resize starts or ends. The manager still sets the size and position.
    /// </summary>
    /// <param name="resizing"><see langword="true"/> at the start of the resize, <see langword="false"/> at the end.</param>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
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

    /// <summary>
    /// Shows the window fullscreen on an output. This does not tell the client. Call <see cref="InformFullscreen(bool)"/> for that.
    /// </summary>
    /// <param name="output">The output that the window fills.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public void Fullscreen(WmOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _wm.EnsureManage(nameof(Fullscreen));
        _proxy.Fullscreen(output.Proxy);
    }

    /// <summary>
    /// Ends the fullscreen display that <see cref="Fullscreen(WmOutput)"/> started. This does not tell the client.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    public void ExitFullscreen()
    {
        _wm.EnsureManage(nameof(ExitFullscreen));
        _proxy.ExitFullscreen();
    }

    /// <inheritdoc/>
    public void Show()
    {
        _wm.EnsureRender(nameof(Show));
        _proxy.Show();
    }

    /// <inheritdoc/>
    public void Hide()
    {
        _wm.EnsureRender(nameof(Hide));
        _proxy.Hide();
    }

    /// <inheritdoc/>
    public void SetBorders(Edges edges, int width, WmColor color)
    {
        _wm.EnsureRender(nameof(SetBorders));
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        _proxy.SetBorders((RiverWindowV1.Edges)edges, width, color.R, color.G, color.B, color.A);
    }

    /// <summary>
    /// Removes the borders that <see cref="SetBorders"/> set.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    public void ClearBorders() => SetBorders(Edges.None, 0, WmColor.Transparent);

    /// <inheritdoc/>
    public void SetClipBox(Rect box)
    {
        _wm.EnsureRender(nameof(SetClipBox));
        _wm.RequireVersion(2, "set_clip_box");
        ArgumentOutOfRangeException.ThrowIfNegative(box.Width);
        ArgumentOutOfRangeException.ThrowIfNegative(box.Height);
        _proxy.SetClipBox(box.X, box.Y, box.Width, box.Height);
    }

    /// <inheritdoc/>
    public void SetContentClipBox(Rect box)
    {
        _wm.EnsureRender(nameof(SetContentClipBox));
        _wm.RequireVersion(3, "set_content_clip_box");
        ArgumentOutOfRangeException.ThrowIfNegative(box.Width);
        ArgumentOutOfRangeException.ThrowIfNegative(box.Height);
        _proxy.SetContentClipBox(box.X, box.Y, box.Width, box.Height);
    }

    /// <summary>
    /// Gives a surface the decoration role above the window in the render order. It needs no sequence.
    /// </summary>
    /// <param name="surface">A surface with no role and no buffer attached or committed yet.</param>
    /// <returns>The decoration, which the caller owns and disposes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    public WmDecoration CreateDecorationAbove(Wayland.WlSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        WmThreadAffinity.Assert();
        return new WmDecoration(_wm, _proxy.GetDecorationAbove(surface), surface);
    }

    /// <summary>
    /// Gives a surface the decoration role below the window in the render order. It needs no sequence.
    /// </summary>
    /// <param name="surface">A surface with no role and no buffer attached or committed yet.</param>
    /// <returns>The decoration, which the caller owns and disposes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    public WmDecoration CreateDecorationBelow(Wayland.WlSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        WmThreadAffinity.Assert();
        return new WmDecoration(_wm, _proxy.GetDecorationBelow(surface), surface);
    }

    /// <inheritdoc/>
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
