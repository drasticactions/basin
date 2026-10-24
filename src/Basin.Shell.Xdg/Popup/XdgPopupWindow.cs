using Basin.Seat;
using Basin.Shell.Xdg.Protocol;
using Wayland;

namespace Basin.Shell.Xdg;

/// <summary>
/// An <c>xdg_popup</c> role object. It places the popup from its positioner and runs the popup grab. The consumer places the popup in the scene and sets the constraint box.
/// </summary>
public sealed class XdgPopupWindow
{
    /// <summary>
    /// The role name that a popup sets on its <see cref="Basin.Surface"/>.
    /// </summary>
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

    /// <summary>
    /// Gets the <c>xdg_surface</c> state under the popup.
    /// </summary>
    public XdgSurfaceState Xdg { get; }

    /// <summary>
    /// Gets the <c>wl_surface</c> of the popup.
    /// </summary>
    public Surface Surface => Xdg.Surface;

    internal XdgPopupResource Resource { get; }

    /// <summary>
    /// Gets the <c>xdg_surface</c> that the client named as the parent. It is <see langword="null"/> for a popup without a parent, for example one that a layer surface adopts.
    /// </summary>
    public XdgSurfaceState? Parent { get; }

    /// <summary>
    /// Gets the layer surface that adopted the popup with <c>get_popup</c>, or <see langword="null"/> when none did.
    /// </summary>
    public LayerSurface? LayerParent { get; internal set; }

    /// <summary>
    /// Gets the box of the popup's window geometry, relative to the parent's window-geometry origin, in logical pixels.
    /// </summary>
    public Box Geometry { get; private set; }

    /// <summary>
    /// Gets the position of the popup's surface origin relative to the parent's window-geometry origin. It is <see cref="Geometry"/> less the popup's own geometry offset, and is the position to give a scene node.
    /// </summary>
    public Point SurfacePosition
    {
        get
        {
            var geometry = Xdg.EffectiveGeometry;
            return new Point(Geometry.X - geometry.X, Geometry.Y - geometry.Y);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the popup took an explicit grab with a valid serial.
    /// </summary>
    public bool HasGrab => _grab is not null;

    /// <summary>
    /// Occurs when the client destroys the popup's role object.
    /// </summary>
    public event Action? Destroyed;

    /// <summary>
    /// Occurs after a reposition or an <see cref="Unconstrain"/> changes <see cref="Geometry"/> and the new configure is sent.
    /// </summary>
    public event Action? GeometryChanged;

    /// <summary>
    /// Occurs when the client repositions the popup, after the new positioner rules replace the old ones. A handler calls <see cref="Unconstrain"/> here, before the configure goes out.
    /// </summary>
    public event Action? Repositioned;

    /// <summary>
    /// Applies the positioner's constraint adjustments against a box and sends a configure when the geometry changes. In a <see cref="Repositioned"/> handler, the configure waits for the end of the reposition.
    /// </summary>
    /// <param name="constraint">The box to keep the popup in, in the same coordinates as <see cref="Geometry"/>.</param>
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

    /// <summary>
    /// Dismisses the popup and every popup above it in the grab chain, and sends <c>popup_done</c>. Later calls do nothing. The client must destroy the popup.
    /// </summary>
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
