namespace Basin.XWayland;

public sealed class XWaylandWindow
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

    public bool OverrideRedirect { get; }

    public int X { get; internal set; }

    public int Y { get; internal set; }

    public int Width { get; internal set; }

    public int Height { get; internal set; }

    public string Title { get; internal set; } = string.Empty;

    public string Instance { get; internal set; } = string.Empty;

    public string Class { get; internal set; } = string.Empty;

    public XWaylandWindow? TransientFor { get; internal set; }

    public bool Modal { get; internal set; }

    public bool IsMappedInX { get; internal set; }

    public bool WantsDecorations { get; internal set; } = true;

    public bool WantsFocus { get; internal set; } = true;

    public XWaylandIcon? Icon { get; internal set; }

    public Surface? Surface { get; internal set; }

    internal ulong AssociationSerial { get; set; }

    internal bool SupportsDeleteWindow { get; set; }

    internal bool SupportsTakeFocus { get; set; }

    internal bool AnnouncedMapped { get; set; }

    public event Action? Mapped;

    public event Action? Unmapped;

    public event Action? TitleChanged;

    public event Action? GeometryChanged;

    public event Action? DecorationsChanged;

    public event Action? IconChanged;

    public event Action? Destroyed;

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
        if (_awaiting)
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

    public void Close() => _wm.CloseWindow(this);

    public void Raise() => _wm.RaiseWindow(this);

    internal void RaiseMapped() => Mapped?.Invoke();

    internal void RaiseUnmapped()
    {
        ReleaseParticipant();
        Unmapped?.Invoke();
    }

    internal void RaiseTitleChanged() => TitleChanged?.Invoke();

    internal void RaiseGeometryChanged() => GeometryChanged?.Invoke();

    internal void RaiseDecorationsChanged() => DecorationsChanged?.Invoke();

    internal void RaiseIconChanged() => IconChanged?.Invoke();

    internal void RaiseDestroyed()
    {
        ReleaseParticipant();
        Destroyed?.Invoke();
    }
}
