using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// The <c>xdg_wm_base</c> global. It keeps the client positioners and makes an <see cref="XdgSurfaceState"/> for each valid <c>get_xdg_surface</c> request.
/// </summary>
public sealed class XdgShell : IDisposable
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
        var wmBase = new XdgWmBaseResource(client, version, id);

        wmBase.CreatePositioner += (_, e) =>
        {
            var resource = new XdgPositionerResource(client, wmBase.Version, e.Id);
            _positioners[resource] = default;
            resource.Destroyed += (_, _) => _positioners.Remove(resource);
            WirePositioner(resource);
        };

        wmBase.GetXdgSurface += (_, e) =>
        {
            var surface = Compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                wmBase.PostError((uint)XdgWmBase.Error.Role, "The wl_surface is not known.");
                return;
            }

            if (surface.Current.Buffer is not null || surface.Pending.Buffer is not null)
            {
                wmBase.PostError((uint)XdgWmBase.Error.InvalidSurfaceState, "The surface already has a buffer. Make the xdg_surface before you attach a buffer.");
                return;
            }

            var resource = new XdgSurfaceResource(client, wmBase.Version, e.Id);
            _ = new XdgSurfaceState(this, wmBase, resource, surface);
        };

        _bases.Add(wmBase);
        wmBase.Destroyed += (_, _) => _bases.Remove(wmBase);
        wmBase.Pong += (_, e) => Ponged?.Invoke(client, e.Serial);
    }

    private void WirePositioner(XdgPositionerResource resource)
    {
        resource.SetSize += (_, e) =>
        {
            if (e.Width <= 0 || e.Height <= 0)
            {
                resource.PostError((uint)XdgPositioner.Error.InvalidInput, "The positioner size must be more than zero.");
                return;
            }

            ref var rules = ref RulesOf(resource);
            if (!Unsafe.IsNullRef(ref rules))
            {
                rules = rules with { Width = e.Width, Height = e.Height };
            }
        };
        resource.SetAnchorRect += (_, e) =>
        {
            if (e.Width < 0 || e.Height < 0)
            {
                resource.PostError((uint)XdgPositioner.Error.InvalidInput, "The anchor rectangle size must not be negative.");
                return;
            }

            ref var rules = ref RulesOf(resource);
            if (!Unsafe.IsNullRef(ref rules))
            {
                rules = rules with { AnchorRect = new Box(e.X, e.Y, e.Width, e.Height) };
            }
        };
        resource.SetAnchor += (_, e) =>
        {
            ref var rules = ref RulesOf(resource);
            if (!Unsafe.IsNullRef(ref rules))
            {
                rules = rules with { Anchor = e.Anchor };
            }
        };
        resource.SetGravity += (_, e) =>
        {
            ref var rules = ref RulesOf(resource);
            if (!Unsafe.IsNullRef(ref rules))
            {
                rules = rules with { Gravity = e.Gravity };
            }
        };
        resource.SetConstraintAdjustment += (_, e) =>
        {
            ref var rules = ref RulesOf(resource);
            if (!Unsafe.IsNullRef(ref rules))
            {
                rules = rules with { ConstraintAdjustment = e.ConstraintAdjustment };
            }
        };
        resource.SetOffset += (_, e) =>
        {
            ref var rules = ref RulesOf(resource);
            if (!Unsafe.IsNullRef(ref rules))
            {
                rules = rules with { OffsetX = e.X, OffsetY = e.Y };
            }
        };
        resource.SetReactive += (_, _) =>
        {
            ref var rules = ref RulesOf(resource);
            if (!Unsafe.IsNullRef(ref rules))
            {
                rules = rules with { Reactive = true };
            }
        };
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
