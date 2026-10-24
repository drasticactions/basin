using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// The state of one <c>xdg_surface</c>: its window geometry, its configure serials and its mapped state. The toplevel or popup role object sits on top of it.
/// </summary>
public sealed class XdgSurfaceState
{
    private readonly List<uint> _pendingConfigures = [];
    private Box _pendingGeometry;
    private bool _geometrySet;
    private bool _initialConfigureSent;
    private bool _configureAcked;
    private bool _mapped;
    private uint _awaitedSerial;
    private bool _awaitingSerial;
    private TransactionParticipant _participant;

    internal XdgSurfaceState(XdgShell shell, XdgWmBaseResource wmBase, XdgSurfaceResource resource, Surface surface)
    {
        Shell = shell;
        WmBase = wmBase;
        Resource = resource;
        Surface = surface;

        resource.GetToplevel += (_, e) =>
        {
            if (!Surface.CanSetRole(XdgToplevelWindow.RoleName))
            {
                wmBase.PostError((uint)XdgWmBase.Error.Role, $"surface already has the '{Surface.Role}' role");
                return;
            }

            var toplevelResource = new XdgToplevelResource(resource.Client, resource.Version, e.Id);
            var toplevel = new XdgToplevelWindow(this, toplevelResource);
            Surface.TrySetRole(XdgToplevelWindow.RoleName, toplevel);
            Role = toplevel;
            shell.EmitNewToplevel(toplevel);
        };

        resource.GetPopup += (_, e) =>
        {
            if (!Surface.CanSetRole(XdgPopupWindow.RoleName))
            {
                wmBase.PostError((uint)XdgWmBase.Error.Role, $"surface already has the '{Surface.Role}' role");
                return;
            }

            if (!shell.TryGetPositioner(e.Positioner, out var rules))
            {
                wmBase.PostError((uint)XdgWmBase.Error.InvalidPositioner, "positioner is incomplete");
                return;
            }

            var parent = e.Parent is { } parentResource ? XdgSurfaceRegistry.Resolve(parentResource) : null;
            var popupResource = new XdgPopupResource(resource.Client, resource.Version, e.Id);
            var popup = new XdgPopupWindow(this, popupResource, parent, rules);
            Surface.TrySetRole(XdgPopupWindow.RoleName, popup);
            Role = popup;
            shell.EmitNewPopup(popup);
        };

        resource.SetWindowGeometry += (_, e) =>
        {
            _pendingGeometry = new Box(e.X, e.Y, e.Width, e.Height);
            _geometrySet = true;
        };

        resource.AckConfigure += (_, e) => OnAckConfigure(e.Serial);
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
            Resource.PostError((uint)XdgSurface.Error.InvalidSerial, $"serial {serial} was never sent");
            return;
        }

        var awaitedIndex = _awaitingSerial ? _pendingConfigures.IndexOf(_awaitedSerial) : -1;
        _pendingConfigures.RemoveRange(0, index + 1);
        _configureAcked = true;
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
        if (_geometrySet)
        {
            WindowGeometry = _pendingGeometry;
            _geometrySet = false;
        }

        if (!_initialConfigureSent)
        {
            if (Surface.Current.Buffer is not null)
            {
                Resource.PostError((uint)XdgSurface.Error.UnconfiguredBuffer, "buffer committed before the initial configure");
                return;
            }

            (Role as XdgToplevelWindow)?.SendConfigure();
            (Role as XdgPopupWindow)?.SendInitialConfigure();
            return;
        }

        if (Surface.Current.Buffer is not null && !_configureAcked)
        {
            Resource.PostError((uint)XdgSurface.Error.UnconfiguredBuffer, "buffer committed before ack_configure");
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
