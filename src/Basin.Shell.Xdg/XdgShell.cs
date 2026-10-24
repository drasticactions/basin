using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// The <c>xdg_wm_base</c> global. It keeps the client positioners and makes an <see cref="XdgSurfaceState"/> for each valid <c>get_xdg_surface</c> request.
/// </summary>
public sealed class XdgShell : IDisposable, IXdgWmBaseHandler, IXdgPositionerHandler
{
    /// <summary>
    /// The version of <c>xdg_wm_base</c> that the global advertises.
    /// </summary>
    public const int Version = 7;

    private readonly WlGlobal _global;
    private readonly Dictionary<XdgPositionerResource, XdgPositionerRules> _positioners = [];
    private readonly List<XdgWmBaseResource> _bases = [];
    private uint _pingSerial;

    /// <summary>
    /// Initializes a new instance of the <see cref="XdgShell"/> class. It creates the global on <paramref name="display"/> at once.
    /// </summary>
    /// <param name="display">The display that advertises the global.</param>
    /// <param name="compositor">The compositor global that resolves the client's surfaces.</param>
    /// <param name="seat">The seat that validates move, resize and popup grab serials. Without a seat, every such request is ignored and every popup grab dismisses the popup.</param>
    public XdgShell(WlServerDisplay display, CompositorGlobal compositor, Basin.Seat.Seat? seat = null)
    {
        Display = display;
        Compositor = compositor;
        Seat = seat;
        _global = display.CreateGlobal(XdgWmBase.Interface, Version, OnBind);
    }

    /// <summary>
    /// Gets the display that advertises the global. Its event loop runs the coalesced configures.
    /// </summary>
    public WlServerDisplay Display { get; }

    /// <summary>
    /// Gets the compositor global that resolves the client's surfaces.
    /// </summary>
    public CompositorGlobal Compositor { get; }

    /// <summary>
    /// Gets the seat that validates grab serials, or <see langword="null"/> when the shell has none.
    /// </summary>
    public Basin.Seat.Seat? Seat { get; }

    /// <summary>
    /// Occurs when a client gives an <c>xdg_surface</c> the toplevel role, before its initial commit. A handler sets <see cref="XdgToplevelWindow.WmCapabilities"/> here.
    /// </summary>
    public event Action<XdgToplevelWindow>? NewToplevel;

    /// <summary>
    /// Occurs when a client gives an <c>xdg_surface</c> the popup role, before its initial commit. A popup that a layer surface adopts has no <see cref="XdgPopupWindow.Parent"/> at this time.
    /// </summary>
    public event Action<XdgPopupWindow>? NewPopup;

    /// <summary>
    /// Occurs when a client answers a ping. The arguments are the client and the serial that it answered.
    /// </summary>
    public event Action<WlClient, uint>? Ponged;

    /// <summary>
    /// Gets each client that has a live <c>xdg_wm_base</c> resource, once each. Each read makes a new array.
    /// </summary>
    public IReadOnlyList<WlClient> BoundClients =>
        _bases.Where(b => !b.IsDestroyed).Select(b => b.Client).Distinct().ToArray();

    /// <summary>
    /// Sends <c>ping</c> on each <c>xdg_wm_base</c> resource of a client, with one serial for all of them. <see cref="Ponged"/> reports the answer.
    /// </summary>
    /// <param name="client">The client to ping.</param>
    /// <returns>The serial that was sent, or 0 when the client has no live <c>xdg_wm_base</c> resource.</returns>
    public uint Ping(WlClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        var serial = 0u;
        foreach (var wmBase in _bases)
        {
            if (wmBase.IsDestroyed || wmBase.Client != client)
            {
                continue;
            }

            if (serial == 0)
            {
                serial = ++_pingSerial;
            }

            wmBase.SendPing(serial);
        }

        return serial;
    }

    /// <summary>
    /// Destroys the global. Resources that exist keep working until the clients destroy them.
    /// </summary>
    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var wmBase = new XdgWmBaseResource(client, version, id, this);
        _bases.Add(wmBase);
        wmBase.Destroyed += (_, _) => _bases.Remove(wmBase);
    }

    void IXdgWmBaseHandler.Destroy(XdgWmBaseResource resource)
    {
    }

    void IXdgWmBaseHandler.CreatePositioner(XdgWmBaseResource resource, in XdgWmBaseResource.CreatePositionerEventArgs args)
    {
        var positioner = new XdgPositionerResource(resource.Client, resource.Version, args.Id, this);
        _positioners[positioner] = default;
        positioner.Destroyed += (_, _) => _positioners.Remove(positioner);
    }

    void IXdgWmBaseHandler.GetXdgSurface(XdgWmBaseResource resource, in XdgWmBaseResource.GetXdgSurfaceEventArgs args)
    {
        var surface = Compositor.ResolveSurface(args.Surface);
        if (surface is null)
        {
            resource.PostError((uint)XdgWmBase.Error.Role, "The wl_surface is not known.");
            return;
        }

        if (surface.Current.Buffer is not null || surface.Pending.Buffer is not null)
        {
            resource.PostError((uint)XdgWmBase.Error.InvalidSurfaceState, "The surface already has a buffer. Make the xdg_surface before you attach a buffer.");
            return;
        }

        _ = new XdgSurfaceState(this, resource, args.Id, surface);
    }

    void IXdgWmBaseHandler.Pong(XdgWmBaseResource resource, in XdgWmBaseResource.PongEventArgs args) => Ponged?.Invoke(resource.Client, args.Serial);

    void IXdgPositionerHandler.Destroy(XdgPositionerResource resource)
    {
    }

    void IXdgPositionerHandler.SetSize(XdgPositionerResource resource, in XdgPositionerResource.SetSizeEventArgs args)
    {
        if (args.Width <= 0 || args.Height <= 0)
        {
            resource.PostError((uint)XdgPositioner.Error.InvalidInput, "The positioner size must be more than zero.");
            return;
        }

        ref var rules = ref RulesOf(resource);
        if (!Unsafe.IsNullRef(ref rules))
        {
            rules = rules with { Width = args.Width, Height = args.Height };
        }
    }

    void IXdgPositionerHandler.SetAnchorRect(XdgPositionerResource resource, in XdgPositionerResource.SetAnchorRectEventArgs args)
    {
        if (args.Width < 0 || args.Height < 0)
        {
            resource.PostError((uint)XdgPositioner.Error.InvalidInput, "The anchor rectangle size must not be negative.");
            return;
        }

        ref var rules = ref RulesOf(resource);
        if (!Unsafe.IsNullRef(ref rules))
        {
            rules = rules with { AnchorRect = new Box(args.X, args.Y, args.Width, args.Height) };
        }
    }

    void IXdgPositionerHandler.SetAnchor(XdgPositionerResource resource, in XdgPositionerResource.SetAnchorEventArgs args)
    {
        ref var rules = ref RulesOf(resource);
        if (!Unsafe.IsNullRef(ref rules))
        {
            rules = rules with { Anchor = args.Anchor };
        }
    }

    void IXdgPositionerHandler.SetGravity(XdgPositionerResource resource, in XdgPositionerResource.SetGravityEventArgs args)
    {
        ref var rules = ref RulesOf(resource);
        if (!Unsafe.IsNullRef(ref rules))
        {
            rules = rules with { Gravity = args.Gravity };
        }
    }

    void IXdgPositionerHandler.SetConstraintAdjustment(XdgPositionerResource resource, in XdgPositionerResource.SetConstraintAdjustmentEventArgs args)
    {
        ref var rules = ref RulesOf(resource);
        if (!Unsafe.IsNullRef(ref rules))
        {
            rules = rules with { ConstraintAdjustment = args.ConstraintAdjustment };
        }
    }

    void IXdgPositionerHandler.SetOffset(XdgPositionerResource resource, in XdgPositionerResource.SetOffsetEventArgs args)
    {
        ref var rules = ref RulesOf(resource);
        if (!Unsafe.IsNullRef(ref rules))
        {
            rules = rules with { OffsetX = args.X, OffsetY = args.Y };
        }
    }

    void IXdgPositionerHandler.SetReactive(XdgPositionerResource resource)
    {
        ref var rules = ref RulesOf(resource);
        if (!Unsafe.IsNullRef(ref rules))
        {
            rules = rules with { Reactive = true };
        }
    }

    void IXdgPositionerHandler.SetParentSize(XdgPositionerResource resource, in XdgPositionerResource.SetParentSizeEventArgs args)
    {
    }

    void IXdgPositionerHandler.SetParentConfigure(XdgPositionerResource resource, in XdgPositionerResource.SetParentConfigureEventArgs args)
    {
    }

    private ref XdgPositionerRules RulesOf(XdgPositionerResource resource) =>
        ref CollectionsMarshal.GetValueRefOrNullRef(_positioners, resource);

    internal bool TryGetPositioner(XdgPositionerResource? resource, out XdgPositionerRules rules)
    {
        if (resource is not null && _positioners.TryGetValue(resource, out rules) && rules.IsComplete)
        {
            return true;
        }

        rules = default;
        return false;
    }

    internal void EmitNewToplevel(XdgToplevelWindow toplevel) => NewToplevel?.Invoke(toplevel);

    internal void EmitNewPopup(XdgPopupWindow popup) => NewPopup?.Invoke(popup);
}
