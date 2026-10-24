namespace Basin.XWayland;

/// <summary>
/// One X11 window that an <see cref="XWaylandWm"/> tracks, with the properties that the window manager reads from X and the requests that the client makes. Positions and sizes are logical unless a name says X11.
/// </summary>
public sealed class XWaylandWindow : Basin.Capabilities.IToplevelHandle
{
    private readonly XWaylandWm _wm;

    internal XWaylandWindow(XWaylandWm wm, uint windowId, bool overrideRedirect, int x, int y, int width, int height)
    {
        _wm = wm;
        WindowId = windowId;
        OverrideRedirect = overrideRedirect;
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets the X11 window id.
    /// </summary>
    public uint WindowId { get; }

    /// <summary>
    /// Gets the window manager that tracks the window.
    /// </summary>
    public XWaylandWm WindowManager => _wm;

    /// <summary>
    /// Gets a value indicating whether the window bypasses the window manager, for example a menu or a tooltip. It can change when the window maps again.
    /// </summary>
    public bool OverrideRedirect { get; internal set; }

    /// <summary>
    /// Gets the left edge in layout coordinates.
    /// </summary>
    public int X { get; internal set; }

    /// <summary>
    /// Gets the top edge in layout coordinates.
    /// </summary>
    public int Y { get; internal set; }

    /// <summary>
    /// Gets the width in logical pixels.
    /// </summary>
    public int Width { get; internal set; }

    /// <summary>
    /// Gets the height in logical pixels.
    /// </summary>
    public int Height { get; internal set; }

    /// <summary>
    /// Gets the position and size in X11 root pixels, as the X server has them.
    /// </summary>
    public Box X11Bounds { get; internal set; }

    /// <summary>
    /// Gets the title from <c>_NET_WM_NAME</c>, or else from <c>WM_NAME</c>. It is empty when the client set neither.
    /// </summary>
    public string Title { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the instance name, the first string of <c>WM_CLASS</c>.
    /// </summary>
    public string Instance { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the class name, the second string of <c>WM_CLASS</c>. It serves as the application id.
    /// </summary>
    public string Class { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the window that <c>WM_TRANSIENT_FOR</c> names, or <see langword="null"/> when it names none or an unknown window.
    /// </summary>
    public XWaylandWindow? TransientFor { get; internal set; }

    /// <summary>
    /// Gets the process of the client. It is the default value until a pid is known.
    /// </summary>
    public ProcessIdentity Process { get; internal set; }

    /// <summary>
    /// Gets the pid of the client, or 0 when it is unknown.
    /// </summary>
    public int Pid => Process.Pid;

    /// <summary>
    /// Gets a value indicating whether the X server reported the pid. When it is <see langword="false"/>, the pid comes from <c>_NET_WM_PID</c>, which the client can set to any value.
    /// </summary>
    public bool PidVerified { get; internal set; }

    /// <summary>
    /// Gets the Steam app id from the <c>STEAM_GAME</c> property, or <see langword="null"/> when it is absent or 0.
    /// </summary>
    public uint? SteamGame { get; internal set; }

    /// <summary>
    /// Gets the first known <c>_NET_WM_WINDOW_TYPE</c> in lowercase without its prefix, for example <c>dialog</c> or <c>popup_menu</c>. Without one, it is <c>dialog</c> for a transient window and <c>normal</c> otherwise.
    /// </summary>
    public string WindowType { get; internal set; } = "normal";

    /// <summary>
    /// Gets the value of <c>_GTK_THEME_VARIANT</c>, for example <c>dark</c>. It is empty when the client set none.
    /// </summary>
    public string ThemeVariant { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether <c>_NET_WM_STATE</c> holds <c>_NET_WM_STATE_MODAL</c>.
    /// </summary>
    public bool Modal { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the window is mapped on the X server. A mapped window can still wait for its Wayland surface.
    /// </summary>
    public bool IsMappedInX { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the window wants server-side decorations. It is <see langword="false"/> when <c>_MOTIF_WM_HINTS</c> turns them off.
    /// </summary>
    public bool WantsDecorations { get; internal set; } = true;

    /// <summary>
    /// Gets a value indicating whether activation gives the window keyboard focus. A focusless window type, a disabled Wine window and a 1x1 window do not take focus.
    /// A window that skips the taskbar and the pager while not fullscreen, or that has no input model, also does not take focus.
    /// </summary>
    public bool WantsFocus =>
        TypeWantsFocus && !IsUseless && (HwndStyle is not { } style || (style & WsDisabled) == 0) &&
        !(SkipTaskbar && SkipPager && !FullscreenState) && (AcceptsInput || TakesFocus);

    internal bool TypeWantsFocus { get; set; } = true;

    /// <summary>
    /// Gets a value indicating whether the input hint of <c>WM_HINTS</c> allows the window manager to set the focus. It is <see langword="true"/> when the hint is absent.
    /// </summary>
    public bool AcceptsInput { get; internal set; } = true;

    /// <summary>
    /// Gets a value indicating whether <c>WM_PROTOCOLS</c> holds <c>WM_TAKE_FOCUS</c>.
    /// </summary>
    public bool TakesFocus => SupportsTakeFocus;

    /// <summary>
    /// Gets a value indicating whether <c>_NET_WM_STATE</c> holds <c>_NET_WM_STATE_SKIP_TASKBAR</c>.
    /// </summary>
    public bool SkipTaskbar { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether <c>_NET_WM_STATE</c> holds <c>_NET_WM_STATE_SKIP_PAGER</c>.
    /// </summary>
    public bool SkipPager { get; internal set; }

    /// <summary>
    /// Gets the Win32 window style that Wine puts in <c>_WINE_HWND_STYLE</c>, or <see langword="null"/> for a window that is not from Wine.
    /// </summary>
    public uint? HwndStyle { get; internal set; }

    /// <summary>
    /// Gets the Win32 extended window style that Wine puts in <c>_WINE_HWND_EXSTYLE</c>, or <see langword="null"/> for a window that is not from Wine.
    /// </summary>
    public uint? HwndStyleEx { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the window is mapped at 1x1 X11 pixels or less, which some clients use as a hidden helper.
    /// </summary>
    public bool IsUseless => IsMappedInX && X11Bounds.Width <= 1 && X11Bounds.Height <= 1;

    /// <summary>
    /// Gets a value indicating whether the window is an override-redirect window of 1x1 pixels or less outside the X screen.
    /// </summary>
    public bool IsOffscreenStub { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether <c>WM_NORMAL_HINTS</c> has the program-specified position flag.
    /// </summary>
    public bool ProgramPosition { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether <c>WM_NORMAL_HINTS</c> has the user-specified position flag.
    /// </summary>
    public bool UserPosition { get; internal set; }

    /// <summary>
    /// Gets the window gravity from <c>WM_NORMAL_HINTS</c>, or <see langword="null"/> when the hints do not set it.
    /// </summary>
    public uint? Gravity { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the client placed the window itself with static gravity, as a popup at a fixed position does.
    /// </summary>
    public bool IsFixedPopup => ProgramPosition && Gravity == StaticGravity;

    internal uint? FocusChild { get; set; }

    private const uint WsDisabled = 0x08000000;

    private const uint StaticGravity = 10;

    private bool _wantsMaximized;

    /// <summary>
    /// Gets a value indicating whether <c>_NET_WM_STATE</c> holds a maximized state, unless <see cref="ForceUnmaximize"/> is set.
    /// </summary>
    public bool WantsMaximized
    {
        get => _wantsMaximized && !ForceUnmaximize;
        internal set => _wantsMaximized = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the window manager ignores the maximized state that the client asks for before the window first maps.
    /// </summary>
    public bool ForceUnmaximize { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a configure request that the policy filter refuses can still move the window.
    /// </summary>
    public bool AllowClientMove { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a fullscreen window can match an emulated RandR mode. The window then renders at that mode and the scene scales it.
    /// </summary>
    public bool HonorEmulatedModes { get; set; } = true;

    /// <summary>
    /// Gets the monitor boxes that Xwayland emulates for the client, from <c>_XWAYLAND_RANDR_EMU_MONITOR_RECTS</c>, in X11 pixels.
    /// </summary>
    public IReadOnlyList<Box> EmulatedModes { get; internal set; } = [];

    /// <summary>
    /// Gets the emulated mode that the window uses now, in X11 pixels, or <see langword="null"/> when it uses none.
    /// </summary>
    public Box? EmulatedMode { get; internal set; }

    internal Box EmulatedLogical { get; set; }

    /// <summary>
    /// Gets a value indicating whether the window is an override-redirect window at 0,0 with the root window size and an emulated mode.
    /// </summary>
    public bool CoversScreen { get; internal set; }

    /// <summary>
    /// Gets the X Shape bounding region in window-local X11 pixels, or <see langword="null"/> when the window is not shaped. The window owns the region.
    /// </summary>
    public Pixman.PixmanRegion32? BoundingShape { get; private set; }

    /// <summary>
    /// Gets the X Shape input region in window-local X11 pixels, or <see langword="null"/> when the window has no input shape. The window owns the region.
    /// </summary>
    public Pixman.PixmanRegion32? InputShape { get; private set; }

    /// <summary>
    /// Occurs when <see cref="BoundingShape"/> or <see cref="InputShape"/> changes. The previous regions are disposed before the event.
    /// </summary>
    public event Action? ShapeChanged;

    internal void SetShapes(Pixman.PixmanRegion32? bounding, Pixman.PixmanRegion32? input)
    {
        var changed = !SameRegion(BoundingShape, bounding) || !SameRegion(InputShape, input);
        if (!changed)
        {
            bounding?.Dispose();
            input?.Dispose();
            return;
        }

        BoundingShape?.Dispose();
        InputShape?.Dispose();
        BoundingShape = bounding;
        InputShape = input;
        ShapeChanged?.Invoke();

        static bool SameRegion(Pixman.PixmanRegion32? a, Pixman.PixmanRegion32? b) =>
            a is null ? b is null : b is not null && a.Equals(b);
    }

    /// <summary>
    /// Gets a value indicating whether <c>_NET_WM_STATE</c> holds <c>_NET_WM_STATE_FULLSCREEN</c>.
    /// </summary>
    public bool WantsFullscreen { get; internal set; }

    /// <summary>
    /// Gets the minimum width from <c>WM_NORMAL_HINTS</c> in logical pixels, or 0 when there is none.
    /// </summary>
    public int MinWidth { get; internal set; }

    /// <summary>
    /// Gets the minimum height from <c>WM_NORMAL_HINTS</c> in logical pixels, or 0 when there is none.
    /// </summary>
    public int MinHeight { get; internal set; }

    /// <summary>
    /// Gets the maximum width from <c>WM_NORMAL_HINTS</c> in logical pixels, or 0 when there is none.
    /// </summary>
    public int MaxWidth { get; internal set; }

    /// <summary>
    /// Gets the maximum height from <c>WM_NORMAL_HINTS</c> in logical pixels, or 0 when there is none.
    /// </summary>
    public int MaxHeight { get; internal set; }

    /// <summary>
    /// Gets the chosen image from <c>_NET_WM_ICON</c>, or <see langword="null"/> when the window has no icon.
    /// </summary>
    public XWaylandIcon? Icon { get; internal set; }

    /// <summary>
    /// Gets the D-Bus service of the application menu from <c>_KDE_NET_WM_APPMENU_SERVICE_NAME</c>. It is empty when the client set none.
    /// </summary>
    public string AppMenuServiceName { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the D-Bus object path of the application menu from <c>_KDE_NET_WM_APPMENU_OBJECT_PATH</c>. It is empty when the client set none.
    /// </summary>
    public string AppMenuObjectPath { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the Wayland surface that Xwayland paired with the window, or <see langword="null"/> before the pairing or after the surface is destroyed.
    /// </summary>
    public Surface? Surface { get; internal set; }

    internal ulong AssociationSerial { get; set; }

    internal bool SupportsDeleteWindow { get; set; }

    internal bool SupportsTakeFocus { get; set; }

    internal bool AnnouncedMapped { get; set; }

    /// <summary>
    /// Occurs when the window is mapped in X and has its surface. It follows <see cref="XWaylandWm.WindowMapped"/> or <see cref="XWaylandWm.OverrideRedirectMapped"/>.
    /// </summary>
    public event Action? Mapped;

    /// <summary>
    /// Occurs when a window that raised <see cref="Mapped"/> unmaps, or changes its override-redirect flag.
    /// </summary>
    public event Action? Unmapped;

    /// <summary>
    /// Occurs when <see cref="Title"/> changes.
    /// </summary>
    public event Action? TitleChanged;

    /// <summary>
    /// Occurs when an identity property changes, for example the class, the transient parent, the window type, the process or the minimized state.
    /// </summary>
    public event Action? PropertiesChanged;

    /// <summary>
    /// Occurs when the X server or the client changes the position or the size of the window.
    /// </summary>
    public event Action? GeometryChanged;

    /// <summary>
    /// Occurs when <see cref="WantsDecorations"/> changes.
    /// </summary>
    public event Action? DecorationsChanged;

    /// <summary>
    /// Occurs when <see cref="Icon"/> changes.
    /// </summary>
    public event Action? IconChanged;

    /// <summary>
    /// Occurs when <see cref="AppMenuServiceName"/> or <see cref="AppMenuObjectPath"/> changes.
    /// </summary>
    public event Action? AppMenuChanged;

    /// <summary>
    /// Occurs when the size limits or the position hints of <c>WM_NORMAL_HINTS</c> change.
    /// </summary>
    public event Action? SizeHintsChanged;

    /// <summary>
    /// Occurs when the X window is destroyed. The shape regions are disposed after the event.
    /// </summary>
    public event Action? Destroyed;

    /// <summary>
    /// Occurs when the client asks to minimize or to restore. The argument is <see langword="true"/> to minimize.
    /// </summary>
    public event Action<bool>? MinimizeRequested;

    /// <summary>
    /// Occurs when the client asks to maximize or to restore. The argument is <see langword="true"/> to maximize.
    /// </summary>
    public event Action<bool>? MaximizeRequested;

    /// <summary>
    /// Occurs when the client asks to enter or leave fullscreen. The argument is <see langword="true"/> to enter.
    /// </summary>
    public event Action<bool>? FullscreenRequested;

    /// <summary>
    /// Gets the pointer position, in layout coordinates, of the last <c>_NET_WM_MOVERESIZE</c> request.
    /// </summary>
    public Point MoveResizeOrigin { get; internal set; }

    /// <summary>
    /// Occurs when the client asks for an interactive move. The argument is <see langword="true"/> for a keyboard move.
    /// </summary>
    public event Action<bool>? MoveRequested;

    /// <summary>
    /// Occurs when the client asks for an interactive resize. The arguments are the edges and <see langword="true"/> for a keyboard resize.
    /// </summary>
    public event Action<X11ResizeEdges, bool>? ResizeRequested;

    /// <summary>
    /// Occurs when the client cancels an interactive move or resize.
    /// </summary>
    public event Action? MoveResizeCancelled;

    /// <summary>
    /// Gets or sets when the window is ready after <see cref="Configure(Transaction, int, int, int, int)"/>.
    /// </summary>
    public XWaylandReadiness Readiness { get; set; } = XWaylandReadiness.OnMatchingCommit;

    /// <summary>
    /// Moves and resizes the X window and sends it a synthetic <c>ConfigureNotify</c>. The X11 values keep the current ones when they round to the same logical values.
    /// </summary>
    /// <param name="x">The left edge in layout coordinates.</param>
    /// <param name="y">The top edge in layout coordinates.</param>
    /// <param name="width">The width in logical pixels.</param>
    /// <param name="height">The height in logical pixels.</param>
    public void Configure(int x, int y, int width, int height) => _wm.ConfigureWindow(this, x, y, width, height);

    /// <summary>
    /// Configures the window and joins <paramref name="transaction"/> until the surface commits as <see cref="Readiness"/> requires. A new configure or an unmap abandons the previous wait.
    /// </summary>
    /// <param name="transaction">The transaction to join. The window does not join when <see cref="Readiness"/> is <see cref="XWaylandReadiness.Immediate"/> or it has no live surface.</param>
    /// <param name="x">The left edge in layout coordinates.</param>
    /// <param name="y">The top edge in layout coordinates.</param>
    /// <param name="width">The width in logical pixels.</param>
    /// <param name="height">The height in logical pixels.</param>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> is <see langword="null"/>.</exception>
    public void Configure(Transaction transaction, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ReleaseParticipant();
        Configure(x, y, width, height);

        if (Readiness == XWaylandReadiness.Immediate || Surface is not { IsDestroyed: false } surface)
        {
            return;
        }

        _participant = transaction.Join();
        _awaitedWidth = width;
        _awaitedHeight = height;
        _awaiting = true;
        _awaitedSurface = surface;
        surface.Committed += OnSurfaceCommitted;
        transaction.Completed += OnTransactionCompleted;
    }

    private TransactionParticipant _participant;
    private Surface? _awaitedSurface;
    private int _awaitedWidth;
    private int _awaitedHeight;
    private bool _awaiting;

    private void OnSurfaceCommitted()
    {
        if (!_awaiting || _awaitedSurface is not { } surface)
        {
            return;
        }

        var current = surface.Current;
        if (current.Buffer is null)
        {
            return;
        }

        if (Readiness == XWaylandReadiness.OnMatchingCommit &&
            (current.Width != _awaitedWidth || current.Height != _awaitedHeight))
        {
            return;
        }

        FinishAwait();
        _participant.Ready();
        _participant = default;
    }

    private void OnTransactionCompleted()
    {
        if (_awaiting && _participant.Transaction is { IsComplete: true })
        {
            FinishAwait();
            _participant = default;
        }
    }

    private void ReleaseParticipant()
    {
        if (!_awaiting)
        {
            return;
        }

        FinishAwait();
        var participant = _participant;
        _participant = default;
        if (participant.Transaction is { } transaction)
        {
            transaction.Completed -= OnTransactionCompleted;
        }

        participant.Abandon();
    }

    private void FinishAwait()
    {
        _awaiting = false;
        if (_awaitedSurface is { } surface)
        {
            surface.Committed -= OnSurfaceCommitted;
            _awaitedSurface = null;
        }
    }

    /// <summary>
    /// Gives the window the X input focus, sends <c>WM_TAKE_FOCUS</c> when the client supports it, and sets <c>_NET_ACTIVE_WINDOW</c>. It does nothing when <see cref="WantsFocus"/> is <see langword="false"/>.
    /// </summary>
    public void Activate() => _wm.ActivateWindow(this);

    /// <summary>
    /// Sets the maximized state and writes it to <c>_NET_WM_STATE</c>.
    /// </summary>
    /// <param name="maximized"><see langword="true"/> to tell the client that it is maximized.</param>
    public void SetMaximized(bool maximized) => _wm.SetWindowMaximized(this, maximized);

    /// <summary>
    /// Sets the fullscreen state and writes it to <c>_NET_WM_STATE</c>.
    /// </summary>
    /// <param name="fullscreen"><see langword="true"/> to tell the client that it is fullscreen.</param>
    public void SetFullscreen(bool fullscreen) => _wm.SetWindowFullscreen(this, fullscreen);

    /// <summary>
    /// Sets the minimized state and writes <c>_NET_WM_STATE</c> and <c>WM_STATE</c>. The compositor must hide the window itself.
    /// </summary>
    /// <param name="minimized"><see langword="true"/> for the iconic state.</param>
    public void SetMinimized(bool minimized) => _wm.SetWindowMinimized(this, minimized);

    /// <summary>
    /// Gets a value indicating whether the last <see cref="SetMinimized"/> call minimized the window.
    /// </summary>
    public bool Minimized { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the last <see cref="SetMaximized"/> call maximized the window.
    /// </summary>
    public bool IsMaximized => MaximizedState;

    /// <summary>
    /// Gets a value indicating whether the last <see cref="SetFullscreen"/> call made the window fullscreen.
    /// </summary>
    public bool IsFullscreen => FullscreenState;

    /// <summary>
    /// Gets or sets a value indicating whether the policy tiles the window. <see cref="XWaylandSceneDriver"/> uses it to choose the fit of a buffer of the wrong size.
    /// </summary>
    public bool Tiled { get; set; }

    /// <summary>
    /// Gets or sets how <see cref="XWaylandSceneDriver"/> fits a buffer that does not match the configured size.
    /// </summary>
    public Basin.Scene.SceneFitPolicy Letterbox { get; set; }

    internal bool MaximizedState { get; set; }

    internal bool FullscreenState { get; set; }

    string Basin.Capabilities.IToplevelHandle.AppId => Class;

    Basin.Capabilities.IToplevelHandle? Basin.Capabilities.IToplevelHandle.Parent => TransientFor;

    (int Width, int Height) Basin.Capabilities.IToplevelHandle.NaturalSize
    {
        get
        {
            var current = Surface?.Current;
            return (current?.Width ?? 0, current?.Height ?? 0);
        }
    }

    void Basin.Capabilities.IToplevelHandle.SetActivated(bool activated)
    {
        if (activated)
        {
            Activate();
        }
    }

    /// <summary>
    /// Sends <c>WM_DELETE_WINDOW</c> when the client supports it. Otherwise it kills the X client connection, which closes every window of that client.
    /// </summary>
    public void Close() => _wm.CloseWindow(this);

    /// <summary>
    /// Puts the window at the top of the X stacking order. It does not change the scene order.
    /// </summary>
    public void Raise() => _wm.RaiseWindow(this);

    /// <summary>
    /// Puts the window at the bottom of the X stacking order. It does not change the scene order.
    /// </summary>
    public void Lower() => _wm.LowerWindow(this);

    internal void RaiseMinimizeRequested(bool minimized) => MinimizeRequested?.Invoke(minimized);

    internal void RaiseMaximizeRequested(bool maximized) => MaximizeRequested?.Invoke(maximized);

    internal void RaiseFullscreenRequested(bool fullscreen) => FullscreenRequested?.Invoke(fullscreen);

    internal void RaiseSizeHintsChanged() => SizeHintsChanged?.Invoke();

    internal void RaiseMoveRequested(bool keyboard) => MoveRequested?.Invoke(keyboard);

    internal void RaiseResizeRequested(X11ResizeEdges edges, bool keyboard) => ResizeRequested?.Invoke(edges, keyboard);

    internal void RaiseMoveResizeCancelled() => MoveResizeCancelled?.Invoke();

    internal void RaiseMapped() => Mapped?.Invoke();

    internal void RaiseUnmapped()
    {
        ReleaseParticipant();
        Unmapped?.Invoke();
    }

    internal void RaiseTitleChanged() => TitleChanged?.Invoke();

    internal void RaiseAppMenuChanged() => AppMenuChanged?.Invoke();

    internal void RaisePropertiesChanged() => PropertiesChanged?.Invoke();

    internal event Action? StateChanged;

    internal void RaiseStateChanged() => StateChanged?.Invoke();

    internal void RaiseGeometryChanged() => GeometryChanged?.Invoke();

    internal void RaiseDecorationsChanged() => DecorationsChanged?.Invoke();

    internal void RaiseIconChanged() => IconChanged?.Invoke();

    internal void RaiseDestroyed()
    {
        ReleaseParticipant();
        Destroyed?.Invoke();
        BoundingShape?.Dispose();
        BoundingShape = null;
        InputShape?.Dispose();
        InputShape = null;
    }
}
