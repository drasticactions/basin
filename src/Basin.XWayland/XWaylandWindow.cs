namespace Basin.XWayland;

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

    public uint WindowId { get; }

    public XWaylandWm WindowManager => _wm;

    public bool OverrideRedirect { get; internal set; }

    public int X { get; internal set; }

    public int Y { get; internal set; }

    public int Width { get; internal set; }

    public int Height { get; internal set; }

    public Box X11Bounds { get; internal set; }

    public string Title { get; internal set; } = string.Empty;

    public string Instance { get; internal set; } = string.Empty;

    public string Class { get; internal set; } = string.Empty;

    public XWaylandWindow? TransientFor { get; internal set; }

    public ProcessIdentity Process { get; internal set; }

    public int Pid => Process.Pid;

    public bool PidVerified { get; internal set; }

    public uint? SteamGame { get; internal set; }

    public string WindowType { get; internal set; } = "normal";

    public string ThemeVariant { get; internal set; } = string.Empty;

    public bool Modal { get; internal set; }

    public bool IsMappedInX { get; internal set; }

    public bool WantsDecorations { get; internal set; } = true;

    public bool WantsFocus =>
        TypeWantsFocus && !IsUseless && (HwndStyle is not { } style || (style & WsDisabled) == 0) &&
        !(SkipTaskbar && SkipPager && !FullscreenState) && (AcceptsInput || TakesFocus);

    internal bool TypeWantsFocus { get; set; } = true;

    public bool AcceptsInput { get; internal set; } = true;

    public bool TakesFocus => SupportsTakeFocus;

    public bool SkipTaskbar { get; internal set; }

    public bool SkipPager { get; internal set; }

    public uint? HwndStyle { get; internal set; }

    public uint? HwndStyleEx { get; internal set; }

    public bool IsUseless => IsMappedInX && X11Bounds.Width <= 1 && X11Bounds.Height <= 1;

    public bool IsOffscreenStub { get; internal set; }

    public bool ProgramPosition { get; internal set; }

    public bool UserPosition { get; internal set; }

    public uint? Gravity { get; internal set; }

    public bool IsFixedPopup => ProgramPosition && Gravity == StaticGravity;

    internal uint? FocusChild { get; set; }

    private const uint WsDisabled = 0x08000000;

    private const uint StaticGravity = 10;

    private bool _wantsMaximized;

    public bool WantsMaximized
    {
        get => _wantsMaximized && !ForceUnmaximize;
        internal set => _wantsMaximized = value;
    }

    public bool ForceUnmaximize { get; set; }

    public bool AllowClientMove { get; set; }

    public bool HonorEmulatedModes { get; set; } = true;

    public IReadOnlyList<Box> EmulatedModes { get; internal set; } = [];

    public Box? EmulatedMode { get; internal set; }

    internal Box EmulatedLogical { get; set; }

    public bool CoversScreen { get; internal set; }

    public Pixman.PixmanRegion32? BoundingShape { get; private set; }

    public Pixman.PixmanRegion32? InputShape { get; private set; }

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

    public bool WantsFullscreen { get; internal set; }

    public int MinWidth { get; internal set; }

    public int MinHeight { get; internal set; }

    public int MaxWidth { get; internal set; }

    public int MaxHeight { get; internal set; }

    public XWaylandIcon? Icon { get; internal set; }

    public string AppMenuServiceName { get; internal set; } = string.Empty;

    public string AppMenuObjectPath { get; internal set; } = string.Empty;

    public Surface? Surface { get; internal set; }

    internal ulong AssociationSerial { get; set; }

    internal bool SupportsDeleteWindow { get; set; }

    internal bool SupportsTakeFocus { get; set; }

    internal bool AnnouncedMapped { get; set; }

    public event Action? Mapped;

    public event Action? Unmapped;

    public event Action? TitleChanged;

    public event Action? PropertiesChanged;

    public event Action? GeometryChanged;

    public event Action? DecorationsChanged;

    public event Action? IconChanged;

    public event Action? AppMenuChanged;

    public event Action? SizeHintsChanged;

    public event Action? Destroyed;

    public event Action<bool>? MinimizeRequested;

    public event Action<bool>? MaximizeRequested;

    public event Action<bool>? FullscreenRequested;

    public event Action<bool>? MoveRequested;

    public event Action<X11ResizeEdges, bool>? ResizeRequested;

    public event Action? MoveResizeCancelled;

    public XWaylandReadiness Readiness { get; set; } = XWaylandReadiness.OnMatchingCommit;

    public void Configure(int x, int y, int width, int height) => _wm.ConfigureWindow(this, x, y, width, height);

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

    public void Activate() => _wm.ActivateWindow(this);

    public void SetMaximized(bool maximized) => _wm.SetWindowMaximized(this, maximized);

    public void SetFullscreen(bool fullscreen) => _wm.SetWindowFullscreen(this, fullscreen);

    public void SetMinimized(bool minimized) => _wm.SetWindowMinimized(this, minimized);

    public bool Minimized { get; internal set; }

    public bool IsMaximized => MaximizedState;

    public bool IsFullscreen => FullscreenState;

    public bool Tiled { get; set; }

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

    public void Close() => _wm.CloseWindow(this);

    public void Raise() => _wm.RaiseWindow(this);

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
