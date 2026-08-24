using Basin.Plasma.Protocol;

namespace Basin.Plasma;

public sealed class PlasmaShellSurface
{
    private readonly OrgKdePlasmaSurfaceResource _resource;
    private bool _removed;

    internal PlasmaShellSurface(Surface surface, OrgKdePlasmaSurfaceResource resource)
    {
        Surface = surface;
        _resource = resource;
        resource.SetOutput += (_, e) =>
        {
            Output = OutputGlobal.FromResource(e.Output)?.Output;
            OutputChanged?.Invoke();
        };
        resource.SetPosition += (_, e) =>
        {
            Position = new Point(e.X, e.Y);
            HasPosition = true;
            PositionChanged?.Invoke();
        };
        resource.SetRole += (_, e) => ApplyRole(e.Role);
        resource.SetPanelBehavior += (_, e) => PanelBehavior = e.Flag switch
        {
            (uint)OrgKdePlasmaSurface.PanelBehavior.AutoHide => PlasmaPanelBehavior.AutoHide,
            (uint)OrgKdePlasmaSurface.PanelBehavior.WindowsCanCover => PlasmaPanelBehavior.WindowsCanCover,
            (uint)OrgKdePlasmaSurface.PanelBehavior.WindowsGoBelow => PlasmaPanelBehavior.WindowsGoBelow,
            _ => PlasmaPanelBehavior.AlwaysVisible,
        };
        resource.SetSkipTaskbar += (_, e) =>
        {
            SkipTaskbar = e.Skip != 0;
            SkipChanged?.Invoke();
        };
        resource.SetSkipSwitcher += (_, e) =>
        {
            SkipSwitcher = e.Skip != 0;
            SkipChanged?.Invoke();
        };
        resource.SetPanelTakesFocus += (_, e) =>
        {
            TakesFocus = e.TakesFocus != 0;
            TakesFocusChanged?.Invoke();
        };
        resource.PanelAutoHideHide += (_, _) => RequestAutoHide(hide: true);
        resource.PanelAutoHideShow += (_, _) => RequestAutoHide(hide: false);
        resource.OpenUnderCursor += (_, _) =>
        {
            if (surface.Current.Buffer is null && surface.Pending.Buffer is null)
            {
                OpenUnderCursor = true;
                OpenUnderCursorRequested?.Invoke();
            }
        };
        resource.Destroyed += (_, _) => Remove();
        surface.Destroyed += Remove;
    }

    public Surface Surface { get; }

    public PlasmaShellRole Role { get; private set; } = PlasmaShellRole.Normal;

    public bool HasRole { get; private set; }

    public Point Position { get; private set; }

    public bool HasPosition { get; private set; }

    public IOutput? Output { get; private set; }

    public PlasmaPanelBehavior PanelBehavior { get; private set; } = PlasmaPanelBehavior.AlwaysVisible;

    public bool SkipTaskbar { get; private set; }

    public bool SkipSwitcher { get; private set; }

    public bool TakesFocus { get; private set; }

    public bool OpenUnderCursor { get; internal set; }

    public bool IsAutoHidden { get; private set; }

    public bool IsDestroyed => _removed;

    public bool Focusable => Role switch
    {
        PlasmaShellRole.Normal => true,
        PlasmaShellRole.OnScreenDisplay or PlasmaShellRole.Tooltip => false,
        _ => TakesFocus,
    };

    public event Action? RoleChanged;

    public event Action? PositionChanged;

    public event Action? OutputChanged;

    public event Action? SkipChanged;

    public event Action? TakesFocusChanged;

    public event Action<bool>? AutoHideRequested;

    public event Action? OpenUnderCursorRequested;

    public event Action? Destroyed;

    public void NotifyAutoHidden()
    {
        IsAutoHidden = true;
        if (_resource.SupportsSendAutoHiddenPanelHidden && !_resource.IsDestroyed)
        {
            _resource.SendAutoHiddenPanelHidden();
        }
    }

    public void NotifyAutoShown()
    {
        IsAutoHidden = false;
        if (_resource.SupportsSendAutoHiddenPanelShown && !_resource.IsDestroyed)
        {
            _resource.SendAutoHiddenPanelShown();
        }
    }

    private void ApplyRole(uint requested)
    {
        if (HasRole)
        {
            return;
        }

        HasRole = true;
        Role = requested <= (uint)OrgKdePlasmaSurface.Role.Appletpopup
            ? (PlasmaShellRole)requested
            : PlasmaShellRole.Normal;
        RoleChanged?.Invoke();
    }

    private void RequestAutoHide(bool hide)
    {
        if (Role != PlasmaShellRole.Panel)
        {
            _resource.PostError(
                (uint)OrgKdePlasmaSurface.Error.PanelNotAutoHide, "not an auto-hide panel");
            return;
        }

        AutoHideRequested?.Invoke(hide);
    }

    private void Remove()
    {
        if (_removed)
        {
            return;
        }

        _removed = true;
        Surface.Destroyed -= Remove;
        Destroyed?.Invoke();
    }
}
