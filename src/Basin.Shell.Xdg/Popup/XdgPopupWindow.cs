using Basin.Seat;
using Basin.Shell.Xdg.Protocol;
using Wayland;

namespace Basin.Shell.Xdg;

public sealed class XdgPopupWindow
{
    public const string RoleName = "xdg_popup";

    private XdgPositionerRules _rules;
    private PopupGrab? _grab;
    private bool _dismissed;
    private bool _configureDeferred;

    internal XdgPopupWindow(XdgSurfaceState xdg, XdgPopupResource resource, XdgSurfaceState? parent, XdgPositionerRules rules)
    {
        XdgPopupRegistry.Register(resource, this);
        resource.Destroyed += (_, _) => XdgPopupRegistry.Remove(resource);
        Xdg = xdg;
        Resource = resource;
        Parent = parent;
        _rules = rules;
        Geometry = rules.Place();

        resource.Grab += (_, e) => OnGrabRequested(e.Serial);
        resource.Reposition += (_, e) => OnReposition(e.Positioner, e.Token);
        resource.Destroyed += (_, _) =>
        {
            _grab?.Remove(this);
            xdg.Surface.ClearRoleObject();
            xdg.OnRoleDestroyed();
            Destroyed?.Invoke();
        };
    }

    public XdgSurfaceState Xdg { get; }

    public Surface Surface => Xdg.Surface;

    internal XdgPopupResource Resource { get; }

    public XdgSurfaceState? Parent { get; }

    public LayerSurface? LayerParent { get; internal set; }

    public Box Geometry { get; private set; }

    public Point SurfacePosition
    {
        get
        {
            var geometry = Xdg.EffectiveGeometry;
            return new Point(Geometry.X - geometry.X, Geometry.Y - geometry.Y);
        }
    }

    public bool HasGrab => _grab is not null;

    public event Action? Destroyed;

    public event Action? GeometryChanged;

    public event Action? Repositioned;

    public void Unconstrain(Box constraint)
    {
        var geometry = _rules.Constrain(constraint);
        if (geometry != Geometry)
        {
            Geometry = geometry;
            if (_configureDeferred)
            {
                return;
            }

            SendConfigure();
            GeometryChanged?.Invoke();
        }
    }

    public void Dismiss()
    {
        if (_dismissed)
        {
            return;
        }

        _dismissed = true;
        _grab?.DismissChainFrom(this);
        _grab?.Remove(this);
        if (!Resource.IsDestroyed)
        {
            Resource.SendPopupDone();
        }
    }

    internal void SendInitialConfigure() => SendConfigure();

    internal void SendConfigure()
    {
        Resource.SendConfigure(Geometry.X, Geometry.Y, Geometry.Width, Geometry.Height);
        Xdg.SendConfigure();
    }

    internal void OnAckConfigure()
    {
    }

    private void OnGrabRequested(uint serial)
    {
        var seat = Xdg.Shell.Seat;
        if (seat is null || !seat.ValidateGrabSerial(serial))
        {
            Dismiss();
            return;
        }

        _grab = PopupGrab.GetOrCreate(seat);
        _grab.Add(this);
    }

    private void OnReposition(XdgPositionerResource? positionerResource, uint token)
    {
        if (!Xdg.Shell.TryGetPositioner(positionerResource, out var rules))
        {
            return;
        }

        _rules = rules;
        Geometry = rules.Place();

        _configureDeferred = true;
        try
        {
            Repositioned?.Invoke();
        }
        finally
        {
            _configureDeferred = false;
        }

        if (Resource.Version >= 3)
        {
            Resource.SendRepositioned(token);
        }

        SendConfigure();
        GeometryChanged?.Invoke();
    }
}
