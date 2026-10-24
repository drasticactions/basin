using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// The state of one <c>xdg_surface</c>: its window geometry, its configure serials and its mapped state. The toplevel or popup role object sits on top of it.
/// </summary>
public sealed class XdgSurfaceState : IXdgSurfaceHandler
{
    private readonly List<uint> _pendingConfigures = [];
    private Box _pendingGeometry;
    private bool _geometrySet;
    private bool _initialConfigureSent;
    private bool _configureAcked;
    private bool _ackedSinceCommit;
    private bool _mapped;
    private uint _awaitedSerial;
    private bool _awaitingSerial;
    private TransactionParticipant _participant;

    internal XdgSurfaceState(XdgShell shell, XdgWmBaseResource wmBase, uint id, Surface surface)
    {
        Shell = shell;
        WmBase = wmBase;
        var resource = new XdgSurfaceResource(wmBase.Client, wmBase.Version, id, this);
        Resource = resource;
        Surface = surface;

        resource.Destroyed += (_, _) =>
        {
            XdgSurfaceRegistry.Remove(resource);
            surface.Committed -= OnCommitted;
            surface.Destroyed -= OnSurfaceDestroyed;
            ReleaseParticipant();
            SetMapped(false);
        };

        surface.Committed += OnCommitted;
        surface.Destroyed += OnSurfaceDestroyed;
        XdgSurfaceRegistry.Register(resource, this);
    }

    void IXdgSurfaceHandler.Destroy(XdgSurfaceResource resource)
    {
    }

    void IXdgSurfaceHandler.GetToplevel(XdgSurfaceResource resource, in XdgSurfaceResource.GetToplevelEventArgs args)
    {
        if (!Surface.CanSetRole(XdgToplevelWindow.RoleName))
        {
            WmBase.PostError((uint)XdgWmBase.Error.Role, $"The surface already has the '{Surface.Role}' role.");
            return;
        }

        var toplevel = new XdgToplevelWindow(this, args.Id);
        Surface.TrySetRole(XdgToplevelWindow.RoleName, toplevel);
        Role = toplevel;
        Shell.EmitNewToplevel(toplevel);
    }

    void IXdgSurfaceHandler.GetPopup(XdgSurfaceResource resource, in XdgSurfaceResource.GetPopupEventArgs args)
    {
        if (!Surface.CanSetRole(XdgPopupWindow.RoleName))
        {
            WmBase.PostError((uint)XdgWmBase.Error.Role, $"The surface already has the '{Surface.Role}' role.");
            return;
        }

        if (!Shell.TryGetPositioner(args.Positioner, out var rules))
        {
            WmBase.PostError((uint)XdgWmBase.Error.InvalidPositioner, "The positioner is not complete. Set its size and anchor rectangle.");
            return;
        }

        var parent = args.Parent is { } parentResource ? XdgSurfaceRegistry.Resolve(parentResource) : null;
        var popup = new XdgPopupWindow(this, args.Id, parent, rules);
        Surface.TrySetRole(XdgPopupWindow.RoleName, popup);
        Role = popup;
        Shell.EmitNewPopup(popup);
    }

    void IXdgSurfaceHandler.SetWindowGeometry(XdgSurfaceResource resource, in XdgSurfaceResource.SetWindowGeometryEventArgs args)
    {
        _pendingGeometry = new Box(args.X, args.Y, args.Width, args.Height);
        _geometrySet = true;
    }

    void IXdgSurfaceHandler.AckConfigure(XdgSurfaceResource resource, in XdgSurfaceResource.AckConfigureEventArgs args) => OnAckConfigure(args.Serial);

    /// <summary>
    /// Gets the shell that made the surface.
    /// </summary>
    public XdgShell Shell { get; }

    /// <summary>
    /// Gets the <c>wl_surface</c> under the <c>xdg_surface</c>.
    /// </summary>
    public Surface Surface { get; }

    internal XdgWmBaseResource WmBase { get; }

    internal XdgSurfaceResource Resource { get; }

    /// <summary>
    /// Gets the role object, an <see cref="XdgToplevelWindow"/> or an <see cref="XdgPopupWindow"/>. It is <see langword="null"/> before the client assigns a role and after the role object is destroyed.
    /// </summary>
    public object? Role { get; private set; }

    /// <summary>
    /// Gets the window geometry in surface-local logical pixels that the last commit applied. It is empty when the client never set one.
    /// </summary>
    public Box WindowGeometry { get; private set; }

    /// <summary>
    /// Gets <see cref="WindowGeometry"/>, or the bounds of the current surface when the window geometry is empty.
    /// </summary>
    public Box EffectiveGeometry => WindowGeometry.IsEmpty
        ? new Box(0, 0, Surface.Current.Width, Surface.Current.Height)
        : WindowGeometry;

    /// <summary>
    /// Gets a value indicating whether the surface is mapped. A commit maps it when the <c>wl_surface</c> is mapped, and the destruction of the role, the <c>xdg_surface</c> or the <c>wl_surface</c> unmaps it.
    /// </summary>
    public bool IsMapped => _mapped;

    /// <summary>
    /// Gets the progress of the last configure that was sent with a <see cref="Transaction"/>.
    /// </summary>
    public ConfigureState ConfigureState { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the client did not acknowledge the newest configure yet.
    /// </summary>
    public bool HasUnackedConfigure => _pendingConfigures.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the client did not acknowledge the newest configure, or did not commit after its last acknowledgement.
    /// </summary>
    public bool IsConfigurePending => _pendingConfigures.Count > 0 || _ackedSinceCommit;

    /// <summary>
    /// Occurs at the commit that maps the surface.
    /// </summary>
    public event Action? Mapped;

    /// <summary>
    /// Occurs when a mapped surface unmaps.
    /// </summary>
    public event Action? Unmapped;

    /// <summary>
    /// Occurs after each commit that does not raise a protocol error, after <see cref="Mapped"/>. It does not occur for the initial commit that solicits the first configure.
    /// </summary>
    public event Action? Committed;

    /// <summary>
    /// Sends <c>xdg_surface.configure</c> and joins a transaction. The surface is ready when the client acknowledges this serial and commits a buffer. A later configure releases the earlier participant.
    /// </summary>
    /// <param name="transaction">The transaction that waits for the surface.</param>
    /// <returns>The configure serial.</returns>
    public uint SendConfigure(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ReleaseParticipant();

        var serial = SendConfigure();
        _awaitedSerial = serial;
        _awaitingSerial = true;
        _participant = transaction.Join();
        ConfigureState = ConfigureState.Inflight;
        transaction.Completed += OnTransactionCompleted;
        return serial;
    }

    /// <summary>
    /// Sends <c>xdg_surface.configure</c> with a new serial. The role object must send its own configure event first.
    /// </summary>
    /// <returns>The configure serial.</returns>
    public uint SendConfigure()
    {
        var serial = Shell.Display.NextSerial();
        _pendingConfigures.Add(serial);
        Resource.SendConfigure(serial);
        _initialConfigureSent = true;
        return serial;
    }

    private void OnAckConfigure(uint serial)
    {
        var index = _pendingConfigures.IndexOf(serial);
        if (index < 0)
        {
            Resource.PostError((uint)XdgSurface.Error.InvalidSerial, $"Serial {serial} does not match a sent configure.");
            return;
        }

        var awaitedIndex = _awaitingSerial ? _pendingConfigures.IndexOf(_awaitedSerial) : -1;
        _pendingConfigures.RemoveRange(0, index + 1);
        _configureAcked = true;
        _ackedSinceCommit = true;
        if (_awaitingSerial && awaitedIndex >= 0 && awaitedIndex <= index)
        {
            ConfigureState = ConfigureState.Acked;
        }

        (Role as XdgPopupWindow)?.OnAckConfigure();
    }

    /// <summary>
    /// Gets a value indicating whether the <c>wl_surface</c> committed at least once after the <c>xdg_surface</c> was created.
    /// </summary>
    public bool HasCommitted { get; private set; }

    private void OnCommitted()
    {
        HasCommitted = true;
        _ackedSinceCommit = false;
        if (_geometrySet)
        {
            WindowGeometry = _pendingGeometry;
            _geometrySet = false;
        }

        if (!_initialConfigureSent)
        {
            if (Surface.Current.Buffer is not null)
            {
                Resource.PostError((uint)XdgSurface.Error.UnconfiguredBuffer, "A buffer was committed before the initial configure.");
                return;
            }

            (Role as XdgToplevelWindow)?.SendConfigure();
            (Role as XdgPopupWindow)?.SendInitialConfigure();
            return;
        }

        if (Surface.Current.Buffer is not null && !_configureAcked)
        {
            Resource.PostError((uint)XdgSurface.Error.UnconfiguredBuffer, "A buffer was committed before ack_configure.");
            return;
        }

        if (ConfigureState == ConfigureState.Acked && Surface.Current.Buffer is not null)
        {
            ConfigureState = ConfigureState.Committed;
            _awaitingSerial = false;
            var participant = _participant;
            _participant = default;
            participant.Ready();
        }

        if (_mapped && !Surface.IsMapped)
        {
            ReleaseParticipant();
            _pendingConfigures.Clear();
            _initialConfigureSent = false;
            _configureAcked = false;
            _ackedSinceCommit = false;
        }

        SetMapped(Surface.IsMapped);
        (Role as XdgToplevelWindow)?.AdoptCommittedSize();
        Committed?.Invoke();
    }

    private void OnTransactionCompleted()
    {
        if (_awaitingSerial)
        {
            ConfigureState = ConfigureState.TimedOut;
            _awaitingSerial = false;
            _participant = default;
        }
    }

    private void ReleaseParticipant()
    {
        if (!_awaitingSerial)
        {
            return;
        }

        _awaitingSerial = false;
        var participant = _participant;
        _participant = default;
        if (participant.Transaction is { } transaction)
        {
            transaction.Completed -= OnTransactionCompleted;
        }

        participant.Abandon();
    }

    internal void OnRoleDestroyed()
    {
        Role = null;
        ReleaseParticipant();
        SetMapped(false);
    }

    private void OnSurfaceDestroyed()
    {
        ReleaseParticipant();
        SetMapped(false);
    }

    private void SetMapped(bool mapped)
    {
        if (_mapped == mapped)
        {
            return;
        }

        _mapped = mapped;
        if (mapped)
        {
            Mapped?.Invoke();
        }
        else
        {
            Unmapped?.Invoke();
        }
    }
}
