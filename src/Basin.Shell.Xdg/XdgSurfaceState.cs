using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

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

    public XdgShell Shell { get; }

    public Surface Surface { get; }

    internal XdgWmBaseResource WmBase { get; }

    internal XdgSurfaceResource Resource { get; }

    public object? Role { get; private set; }

    public Box WindowGeometry { get; private set; }

    public Box EffectiveGeometry => WindowGeometry.IsEmpty
        ? new Box(0, 0, Surface.Current.Width, Surface.Current.Height)
        : WindowGeometry;

    public bool IsMapped => _mapped;

    public ConfigureState ConfigureState { get; private set; }

    public bool HasUnackedConfigure => _pendingConfigures.Count > 0;

    public event Action? Mapped;

    public event Action? Unmapped;

    public event Action? Committed;

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
